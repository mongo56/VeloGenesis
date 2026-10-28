using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Devices;
using VeloGenesis.Ride;

namespace VeloGenesis.UI;

/// <summary>Ride setup: mode, terrain shape and (for ERG) the generated workout.</summary>
public partial class SetupScreen : Screen
{
    public event Action<RideConfig> Start;
    public event Action Back;

    Button _freeBtn, _ergBtn;
    SpinBox _gradient, _gain, _distance, _ftp, _minutes;
    CheckButton _climb, _gels, _ghosts;
    OptionButton _workoutType;
    LineEdit _seed;
    Control _freeOnly, _ergOnly;
    WorkoutGraph _graph;
    Label _workoutInfo, _warning, _terrainSummary;
    RideMode _mode;
    int _workoutSeed = System.Environment.TickCount;

    protected override void Build()
    {
        Dim = 0.45f;
        var s = Settings.Current;
        _mode = s.LastMode == "Erg" ? RideMode.Erg : RideMode.FreeRide;

        var panel = UiTheme.Anchor(UiTheme.Panel(0.88f, 30), 0.5f, 0.5f, -720, 0, 1440, 0);
        AddChild(panel);
        var v = UiTheme.VBox(12);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("NEW RIDE", 48, UiTheme.Cyan, bold: true));

        var modes = UiTheme.HBox(16);
        var group = new ButtonGroup();
        _freeBtn = ModeButton("FREE RIDE\nTerrain sets the resistance · virtual gears", group);
        _ergBtn = ModeButton("ERG WORKOUT\nStructured intervals from your FTP", group);
        _freeBtn.Pressed += () => SetMode(RideMode.FreeRide);
        _ergBtn.Pressed += () => SetMode(RideMode.Erg);
        modes.AddChild(_freeBtn);
        modes.AddChild(_ergBtn);
        v.AddChild(modes);
        FirstFocus = _mode == RideMode.Erg ? _ergBtn : _freeBtn;

        v.AddChild(Section("TERRAIN"));
        _gradient = UiTheme.Spin(1, 15, 0.5, s.LastAvgGradient, " %");
        v.AddChild(Row("Average gradient", _gradient, "How steep the hills are."));
        _gain = UiTheme.Spin(0, 5000, 50, s.LastElevationGain, " m");
        v.AddChild(Row("Elevation gain", _gain, "Total climbing the course aims for over the ride. Forks let you go over or under."));
        _climb = new CheckButton { Text = "Climb today", ButtonPressed = s.LastClimb };
        _climb.AddThemeFontSizeOverride("font_size", 22);
        v.AddChild(Row("Sustained climbs", _climb, "On: long climbs appear on the harder branches. Off: rolling hills only."));
        _freeOnly = UiTheme.VBox(12);
        _distance = UiTheme.Spin(3, 300, 1, s.LastDistanceKm, " km");
        _freeOnly.AddChild(Row("Planned distance", _distance, "Paces the elevation budget. You can keep riding afterwards."));
        _gels = new CheckButton { Text = "Power gels", ButtonPressed = s.LastPowerGels };
        _gels.AddThemeFontSizeOverride("font_size", 22);
        _freeOnly.AddChild(Row("Power-ups", _gels, "Gold gels over the road (grab them in Z4+) charge the slipstream boost. Off: no gels, no slipstream."));
        _ghosts = new CheckButton { Text = "Ghost races", ButtonPressed = s.LastGhostRaces };
        _ghosts.AddThemeFontSizeOverride("font_size", 22);
        _freeOnly.AddChild(Row("Challenges", _ghosts, "A ghost rider sometimes challenges you on harder branches. Its pace is based on your FTP."));
        v.AddChild(_freeOnly);
        _seed = new LineEdit { PlaceholderText = "random", CustomMinimumSize = new Vector2(180, 0) };
        v.AddChild(Row("World seed", _seed, "Same seed + settings = same world. Leave empty for a new one."));
        _terrainSummary = UiTheme.Label("", 18, UiTheme.Dim);
        v.AddChild(_terrainSummary);

        _ergOnly = UiTheme.VBox(10);
        _ergOnly.AddChild(Section("WORKOUT"));
        var wrow = UiTheme.HBox(16);
        _workoutType = new OptionButton();
        foreach (var t in WorkoutGenerator.All) _workoutType.AddItem(WorkoutGenerator.Title(t), (int)t);
        _workoutType.Selected = Math.Max(0, Array.IndexOf(WorkoutGenerator.All, Enum.TryParse<WorkoutType>(s.LastWorkoutType, out var wt) ? wt : WorkoutType.SweetSpot));
        _workoutType.AddThemeFontSizeOverride("font_size", 22);
        wrow.AddChild(_workoutType);
        _minutes = UiTheme.Spin(20, 240, 5, s.LastWorkoutMinutes, " min");
        wrow.AddChild(_minutes);
        _ftp = UiTheme.Spin(80, 600, 1, s.Ftp, " W FTP");
        wrow.AddChild(_ftp);
        wrow.AddChild(UiTheme.Button("⟳ SHUFFLE", () => { _workoutSeed = System.Environment.TickCount; RefreshWorkout(); }, 20));
        _ergOnly.AddChild(wrow);
        _graph = new WorkoutGraph { CustomMinimumSize = new Vector2(0, 120), MouseFilter = MouseFilterEnum.Ignore };
        _ergOnly.AddChild(_graph);
        _workoutInfo = UiTheme.Label("", 18, UiTheme.Dim);
        _ergOnly.AddChild(_workoutInfo);
        v.AddChild(_ergOnly);

        _warning = UiTheme.Label("", 18, UiTheme.Amber);
        _warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(_warning);

        var buttons = UiTheme.HBox(16);
        buttons.AddChild(UiTheme.Button("◀  BACK", () => Back?.Invoke(), 24, 200));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        buttons.AddChild(spacer);
        _startBtn = UiTheme.Button("START  ▶", OnStart, 32, 320);
        buttons.AddChild(_startBtn);
        v.AddChild(buttons);

        _workoutType.ItemSelected += _ => RefreshWorkout();
        _minutes.ValueChanged += _ => { RefreshWorkout(); RefreshSummary(); };
        _ftp.ValueChanged += _ => RefreshWorkout();
        _gain.ValueChanged += _ => RefreshSummary();
        _distance.ValueChanged += _ => RefreshSummary();
        _gradient.ValueChanged += _ => RefreshSummary();

        (_mode == RideMode.Erg ? _ergBtn : _freeBtn).ButtonPressed = true;
        SetMode(_mode);
    }

    static Button ModeButton(string text, ButtonGroup group)
    {
        var b = UiTheme.Button(text, null, 26);
        b.ToggleMode = true;
        b.ButtonGroup = group;
        b.CustomMinimumSize = new Vector2(0, 96);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return b;
    }

    Button _startBtn;

    /// <summary>Trailer: switch mode as if the button was clicked.</summary>
    public void DemoSelectMode(RideMode m)
    {
        (m == RideMode.Erg ? _ergBtn : _freeBtn).ButtonPressed = true;
        SetMode(m);
    }

    /// <summary>Trailer: highlight the start button.</summary>
    public void DemoFocusStart() => _startBtn.GrabFocus();

    void SetMode(RideMode m)
    {
        _mode = m;
        _freeOnly.Visible = m == RideMode.FreeRide;
        _ergOnly.Visible = m == RideMode.Erg;
        RefreshWorkout();
        RefreshSummary();
        var t = DeviceManager.Trainer;
        _warning.Text = t.IsSimulated
            ? "No trainer connected – the keyboard simulator will be used (W/S or gamepad triggers change power). Pair your trainer under DEVICES."
            : $"Trainer: {t.Name}";
    }

    RideConfig MakeConfig()
    {
        int seed = string.IsNullOrWhiteSpace(_seed.Text) ? System.Environment.TickCount & 0x7FFFFFFF
            : int.TryParse(_seed.Text, out var n) ? n : _seed.Text.GetHashCode() & 0x7FFFFFFF;
        return new RideConfig
        {
            Mode = _mode,
            AvgGradient = (float)_gradient.Value,
            ElevationGain = (float)_gain.Value,
            DistanceKm = (float)_distance.Value,
            Climb = _climb.ButtonPressed,
            PowerGels = _mode == RideMode.Erg || _gels.ButtonPressed,
            GhostRaces = _mode == RideMode.FreeRide && _ghosts.ButtonPressed,
            Seed = seed,
            Workout = (WorkoutType)_workoutType.GetSelectedId(),
            WorkoutMinutes = (int)_minutes.Value,
            WorkoutSeed = _workoutSeed,
        };
    }

    void RefreshWorkout()
    {
        if (_graph == null) return;
        var cfg = MakeConfig();
        var w = WorkoutGenerator.Generate(cfg.Workout, cfg.WorkoutMinutes, cfg.WorkoutSeed);
        int ftp = (int)_ftp.Value;
        _graph.Workout = w;
        _graph.Ftp = ftp;
        _graph.QueueRedraw();
        float kj = 0, np4 = 0;
        foreach (var iv in w.Intervals)
        {
            float p = (iv.StartPct + iv.EndPct) * 0.5f * ftp;
            kj += p * iv.Duration / 1000f;
            np4 += MathF.Pow(p, 4) * iv.Duration;
        }
        float np = MathF.Pow(np4 / w.TotalDuration, 0.25f);
        float tss = w.TotalDuration * np * (np / ftp) / (ftp * 3600f) * 100f;
        _workoutInfo.Text = $"{w.Name}: {w.Description}   ·   {UiTheme.Time(w.TotalDuration)}   ·   ~{kj:0} kJ   ·   ~{tss:0} TSS";
    }

    void RefreshSummary()
    {
        if (_terrainSummary == null) return;
        var cfg = MakeConfig();
        _terrainSummary.Text = $"≈ {cfg.GainPerMetre * 1000f:0} m of climbing per km over {cfg.PlannedDistanceKm:0} km{(cfg.Mode == RideMode.Erg ? " (estimated from workout length)" : "")}. Every 1.5–3 km the road forks: one branch harder, one easier.";
    }

    void OnStart()
    {
        var cfg = MakeConfig();
        var s = Settings.Current;
        s.LastMode = cfg.Mode.ToString();
        s.LastAvgGradient = cfg.AvgGradient;
        s.LastElevationGain = cfg.ElevationGain;
        s.LastDistanceKm = cfg.DistanceKm;
        s.LastClimb = cfg.Climb;
        if (cfg.Mode == RideMode.FreeRide) { s.LastPowerGels = cfg.PowerGels; s.LastGhostRaces = cfg.GhostRaces; }
        s.LastWorkoutType = cfg.Workout.ToString();
        s.LastWorkoutMinutes = cfg.WorkoutMinutes;
        s.Ftp = (int)_ftp.Value;
        Settings.Save();
        Start?.Invoke(cfg);
    }
}
