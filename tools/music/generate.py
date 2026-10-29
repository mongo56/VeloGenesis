"""
Generates the VeloGenesis soundtrack with ACE-Step 1.5 (https://github.com/ACE-Step/ACE-Step-1.5, MIT).

Every song is a complete 2½-minute instrumental written in one pass: mellow, warm 80s synthwave with soft
clean drums, with a light flavour per biome. The tracks are loudness-normalised and encoded to .ogg in
assets/music/<biome>/, and assets/music/soundtrack.json lists them for the game.

Setup (CUDA GPU, ~10 GB of models download on first run):

    git clone https://github.com/ACE-Step/ACE-Step-1.5.git
    cd ACE-Step-1.5 && uv sync
    uv run python <repo>/tools/music/generate.py --acestep . --per-biome 9

Existing tracks are kept; rerunning adds songs until each biome has --per-biome of them. Tracks can also be
imported from earlier runs with --import <wav> --biome <key> --seed <n> (prompt = the biome's prompt).
"""

import argparse
import json
import os
import random
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "music")

STYLE = ("warm mellow dreamy synthwave, soft clean retro drum machine, gentle snare, lush analog pads, "
         "shimmering arpeggios, smooth round analog bass, relaxed steady groove, golden hour, classic 80s, instrumental")
NEGATIVE = ("dubstep, wobble bass, bass drop, brostep, growl bass, glitch, trap, aggressive EDM, distorted bass, "
            "crushed drums, bitcrushed, compressed drums, pumping sidechain, harsh, saturated, lo-fi crunch, heavy kick, industrial")
BIOMES = {
    "sunset": ("warm sunset synthwave, " + STYLE, 96),
    "metropolis": ("night city synthwave, neon reflections, " + STYLE, 98),
    "desert": ("open road desert synthwave, wide and airy, " + STYLE, 94),
    "aurora": ("starry night synthwave, spacey, " + STYLE, 90),
}
SECONDS = 150

TITLE_A = ["Neon", "Chrome", "Midnight", "Velvet", "Electric", "Laser", "Crystal", "Turbo", "Solar", "Vapor", "Cobalt",
           "Magenta", "Static", "Prism", "Hyper", "Golden", "Polar", "Radiant", "Amber", "Silver", "Lunar", "Coral"]
TITLE_B = ["Horizon", "Boulevard", "Runner", "Skyline", "Pulse", "Mirage", "Highway", "Drift", "Signal", "Dream",
           "Overdrive", "Cascade", "Sunrise", "Velocity", "Echoes", "Afterglow", "Frontier", "Orbit", "Coast", "Glow"]


def load_manifest():
    path = os.path.join(OUT, "soundtrack.json")
    return json.load(open(path)) if os.path.exists(path) else {"tracks": []}


def save_manifest(m):
    with open(os.path.join(OUT, "soundtrack.json"), "w") as f:
        json.dump(m, f, indent=2)


def add_track(manifest, wav, biome, seed, rng):
    used = {t["title"] for t in manifest["tracks"]}
    title = None
    while title is None or title in used:
        title = f"{rng.choice(TITLE_A)} {rng.choice(TITLE_B)}"
    file = f"{biome}/{title.lower().replace(' ', '_')}.ogg"
    os.makedirs(os.path.join(OUT, biome), exist_ok=True)
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", wav, "-af", "loudnorm=I=-16:TP=-1.5:LRA=11",
                    "-ar", "44100", "-c:a", "libvorbis", "-q:a", "4", os.path.join(OUT, file)], check=True)
    manifest["tracks"].append({"title": title, "biome": biome, "file": file, "prompt": BIOMES[biome][0],
                               "bpm": BIOMES[biome][1], "seed": seed, "model": "ACE-Step 1.5 turbo"})
    save_manifest(manifest)
    print(f"[{biome}] {title} (seed {seed}) -> {file}", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--acestep", help="ACE-Step-1.5 checkout (with its models)")
    ap.add_argument("--per-biome", type=int, default=9)
    ap.add_argument("--seed", type=int, default=2400)
    ap.add_argument("--import", dest="import_wav")
    ap.add_argument("--biome", choices=list(BIOMES))
    ap.add_argument("--track-seed", type=int, default=-1)
    args = ap.parse_args()

    manifest = load_manifest()
    rng = random.Random(args.seed + len(manifest["tracks"]))
    if args.import_wav:
        add_track(manifest, args.import_wav, args.biome, args.track_seed, rng)
        return

    ace = os.path.abspath(args.acestep)
    sys.path.insert(0, ace)
    os.chdir(ace)
    from acestep.handler import AceStepHandler
    from acestep.inference import GenerationConfig, GenerationParams, generate_music
    from acestep.llm_inference import LLMHandler

    dit = AceStepHandler()
    dit.initialize_service(project_root=ace, config_path="acestep-v15-turbo", device="cuda")
    lm = LLMHandler()
    lm.initialize(checkpoint_dir=os.path.join(ace, "checkpoints"), lm_model_path="acestep-5Hz-lm-1.7B", backend="pt", device="cuda")

    tmp = tempfile.mkdtemp()
    for b, (biome, (caption, bpm)) in enumerate(BIOMES.items()):
        k = 0
        while sum(t["biome"] == biome for t in manifest["tracks"]) < args.per_biome:
            seed = args.seed + b * 100 + k
            k += 1
            params = GenerationParams(caption=caption, lyrics="[Instrumental]", instrumental=True, duration=SECONDS,
                                      bpm=bpm, shift=3.0, seed=seed, lm_negative_prompt=NEGATIVE)
            config = GenerationConfig(batch_size=1, use_random_seed=False, seeds=[seed], audio_format="wav")
            result = generate_music(dit, lm, params, config, save_dir=tmp)
            if not result.success:
                print(f"[{biome}] seed {seed} failed: {result.error}", flush=True)
                continue
            add_track(manifest, result.audios[0]["path"], biome, seed, rng)
    shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
