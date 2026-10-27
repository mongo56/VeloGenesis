using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.Audio;

/// <summary>
/// Chooses between the procedural synth and the player's own tracks in Documents/VeloGenesis/Music.
/// "Both" shuffles your files with ~4 minute procedural interludes. "External" silences the game's music
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

    static bool External => Settings.Current.MusicSource == "External";

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
        _files.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, s.MusicVolume));
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
            _procTurn = false;
            Synth.MusicOn = false;
            _ = ExternalPlayer.Start();
            SetNowPlaying(ExternalNowPlaying);
        }
        else if (s.MusicSource == "Procedural" || _playlist.Count == 0)
        {
            _files.Stop();
            Synth.MusicOn = true;
            SetNowPlaying(_playlist.Count == 0 && s.MusicSource != "Procedural" ? $"{Synth.SongTitle} (no files in Music folder)" : Synth.SongTitle);
        }
        else if (!_files.Playing && !_procTurn) Next();
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

    public void Next()
    {
        var s = Settings.Current;
        if (External)
        {
            ExternalPlayer.Next();
            return;
        }
        if (s.MusicSource == "Procedural" || _playlist.Count == 0)
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
        if (_procTurn)
        {
            _procTimer -= delta;
            if (_procTimer <= 0) Next();
        }
    }
}
