# VeloGenesis

Procedural neon cycling worlds for Bluetooth smart trainers. Every ride generates a new road that keeps
forking. Push harder to take the climb, or ease off to take the easier branch. The game is built with
Godot 4.7 (.NET / C#).

## Running

Requirements: Windows 10/11 with Bluetooth LE, the .NET SDK (8+; 10 is used here), and
**Godot 4.7.2 .NET** (`winget install GodotEngine.GodotEngine.Mono`).

- **Editor:** open `project.godot` in Godot .NET, then press F5.
- **Command line:** `godot --path .` (first run: `dotnet build` then `godot --path . --import`).

`nuget.config` pins nuget.org for this project, because the machine-wide NuGet config has no sources.

## Hardware

- **Trainer:** any Bluetooth FTMS trainer (Cycplus R200 included). Go to DEVICES → SCAN → "USE AS TRAINER".
  Close Zwift, the Cycplus app and other trainer apps first, because a trainer accepts only one controller.
- **Heart rate:** any standard BLE HR strap ("USE AS HR").
- **No hardware:** the keyboard simulator kicks in automatically. W/S or the gamepad triggers change power.

Devices are remembered and reconnect automatically on the next launch.

## Ride modes

| | Free ride | ERG workout |
|---|---|---|
| Resistance | FTMS simulation mode: grade × *trainer difficulty* | FTMS target power from the workout |
| ▲▼ / LB RB | Virtual gears (24 speeds) | Intensity bias ±5% |
| Fork choice | Power vs your 20 s baseline (±12%) | Cadence vs your 15 s baseline (±6 rpm) |

**Virtual shifting** keeps the bike in one gear. The game solves for the grade to send so that the
trainer's resistance at your cadence matches the virtual gear. It uses the trainer-reported flywheel
speed when the trainer provides it. To use your real gears instead, turn virtual shifting off in Settings.

**Terrain setup:** you set the average gradient, the total elevation gain, the planned distance and
whether to include sustained climbs. At each fork, one branch climbs more than the budget and the
other less. The branch marked **PLAN** keeps you on target.

## Power zones, gels and slipstream

The HUD shows your 7-zone power zone (Coggan: Z1 recovery … Z7 neuromuscular, from your FTP).
**Power gels** float over the road; they only light up and count when you ride through them in
**Z4 or higher**. Below that they're faint ghosts you pass straight through. Each gel gives a
slipstream charge (max 3). The ride summary shows time in each zone.

## Ghost races (free ride)

After you take a harder branch there's a 60% chance a holographic **ghost rider** pulls up in the
left lane and challenges you to a **FINISH** arch 0.4–1.8 km ahead. It rides the same terrain with the
same physics at a steady 85–108% of your FTP (shorter races are a bit harder). Beat it for a
slipstream charge. Power gels and ghost races can each be switched off on the ride setup screen.

## Controls

All bindings can be changed in **Settings → Controls / Key bindings** (defaults below).


| Action | Keyboard | Gamepad |
|---|---|---|
| Gear up / down | ↑ ↓ or E / Q | RB / LB, D-pad ↑↓ |
| Choose fork branch | ← → or A / D | D-pad ←→, left stick |
| Slipstream power-up | Space / B | A / X |
| Camera | C | Y |
| Screenshot | F12 | |
| Test rides only: time slower / faster (1×–8×) | F6 / F7 | |
| Toggle HUD | H | |
| Next music track | N | Back |
| Pause | Esc / P | Start |
| Simulator power | W / S | Triggers |

## Strava

**UPLOAD TO STRAVA** on the ride summary works without any setup. It opens Strava's web uploader and an
Explorer window with the ride's .fit file selected, and you drag the file onto the page.

**Direct upload (optional, needs a Strava subscription).** Since June 2026 Strava only lets subscribers
create API apps. If you have a subscription, you can connect the game for one-click or automatic uploads:

1. Go to **Settings → Strava → Open Strava API page** (strava.com/settings/api) and create an app.
   Any name and website work. Set **Authorization Callback Domain** to `localhost`.
2. Paste the **Client ID** and **Client Secret** into the Strava screen and click **Connect with Strava**.
   Approve access in the browser. The game listens on a random loopback port for the redirect.
3. Rides are then uploaded directly as trainer rides, named after the mode or workout, with sectors and
   races in the description. Uploading a ride a second time links to the existing activity.

Credentials and tokens are stored in `%APPDATA%/Godot/app_userdata/VeloGenesis/strava.json`.

## Files

- Rides: `Documents/VeloGenesis/Rides/*.fit` (cycling / virtual activity)
- Your music: `Documents/VeloGenesis/Music` (.mp3 / .ogg / .wav). Choose the source in Settings.
- Settings: `%APPDATA%/Godot/app_userdata/VeloGenesis/settings.json`

## Code map

```
src/Main.cs                 state machine: menu (attract-mode world) → setup → ride → pause → summary
src/Core/                   settings, quality presets, input map, main-thread dispatcher
src/Devices/                WinRT BLE: scanner, FTMS trainer (ERG + sim), HR strap, keyboard simulator
src/Ride/                   ride loop, physics, virtual gearing, fork decider, workouts, recorder, FIT writer
src/World/                  course generator, mesh builder (worker thread), streaming world, shaders, avatar
src/Online/                 Strava OAuth (loopback redirect) + FIT upload
src/Audio/                  procedural synthwave engine + music director
src/UI/                     neon theme, HUD, graphs, menus
```

Debug flags (after `--`): `--autoride=free|erg`, `--screen=setup|devices|settings|controls|strava|pause`,
`--shot=out.png --shot-after=8`, `--skip=metres`, `--timescale=6`, `--sim-power=300`, `--seed=n`,
`--quality=Low|Medium|High|Ultra`, `--finish-after=sec`, `--camera=n`, `--no-ble`.

## Roadmap

- Ideas: ghost of your previous ride, achievements for sectors, ANT+ FE-C, exported/installer builds.
