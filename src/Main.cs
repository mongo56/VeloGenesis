using System;
using Godot;
using VeloGenesis.Audio;
using VeloGenesis.Core;
using VeloGenesis.Devices;
using VeloGenesis.Ride;
using VeloGenesis.UI;

namespace VeloGenesis;

/// <summary>
/// App entry point and state machine: menu (with a self-riding attract world behind it),
/// setup, ride, pause and summary.
///
/// Debug command line (after "--"):
///   --autoride=free|erg   start a simulated ride immediately
///   --screen=setup|devices|settings|controls|strava|pause   open a screen
///   --shot=path.png --shot-after=8   save a screenshot then quit
///   --skip=metres  --camera=n  --finish-after=sec  --timescale=x  --sim-power=watts
///   --no-ble   skip Bluetooth auto-connect
///   --ghost / --self-ghost   start a ghost race (replaying your own best effort) asap
///   --pack     spawn a draft pack ahead
///   --rhythm   put a cadence rhythm zone just ahead
///   --biome=n  start in biome n (0 Sunset Strip, 1 Neon Metropolis, 2 Desert Grid, 3 Aurora Peaks)
///   --nohud    hide the ride HUD (for checking the 3D scene in screenshots)
///   --music=Procedural|Files|Both|External   override the music source for this run
/// </summary>
public partial class Main : Node
{
    MusicDirector _music;
    RideSession _ride;
    CanvasLayer _ui;
    Control _screen;
    PauseMenu _pause;

    string _shotPath;
    double _shotAfter = -1, _clock, _finishAfter = -1;
    int _cameraCycles, _seed = 4242, _slip;
    bool _noHud;
    bool _ghostDbg, _selfGhostDbg, _packDbg, _rhythmDbg;

    /// <summary>Set by --test (TestRide.bat): enables F6 / F7 time scaling.</summary>
    public static bool TestMode { get; private set; }
    static readonly double[] TimeScales = { 1, 2, 4, 8 };

    public override void _Ready()
    {
        Settings.Load();
        PersonalBests.Load();
        Online.Strava.Load();
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--quality=")) Settings.Current.Quality = a[10..];
            else if (a.StartsWith("--aa=")) Settings.Current.AntiAliasing = a[5..];
            else if (a.StartsWith("--music=")) Settings.Current.MusicSource = a[8..];
        InputSetup.Register();
        Quality.Get(Settings.Current.Quality).ApplyToViewport(GetViewport());
        DisplayServer.WindowSetTitle("VeloGenesis");

        _music = new MusicDirector();
        AddChild(_music);
        _ui = new CanvasLayer { Layer = 10 };
        AddChild(_ui);

        string autoride = null, screen = null;
        bool noBle = false, demo = false;
        float skip = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--autoride=")) autoride = a[11..];
            else if (a.StartsWith("--screen=")) screen = a[9..];
            else if (a.StartsWith("--shot=")) _shotPath = a[7..];
            else if (a.StartsWith("--shot-after=")) _shotAfter = double.Parse(a[13..], inv);
            else if (a.StartsWith("--skip=")) skip = float.Parse(a[7..], inv);
            else if (a.StartsWith("--finish-after=")) _finishAfter = double.Parse(a[15..], inv);
            else if (a.StartsWith("--camera=")) _cameraCycles = int.Parse(a[9..]);
            else if (a.StartsWith("--seed=")) _seed = int.Parse(a[7..]);
            else if (a.StartsWith("--slip=")) _slip = int.Parse(a[7..]);
            else if (a == "--nohud") _noHud = true;
            else if (a == "--ghost") _ghostDbg = true;
            else if (a == "--self-ghost") _ghostDbg = _selfGhostDbg = true;
            else if (a == "--pack") _packDbg = true;
            else if (a == "--rhythm") _rhythmDbg = true;
            else if (a.StartsWith("--biome=")) World.CourseGenerator.DebugStartBiome = int.Parse(a[8..]);
            else if (a.StartsWith("--timescale="))Engine.TimeScale = double.Parse(a[12..], inv);
            else if (a.StartsWith("--sim-power=")) DeviceManager.Simulator.Effort = float.Parse(a[12..], inv);
            else if (a == "--no-ble") noBle = true;
            else if (a == "--test") TestMode = true;
            else if (a == "--demo") { demo = true; noBle = true; }
        }
        if (_shotPath != null && _shotAfter < 0) _shotAfter = 8;

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--music-dump="))
            {
                _music.Synth.RenderToWav(a[13..], 240f, 0.6f);
                GD.Print($"Music rendered: {_music.Synth.SongTitle}");
                GetTree().Quit();
                return;
            }

        if (!noBle) DeviceManager.AutoConnect();

        if (demo)
        {
            // Trailer: fixed world, no in-game music (a soundtrack is added afterwards), quieter effects.
            var ds = Settings.Current;
            ds.Quality = "Ultra";
            ds.AntiAliasing = "TAA";
            ds.MusicSource = "Procedural";
            ds.MusicVolume = 0f;
            ds.SfxVolume = 0.5f;
            ds.Ftp = 230;
            ds.LastAvgGradient = 5f;
            ds.LastElevationGain = 600f;
            ds.LastDistanceKm = 20f;
            ds.LastClimb = true;
            Quality.Get(ds.Quality).ApplyToViewport(GetViewport());
            _music.ApplySettings();
            _seed = 2;
        }
        if (demo)
        {
            ShowMenu();
            AddChild(new DemoDirector(this));
            return;
        }

        if (autoride != null)
        {
            var s = Settings.Current;
            StartRide(new RideConfig
            {
                Mode = autoride == "erg" ? RideMode.Erg : RideMode.FreeRide,
                AvgGradient = s.LastAvgGradient,
                ElevationGain = s.LastElevationGain,
                DistanceKm = s.LastDistanceKm,
                Climb = s.LastClimb,
                Seed = _seed,
                Workout = WorkoutType.SweetSpot,
                WorkoutMinutes = 60,
                WorkoutSeed = 7,
            });
            _ride.DebugSkip = skip;
            _ride.DebugCameraCycles = _cameraCycles;
            _ride.DebugSlip = _slip;
            _ride.DebugGhost = _ghostDbg;
            _ride.DebugSelfGhost = _selfGhostDbg;
            _ride.DebugPack = _packDbg;
            _ride.DebugRhythm = _rhythmDbg;
            if (_noHud) Callable.From(() => { if (_ride?.Hud != null) _ride.Hud.Visible = false; }).CallDeferred();
            if (screen == "pause") TogglePause();
        }
        else
        {
            ShowMenu();
            switch (screen)
            {
                case "setup": ShowSetup(); break;
                case "devices": ShowDevices(); break;
                case "settings": ShowSettings(); break;
                case "controls":
                    var cs = new ControlsScreen();
                    cs.Back += ShowSettings;
                    SetScreen(cs);
                    break;
                case "strava":
                    var ss = new StravaScreen();
                    ss.Back += ShowSettings;
                    SetScreen(ss);
                    break;
            }
        }
    }

    public override void _Process(double delta)
    {
        MainThread.Pump();
        if (_ride == null || !_ride.Attract) DeviceManager.Tick(delta);

        if (TestMode)
        {
            int dir = (Input.IsActionJustPressed(InputSetup.TimeFaster) ? 1 : 0) - (Input.IsActionJustPressed(InputSetup.TimeSlower) ? 1 : 0);
            if (dir != 0)
            {
                int idx = Math.Clamp(Array.IndexOf(TimeScales, Engine.TimeScale) + dir, 0, TimeScales.Length - 1);
                Engine.TimeScale = TimeScales[Math.Max(idx, 0)];
                _ride?.Hud?.Toast($"TIME ×{Engine.TimeScale:0}", UiTheme.Cyan);
            }
        }

        if (Input.IsActionJustPressed(InputSetup.Screenshot))
        {
            string file = System.IO.Path.Combine(Paths.Screenshots, $"VeloGenesis_{DateTime.Now:yyyy-MM-dd_HHmmss}.png");
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print($"Screenshot saved: {file}");
            _ride?.Hud?.Toast("SCREENSHOT SAVED", UiTheme.Cyan);
        }

        _clock += delta;
        if (_finishAfter > 0 && _clock >= _finishAfter && _ride != null && !_ride.Attract)
        {
            _finishAfter = -1;
            EndRide(true);
        }
        if (_shotPath != null && _clock >= _shotAfter)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(_shotPath);
            GD.Print($"Screenshot saved: {_shotPath}");
            _shotPath = null;
            GetTree().Quit();
        }
    }

    void SetScreen(Control screen)
    {
        if (_screen != null && IsInstanceValid(_screen)) _screen.QueueFree();
        _screen = screen;
        if (screen != null) _ui.AddChild(screen);
    }

    void ReplaceRide(RideSession ride)
    {
        if (_ride != null && IsInstanceValid(_ride)) _ride.QueueFree();
        _ride = ride;
        AddChild(ride);
    }

    void StartAttract()
    {
        ReplaceRide(new RideSession(new RideConfig
        {
            Mode = RideMode.FreeRide,
            AvgGradient = 5,
            ElevationGain = 600,
            DistanceKm = 20,
            Climb = true,
            Seed = System.Environment.TickCount & 0x7FFFFFFF,
        }, _music, true));
    }

    void ShowMenu()
    {
        _pause = null;
        if (_ride == null || !_ride.Attract) StartAttract();
        var m = new MainMenu();
        m.StartRide += ShowSetup;
        m.OpenDevices += ShowDevices;
        m.OpenSettings += ShowSettings;
        m.Quit += () => GetTree().Quit();
        SetScreen(m);
    }

    // ---- Trailer hooks (DemoDirector) ----
    public SetupScreen DemoShowSetup()
    {
        ShowSetup();
        return _screen as SetupScreen;
    }

    public RideSession DemoStartRide(RideConfig cfg)
    {
        StartRide(cfg);
        return _ride;
    }

    void ShowSetup()
    {
        var s = new SetupScreen();
        s.Back += ShowMenu;
        s.Start += StartRide;
        SetScreen(s);
    }

    void ShowDevices()
    {
        var d = new DevicesScreen();
        d.Back += ShowMenu;
        SetScreen(d);
    }

    void ShowSettings()
    {
        var st = new SettingsScreen();
        st.Back += ShowMenu;
        st.AudioChanged += _music.ApplySettings;
        st.VolumeChanged += _music.ApplyVolume;
        st.AntiAliasingChanged += () => Quality.Get(Settings.Current.Quality).ApplyToViewport(GetViewport());
        st.OpenControls += () =>
        {
            var c = new ControlsScreen();
            c.Back += ShowSettings;
            SetScreen(c);
        };
        st.OpenStrava += () =>
        {
            var c = new StravaScreen();
            c.Back += ShowSettings;
            SetScreen(c);
        };
        st.QualityChanged += () =>
        {
            Quality.Get(Settings.Current.Quality).ApplyToViewport(GetViewport());
            StartAttract();
        };
        SetScreen(st);
    }

    void StartRide(RideConfig cfg)
    {
        var ride = new RideSession(cfg, _music, false);
        ride.PauseRequested += TogglePause;
        ReplaceRide(ride);
        SetScreen(null);
    }

    void TogglePause()
    {
        if (_ride == null || _ride.Attract) return;
        if (_pause != null && IsInstanceValid(_pause))
        {
            _ride.Paused = false;
            _music.SetRidePaused(false);
            Settings.Save(); // keep volume changes made in the pause menu
            _pause = null;
            SetScreen(null);
            return;
        }
        _ride.Paused = true;
        _music.SetRidePaused(true);
        _pause = new PauseMenu();
        _pause.Resume += TogglePause;
        _pause.EndRide += () => EndRide(true);
        _pause.Discard += () => EndRide(false);
        _pause.VolumeChanged += _music.ApplyVolume;
        SetScreen(_pause);
    }

    void EndRide(bool save)
    {
        _pause = null;
        var summary = _ride.Finish(save);
        _ride.Paused = true;
        _ride.PauseRequested -= TogglePause;
        if (!save) { ShowMenu(); return; }
        if (summary.NewBests.Count > 0) _music.Synth.SfxPersonalBest();
        var sc = new SummaryScreen(summary);
        sc.Done += ShowMenu;
        SetScreen(sc);
    }

    public override void _ExitTree()
    {
        // Don't lose a ride if the window is closed mid-session.
        if (_ride != null && IsInstanceValid(_ride) && !_ride.Attract && !_ride.Finished && _ride.Recorder.Samples.Count > 60)
            _ride.Finish(true);
        DeviceManager.Shutdown();
    }
}
