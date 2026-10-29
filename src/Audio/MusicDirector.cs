using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.Audio;

/// <summary>
/// Chooses the music: the bundled ACE-Step soundtrack (default, songs picked by biome), the procedural synth,
/// or the player's own tracks in Documents/VeloGenesis/Music. "Both" shuffles your files with ~4 minute procedural interludes. "External" silences the game's music
/// and follows / controls another player (YouTube Music in the browser, Spotify…) via ExternalPlayer.
/// </summary>
public partial class MusicDirector : Node
{
    public SynthwaveEngine Synth { get; private set; }
    AudioStreamPlayer _files;
    List<string> _playlist = new();
    int _index = -1;
    bool _procTurn;
    double _procTimer;
    readonly Random _rng = new();
    bool _pausedExternal;

    // Soundtrack: two players so a new biome's song can crossfade in over the old one.
    readonly AudioStreamPlayer[] _ost = new AudioStreamPlayer[2];
    readonly float[] _ostFade = new float[2], _ostTarget = new float[2];
    int _ostCur;
    Soundtrack.Track _ostTrack;
    readonly HashSet<Soundtrack.Track> _ostPlayed = new();
    int _biome;
    const float CrossfadeSeconds = 3f;

    static bool External => Settings.Current.MusicSource == "External";
    static bool Ost => Settings.Current.MusicSource == "Soundtrack" && Soundtrack.Tracks.Count > 0;

    public string NowPlaying { get; private set; } = "";
    public event Action<string> TrackChanged;

    public override void _Ready()
    {
        EnsureBuses();
        Synth = new SynthwaveEngine();
        AddChild(Synth);
        Synth.SongChanged += name => { if (!_files.Playing && !External) SetNowPlaying(name); };
        ExternalPlayer.Changed += () => { if (External) SetNowPlaying(ExternalNowPlaying); };
        _files = new AudioStreamPlayer { Bus = "Master" };
        _files.Finished += Next;
        AddChild(_files);
        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            _ost[i] = new AudioStreamPlayer { Bus = "Master" };
            _ost[i].Finished += () => { if (idx == _ostCur && Ost) PlayOst(PickOst()); };
            AddChild(_ost[i]);
        }
        ApplySettings();
    }

    static void EnsureBuses()
    {
        if (AudioServer.GetBusIndex("Music") >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, "Music");
        AudioServer.SetBusSend(idx, "Master");
        AudioServer.AddBusEffect(idx, new AudioEffectReverb { RoomSize = 0.55f, Damping = 0.45f, Spread = 1f, Wet = 0.16f, Dry = 1f });
    }

    /// <summary>Applies volume sliders immediately without touching the playlist.</summary>
    public void ApplyVolume()
    {
        var s = Settings.Current;
        Synth.MusicVolume = s.MusicVolume;
        Synth.SfxVolume = s.SfxVolume;
        Synth.WindVolume = s.WindVolume;
        _files.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, s.MusicVolume));
        UpdateOstVolume();
    }

    public void ApplySettings()
    {
        var s = Settings.Current;
        ApplyVolume();
        _playlist = Directory.EnumerateFiles(Paths.Music)
            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => _rng.Next()).ToList();

        if (External)
        {
            _files.Stop();
            StopOst();
            _procTurn = false;
            Synth.MusicOn = false;
            _ = ExternalPlayer.Start();
            SetNowPlaying(ExternalNowPlaying);
        }
        else if (Ost)
        {
            _files.Stop();
            _procTurn = false;
            Synth.MusicOn = false;
            if (_ostTrack == null || !_ost[_ostCur].Playing) PlayOst(PickOst());
            else SetNowPlaying(OstTitle(_ostTrack));
        }
        else if (s.MusicSource is "Procedural" or "Soundtrack" || _playlist.Count == 0)
        {
            _files.Stop();
            StopOst();
            Synth.MusicOn = true;
            SetNowPlaying(_playlist.Count == 0 && s.MusicSource != "Procedural" ? $"{Synth.SongTitle} (no files in Music folder)" : Synth.SongTitle);
        }
        else
        {
            StopOst();
            if (!_files.Playing && !_procTurn) Next();
        }
    }

    static string ExternalNowPlaying =>
        ExternalPlayer.NowPlaying.Length > 0 ? ExternalPlayer.NowPlaying : "External player – start YouTube Music in your browser";

    /// <summary>Pauses the external player with the ride (and resumes it only if we paused it).</summary>
    public void SetRidePaused(bool paused)
    {
        if (!External) return;
        if (paused && ExternalPlayer.IsPlaying)
        {
            ExternalPlayer.Pause();
            _pausedExternal = true;
        }
        else if (!paused && _pausedExternal)
        {
            ExternalPlayer.Play();
            _pausedExternal = false;
        }
    }

    /// <summary>
    /// The ride is in biome <paramref name="biome"/> (index into Biome.All). The soundtrack crossfades to one of
    /// that biome's songs; the synth starts a new song when <paramref name="fresh"/> (a biome crossing).
    /// Your own tracks and the external player are never interrupted.
    /// </summary>
    public void EnterBiome(int biome, bool fresh = true)
    {
        _biome = biome;
        if (Ost)
        {
            if (_ostTrack?.Biome != Soundtrack.BiomeKey(biome)) PlayOst(PickOst());
        }
        else if (fresh && !External && !_files.Playing && Synth.MusicOn) Synth.NewSong();
    }

    public void Next()
    {
        var s = Settings.Current;
        if (External)
        {
            ExternalPlayer.Next();
            return;
        }
        if (Ost)
        {
            PlayOst(PickOst());
            return;
        }
        if (s.MusicSource is "Procedural" or "Soundtrack" || _playlist.Count == 0)
        {
            Synth.NewSong();
            return;
        }
        if (s.MusicSource == "Both" && !_procTurn && _index >= 0)
        {
            _procTurn = true;
            _procTimer = 240;
            _files.Stop();
            Synth.MusicOn = true;
            Synth.NewSong();
            return;
        }
        _procTurn = false;
        _index = (_index + 1) % _playlist.Count;
        var path = _playlist[_index];
        var stream = Load(path);
        if (stream == null) { _playlist.RemoveAt(_index); _index--; if (_playlist.Count > 0) Next(); return; }
        Synth.MusicOn = false;
        _files.Stream = stream;
        _files.Play();
        SetNowPlaying(Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>An unplayed song of the current biome, else any unplayed song, then starts the round again.</summary>
    Soundtrack.Track PickOst()
    {
        string key = Soundtrack.BiomeKey(_biome);
        var all = Soundtrack.Tracks;
        var fresh = all.Where(t => !_ostPlayed.Contains(t) && t != _ostTrack).ToList();
        if (fresh.Count == 0)
        {
            _ostPlayed.Clear();
            fresh = all.Where(t => t != _ostTrack).ToList();
            if (fresh.Count == 0) return _ostTrack ?? all[0];
        }
        var pool = fresh.Where(t => t.Biome == key).ToList();
        if (pool.Count == 0) pool = fresh;
        return pool[_rng.Next(pool.Count)];
    }

    void PlayOst(Soundtrack.Track t)
    {
        var stream = Soundtrack.Stream(t);
        if (stream == null) return;
        bool crossfade = _ost[_ostCur].Playing;
        _ostTarget[_ostCur] = 0;
        _ostCur ^= 1;
        var p = _ost[_ostCur];
        p.Stream = stream;
        p.Play();
        _ostFade[_ostCur] = crossfade ? 0 : 1;
        _ostTarget[_ostCur] = 1;
        UpdateOstVolume();
        _ostTrack = t;
        _ostPlayed.Add(t);
        SetNowPlaying(OstTitle(t));
    }

    void StopOst()
    {
        for (int i = 0; i < 2; i++) { _ost[i].Stop(); _ostFade[i] = _ostTarget[i] = 0; }
        _ostTrack = null;
    }

    static string OstTitle(Soundtrack.Track t) => $"{t.Title} · VeloGenesis soundtrack";

    void UpdateOstVolume()
    {
        for (int i = 0; i < 2; i++)
            _ost[i].VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, Settings.Current.MusicVolume * _ostFade[i] * _ostFade[i]));
    }

    static AudioStream Load(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp3" => new AudioStreamMP3 { Data = File.ReadAllBytes(path) },
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                ".wav" => AudioStreamWav.LoadFromFile(path),
                _ => null,
            };
        }
        catch (Exception e)
        {
            GD.PrintErr($"Could not load music '{path}': {e.Message}");
            return null;
        }
    }

    void SetNowPlaying(string name)
    {
        NowPlaying = name;
        TrackChanged?.Invoke(name);
    }

    public override void _Process(double delta)
    {
        bool fading = false;
        for (int i = 0; i < 2; i++)
        {
            if (_ostFade[i] == _ostTarget[i]) continue;
            fading = true;
            _ostFade[i] = Mathf.MoveToward(_ostFade[i], _ostTarget[i], (float)delta / CrossfadeSeconds);
            if (_ostFade[i] == 0 && i != _ostCur) _ost[i].Stop();
        }
        if (fading) UpdateOstVolume();

        if (_procTurn)
        {
            _procTimer -= delta;
            if (_procTimer <= 0) Next();
        }
    }
}
