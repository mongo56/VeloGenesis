using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Devices;
using VeloGenesis.Ride;
using VeloGenesis.World;

namespace VeloGenesis.UI;

/// <summary>In-ride heads-up display.</summary>
public partial class Hud : Control
{
    readonly RideSession _ride;

    Label _power, _powerSub, _cadence, _hr, _speed, _dist, _time, _gain, _grade, _gradeSub;
    Label _gearBig, _gearSub, _ergTarget, _ergInterval, _ergNext, _ergCountdown;
    Control _gearPanel, _ergPanel;
    ProfileGraph _profile;
    WorkoutGraph _workout;
    Minimap _map;
    Label _status, _toast, _bannerTop, _bannerName, _hint;
    PanelContainer _banner, _toastPanel, _footer;
    float _toastT, _bannerT;

    PanelContainer _forkPanel;
    Label _forkHeader, _forkHelp;
    readonly PanelContainer[] _forkOpt = new PanelContainer[2];
    readonly Label[] _forkName = new Label[2], _forkStats = new Label[2], _forkTag = new Label[2];
    ColorRect _leanBar, _leanFill;
    int _lastSel = -1;
    Label _slipCharges, _slipHint, _zoneLabel, _gelHint;
    ZoneBar _zoneBar;
    PanelContainer _racePanel;
    Label _raceTitle, _raceGap, _raceInfo;
    ColorRect _raceBar, _raceFill;
    ColorRect _slipBar, _slipFill;
    readonly Label[] _forkStake = new Label[2];
    PanelContainer _packPanel, _rhythmPanel;
    Label _packTitle, _packState, _packInfo, _rhythmHeader, _rhythmInfo;
    ColorRect _packBar, _packFill;
    RhythmGauge _rhythmGauge;

    // Steady mode: secondary panels fade out during long uneventful stretches.
    Control[] _secondary;
    Control _slipPanel;
    float _calm, _steadyAlpha = 1f;
    int _lastGear = -1, _lastBias;
    const float CalmDelay = 20f, SteadyAlpha = 0.1f;

    /// <summary>Hides the device status and key-hint lines (for trailers / screenshots).</summary>
    public void SetCleanMode(bool clean)
    {
        _status.Visible = !clean;
        _hint.Visible = !clean;
        _footer.Visible = !clean;
    }

    public Hud() { }

    public Hud(RideSession ride) => _ride = ride;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Theme;

        // Power / cadence / HR
        // Left column: power panel with the slipstream meter stacked below it.
        var column = UiTheme.Anchor(UiTheme.VBox(14), 0, 0, 24, 24, 340, 0);
        AddChild(column);
        var left = UiTheme.Panel();
        column.AddChild(left);
        var lv = UiTheme.VBox(2);
        left.AddChild(lv);
        lv.AddChild(UiTheme.CaptionLabel("POWER"));
        var prow = UiTheme.HBox(8);
        _power = UiTheme.Label("0", 96, UiTheme.Text, bold: true);
        prow.AddChild(_power);
        var wl = UiTheme.Label("W", 34, UiTheme.Dim);
        wl.VerticalAlignment = VerticalAlignment.Bottom;
        wl.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        prow.AddChild(wl);
        lv.AddChild(prow);
        _powerSub = UiTheme.Label("", 20, UiTheme.Dim);
        lv.AddChild(_powerSub);
        var zrow = UiTheme.HBox(10);
        _zoneLabel = UiTheme.Label("", 22, UiTheme.Text, bold: true);
        _zoneLabel.CustomMinimumSize = new Vector2(150, 0);
        zrow.AddChild(_zoneLabel);
        _zoneBar = new ZoneBar { CustomMinimumSize = new Vector2(150, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        zrow.AddChild(_zoneBar);
        lv.AddChild(zrow);
        var crow = UiTheme.HBox(36);
        crow.AddChild(Stat("CADENCE", out _cadence, 50));
        crow.AddChild(Stat("HEART", out _hr, 50));
        lv.AddChild(crow);

        // Slipstream power-up meter
        var slip = UiTheme.Panel();
        column.AddChild(slip);
        slip.Visible = _ride.Config.PowerGels;

        // Ghost race panel (under the minimap)
        _racePanel = UiTheme.Anchor(UiTheme.Panel(), 1, 0, -390, 320, 366, 0);
        AddChild(_racePanel);
        var rv = UiTheme.VBox(2);
        _racePanel.AddChild(rv);
        _raceTitle = UiTheme.CaptionLabel("");
        rv.AddChild(_raceTitle);
        _raceGap = UiTheme.Label("", 48, UiTheme.Text, bold: true);
        rv.AddChild(_raceGap);
        _raceInfo = UiTheme.Label("", 18, UiTheme.Dim);
        rv.AddChild(_raceInfo);
        _raceBar = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore };
        _raceFill = new ColorRect { Color = new Color(0.55f, 0.95f, 1f), MouseFilter = MouseFilterEnum.Ignore };
        _raceBar.AddChild(_raceFill);
        rv.AddChild(_raceBar);
        _racePanel.Visible = false;

        // Draft pack panel (same slot as the race panel: packs never ride into a race)
        _packPanel = UiTheme.Anchor(UiTheme.Panel(), 1, 0, -390, 320, 366, 0);
        AddChild(_packPanel);
        var pv = UiTheme.VBox(2);
        _packPanel.AddChild(pv);
        _packTitle = UiTheme.CaptionLabel("");
        pv.AddChild(_packTitle);
        _packState = UiTheme.Label("", 40, UiTheme.Text, bold: true);
        pv.AddChild(_packState);
        _packInfo = UiTheme.Label("", 18, UiTheme.Dim);
        pv.AddChild(_packInfo);
        _packBar = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore };
        _packFill = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        _packBar.AddChild(_packFill);
        pv.AddChild(_packBar);
        _packPanel.Visible = false;

        var sv = UiTheme.VBox(4);
        slip.AddChild(sv);
        var srow = UiTheme.HBox(12);
        srow.AddChild(UiTheme.CaptionLabel("SLIPSTREAM"));
        _slipHint = UiTheme.Label("", 16, UiTheme.Dim);
        _slipHint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _slipHint.HorizontalAlignment = HorizontalAlignment.Right;
        srow.AddChild(_slipHint);
        sv.AddChild(srow);
        _slipCharges = UiTheme.Label("", 34, UiTheme.Amber, bold: true);
        sv.AddChild(_slipCharges);
        _slipBar = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore };
        _slipFill = new ColorRect { Color = UiTheme.Amber, MouseFilter = MouseFilterEnum.Ignore };
        _slipBar.AddChild(_slipFill);
        sv.AddChild(_slipBar);
        _gelHint = UiTheme.Label("", 17, UiTheme.Dim);
        sv.AddChild(_gelHint);

        // Speed / distance / time / gain
        var top = UiTheme.Anchor(UiTheme.Panel(), 0.5f, 0, -450, 24, 900, 0);
        AddChild(top);
        var trow = UiTheme.HBox(54);
        trow.Alignment = BoxContainer.AlignmentMode.Center;
        top.AddChild(trow);
        trow.AddChild(Stat("SPEED  km/h", out _speed, 52));
        trow.AddChild(Stat("DISTANCE  km", out _dist, 52));
        trow.AddChild(Stat("TIME", out _time, 52));
        trow.AddChild(Stat("CLIMBED  m", out _gain, 52));

        _map = UiTheme.Anchor(new Minimap { World = _ride.World }, 1, 0, -300, 24, 276, 276);
        AddChild(_map);

        // Gear (free ride)
        var gp = UiTheme.Anchor(UiTheme.Panel(), 1, 0.5f, -330, 0, 306, 0);
        _gearPanel = gp;
        AddChild(gp);
        var gv = UiTheme.VBox(2);
        gp.AddChild(gv);
        gv.AddChild(UiTheme.CaptionLabel("GEAR"));
        _gearBig = UiTheme.Label("", 64, UiTheme.Text, bold: true);
        gv.AddChild(_gearBig);
        _gearSub = UiTheme.Label("", 18, UiTheme.Dim);
        gv.AddChild(_gearSub);

        // ERG
        var ep = UiTheme.Anchor(UiTheme.Panel(), 1, 0.5f, -390, 0, 366, 0);
        _ergPanel = ep;
        AddChild(ep);
        var ev = UiTheme.VBox(2);
        ep.AddChild(ev);
        ev.AddChild(UiTheme.CaptionLabel("ERG TARGET"));
        _ergTarget = UiTheme.Label("", 64, UiTheme.Text, bold: true);
        ev.AddChild(_ergTarget);
        _ergInterval = UiTheme.Label("", 22, UiTheme.Text);
        ev.AddChild(_ergInterval);
        _ergCountdown = UiTheme.Label("", 40, UiTheme.Text, bold: true);
        ev.AddChild(_ergCountdown);
        _ergNext = UiTheme.Label("", 18, UiTheme.Dim);
        ev.AddChild(_ergNext);

        // Grade + profile
        var gradePanel = UiTheme.Anchor(UiTheme.Panel(), 0.5f, 1, -850, -196, 220, 0);
        AddChild(gradePanel);
        var grv = UiTheme.VBox(0);
        gradePanel.AddChild(grv);
        grv.AddChild(UiTheme.CaptionLabel("GRADE"));
        _grade = UiTheme.Label("0.0%", 60, UiTheme.Text, bold: true);
        grv.AddChild(_grade);
        _gradeSub = UiTheme.Label("", 16, UiTheme.Dim);
        grv.AddChild(_gradeSub);

        _profile = UiTheme.Anchor(new ProfileGraph { MouseFilter = MouseFilterEnum.Ignore }, 0.5f, 1, -610, -196, 1220, 150);
        AddChild(_profile);
        _workout = UiTheme.Anchor(new WorkoutGraph { MouseFilter = MouseFilterEnum.Ignore }, 0.5f, 1, -610, -300, 1220, 92);
        AddChild(_workout);

        // Fork chooser
        _forkPanel = UiTheme.Anchor(UiTheme.Panel(0.7f, alert: true), 0.5f, 0, -460, 170, 920, 0);
        AddChild(_forkPanel);
        var fv = UiTheme.VBox(8);
        _forkPanel.AddChild(fv);
        _forkHeader = UiTheme.Label("", 30, UiTheme.Magenta, HorizontalAlignment.Center, true);
        fv.AddChild(_forkHeader);
        var opts = UiTheme.HBox(16);
        fv.AddChild(opts);
        for (int i = 0; i < 2; i++)
        {
            var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            _forkOpt[i] = p;
            var v = UiTheme.VBox(2);
            p.AddChild(v);
            _forkTag[i] = UiTheme.Label("", 18, UiTheme.Dim, HorizontalAlignment.Center, true);
            _forkName[i] = UiTheme.Label("", 28, UiTheme.Text, HorizontalAlignment.Center, true);
            _forkStats[i] = UiTheme.Label("", 18, UiTheme.Dim, HorizontalAlignment.Center);
            _forkStake[i] = UiTheme.Label("", 20, UiTheme.Text, HorizontalAlignment.Center, true);
            v.AddChild(_forkTag[i]);
            v.AddChild(_forkName[i]);
            v.AddChild(_forkStats[i]);
            v.AddChild(_forkStake[i]);
            opts.AddChild(p);
        }
        _leanBar = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        _leanFill = new ColorRect { Color = UiTheme.Magenta, MouseFilter = MouseFilterEnum.Ignore };
        _leanBar.AddChild(_leanFill);
        fv.AddChild(_leanBar);
        _forkHelp = UiTheme.Label("", 17, UiTheme.Dim, HorizontalAlignment.Center);
        fv.AddChild(_forkHelp);

        // Rhythm zone meter (same slot as the fork chooser: zones always end well before a fork)
        _rhythmPanel = UiTheme.Anchor(UiTheme.Panel(0.7f), 0.5f, 0, -360, 170, 720, 0);
        AddChild(_rhythmPanel);
        var rzv = UiTheme.VBox(6);
        _rhythmPanel.AddChild(rzv);
        _rhythmHeader = UiTheme.Label("", 26, WorldRoot.RhythmColor, HorizontalAlignment.Center, true);
        rzv.AddChild(_rhythmHeader);
        _rhythmGauge = new RhythmGauge { CustomMinimumSize = new Vector2(0, 64), MouseFilter = MouseFilterEnum.Ignore };
        rzv.AddChild(_rhythmGauge);
        _rhythmInfo = UiTheme.Label("", 18, UiTheme.Dim, HorizontalAlignment.Center);
        rzv.AddChild(_rhythmInfo);
        _rhythmPanel.Visible = false;

        _banner = UiTheme.Anchor(UiTheme.Panel(0.65f, 22), 0.5f, 0.3f, 0, -50, 0, 0);
        var bv = UiTheme.VBox(0);
        _banner.AddChild(bv);
        _bannerTop = UiTheme.CaptionLabel("", 22, HorizontalAlignment.Center);
        _bannerName = UiTheme.Label("", 60, UiTheme.Text, HorizontalAlignment.Center, true);
        _bannerName.CustomMinimumSize = new Vector2(520, 0);
        bv.AddChild(_bannerTop);
        bv.AddChild(_bannerName);
        AddChild(_banner);
        _toastPanel = UiTheme.Anchor(UiTheme.Panel(0.7f, 12), 0.5f, 0.42f, 0, 0, 0, 0);
        _toast = UiTheme.Label("", 30, UiTheme.Amber, HorizontalAlignment.Center, true);
        _toastPanel.AddChild(_toast);
        AddChild(_toastPanel);

        // Footer strip for device status and key hints, so they never sit on the busy scene.
        var footer = UiTheme.Anchor(new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Material = UiTheme.GlassMaterial }, 0, 1, 0, -38, 0, 38);
        footer.AnchorRight = 1;
        footer.OffsetRight = 0;
        var fsb = UiTheme.GlassBox(UiTheme.Cyan, 0.55f, 0);
        fsb.SetBorderWidthAll(0);
        fsb.BorderWidthTop = 1;
        fsb.CornerRadiusTopLeft = fsb.CornerRadiusBottomRight = 0;
        footer.AddThemeStyleboxOverride("panel", fsb);
        _footer = footer;
        AddChild(footer);

        _status = UiTheme.Anchor(UiTheme.Label("", 16, UiTheme.Dim), 0, 1, 24, -31, 1100, 24);
        AddChild(_status);
        _hint = UiTheme.Anchor(UiTheme.Label("", 16, new Color(UiTheme.Dim, 0.8f), HorizontalAlignment.Right), 1, 1, -924, -31, 900, 24);
        AddChild(_hint);

        _slipPanel = slip;
        _secondary = new Control[] { top, _map, gp, gradePanel, _profile, _workout, slip, _footer, _status, _hint };
    }

    /// <summary>Any key or button brings the full HUD back.</summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true }) _calm = 0;
    }

    /// <summary>Steady mode: after a calm spell, fade everything but power, cadence and heart rate (and ERG).</summary>
    void UpdateSteady(float dt)
    {
        var r = _ride;
        bool erg = r.Config.Mode == RideMode.Erg;
        bool busy = _forkPanel.Visible || _racePanel.Visible || _rhythmPanel.Visible || _toastT > 0 || _bannerT > 0
            || r.SlipstreamActive || r.PackSurgeLeft > 0 || (r.PackActive && r.PackStatus != RideSession.PackState.Drafting)
            || r.Gearing.Index != _lastGear || r.ErgBias != _lastBias;
        if (erg && r.Workout != null)
        {
            int idx = r.Workout.Locate(r.WorkoutTime, out float tIn);
            busy |= idx >= 0 && (r.Workout.Intervals[idx].Duration - tIn < 15f || tIn < 8f);
        }
        _lastGear = r.Gearing.Index;
        _lastBias = r.ErgBias;
        _calm = busy || !Settings.Current.SteadyHud ? 0 : _calm + dt;

        float target = _calm > CalmDelay ? SteadyAlpha : 1f;
        _steadyAlpha = Mathf.MoveToward(_steadyAlpha, target, dt * (target > _steadyAlpha ? 4f : 0.6f));
        // A gel coming up only wakes its own panel (they're too frequent to wake everything).
        bool gelNear = r.World.NextGelDistance is >= 0 and < 150;
        foreach (var c in _secondary) c.Modulate = new Color(1, 1, 1, c == _slipPanel && gelNear ? 1f : _steadyAlpha);
    }

    static Control Stat(string caption, out Label value, int size)
    {
        var v = UiTheme.VBox(0);
        v.AddChild(UiTheme.CaptionLabel(caption));
        value = UiTheme.Label("0", size, UiTheme.Text, bold: true);
        v.AddChild(value);
        return v;
    }

    public void Toast(string text, Color? color = null)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color ?? UiTheme.Amber);
        _toastT = 2.6f;
    }

    public void Banner(string top, string name, Color color)
    {
        _bannerTop.Text = top;
        _bannerName.Text = name;
        _bannerName.AddThemeColorOverride("font_color", color);
        _bannerT = 3.5f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var r = _ride;
        var s = Settings.Current;
        int ftp = Math.Max(1, s.Ftp);

        float p3 = r.Power3s;
        _power.Text = $"{p3:0}";
        int zone = r.CurrentZone;
        var zc = PowerZones.Color(zone);
        _power.AddThemeColorOverride("font_color", zc);
        _powerSub.Text = $"{p3 / s.RiderWeightKg:0.0} W/kg  ·  avg {r.AvgPower:0} W";
        _zoneLabel.Text = $"Z{zone}  {PowerZones.Name(zone).ToUpper()}";
        _zoneLabel.AddThemeColorOverride("font_color", zc);
        _zoneBar.Zone = zone;
        _zoneBar.QueueRedraw();
        _cadence.Text = r.Cadence > 0 ? $"{r.Cadence:0}" : "--";
        _hr.Text = r.HeartRate > 0 ? $"{r.HeartRate}" : "--";
        _speed.Text = $"{r.Speed * 3.6f:0.0}";
        _dist.Text = $"{r.World.GlobalDist / 1000f:0.00}";
        _time.Text = UiTheme.Time(r.Elapsed);
        _gain.Text = $"{r.World.GainSoFar:0}";

        float g = r.World.Grade;
        _grade.Text = $"{g:0.0}%";
        _grade.AddThemeColorOverride("font_color", ProfileGraph.GradeColor(g));
        var cur = r.World.Current;
        _gradeSub.Text = cur == null ? "" : cur.Name.ToUpper();

        bool erg = r.Config.Mode == RideMode.Erg;
        _gearPanel.Visible = !erg;
        _ergPanel.Visible = erg;
        _workout.Visible = erg;
        if (!erg)
        {
            _gearBig.Text = $"{r.Gearing.Index + 1} / {r.Gearing.Count}";
            _gearSub.Text = s.VirtualShifting
                ? $"ratio {r.Gearing.Ratio:0.00}  ·  sent {r.SentGrade:0.0}%  ·  ▲▼ / LB RB"
                : $"virtual shifting off  ·  sent {r.SentGrade:0.0}%";
        }
        else if (r.Workout != null)
        {
            int idx = r.Workout.Locate(r.WorkoutTime, out float tIn);
            _ergTarget.Text = $"{r.ErgTarget} W";
            if (idx >= 0)
            {
                var iv = r.Workout.Intervals[idx];
                _ergInterval.Text = $"{iv.Label.ToUpper()}  ·  {iv.PctAt(tIn) * 100:0}% FTP" + (r.ErgBias != 0 ? $"  ·  bias {r.ErgBias:+0;-0}%" : "");
                _ergCountdown.Text = UiTheme.Time(iv.Duration - tIn);
                if (idx + 1 < r.Workout.Intervals.Count)
                {
                    var nx = r.Workout.Intervals[idx + 1];
                    _ergNext.Text = $"NEXT  {nx.Label}  {UiTheme.Time(nx.Duration)} @ {nx.StartPct * ftp:0} W";
                }
                else _ergNext.Text = "LAST INTERVAL";
            }
            else
            {
                _ergInterval.Text = "WORKOUT COMPLETE";
                _ergCountdown.Text = "★";
                _ergNext.Text = "Keep riding or press Esc to finish";
            }
            _workout.Workout = r.Workout;
            _workout.Ftp = ftp;
            _workout.Elapsed = r.WorkoutTime;
            _workout.QueueRedraw();
        }

        _profile.Points = r.World.ProfileAhead(_profile.Span, 20f);
        _profile.ForkAt = r.World.Current?.Children != null ? r.World.DistanceToFork : -1;
        _profile.QueueRedraw();
        _map.QueueRedraw();

        UpdateFork();
        UpdateSlipstream();
        UpdateRace();
        UpdatePack();
        UpdateRhythm();
        PlaceSidePanel(_gearPanel);
        PlaceSidePanel(_ergPanel);

        _toastT -= dt;
        _toastPanel.Modulate = new Color(1, 1, 1, Math.Clamp(_toastT, 0, 1));
        _toastPanel.Visible = _toastT > 0;
        _bannerT -= dt;
        float ba = Math.Clamp(_bannerT, 0, 1);
        _banner.Modulate = new Color(1, 1, 1, ba);
        _banner.Visible = ba > 0;
        UpdateSteady(dt);

        var tr = DeviceManager.Trainer;
        string hrName = DeviceManager.Hr != null ? $"{DeviceManager.Hr.Name} ({DeviceManager.Hr.State})" : "none";
        _status.Text = $"TRAINER  {tr.Name} {(tr.IsSimulated ? $"({VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.SimPowerUp, false)} / {VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.SimPowerDown, false)} to change power)" : $"({(DeviceManager.BleTrainer?.Status ?? "")})")}   ·   HR  {hrName}   ·   ♪ {r.NowPlaying}"
            + (s.ShowFps ? $"   ·   {Engine.GetFramesPerSecond():0} fps" : "");
        _hint.Text = (VeloGenesis.Main.TestMode ? $"TIME ×{Engine.TimeScale:0}  [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.TimeSlower, false)}/{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.TimeFaster, false)}]   ·   " : "")
            + $"CAM {r.CameraMode}  [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.Camera, false)}]   ·   HUD [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.ToggleHud, false)}]   ·   SKIP SONG [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.NextTrack, false)}]   ·   PAUSE [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.Pause, false)}]";
    }

    /// <summary>
    /// Gear / ERG panel: centred on the right edge, but pushed below the race or pack panel
    /// (which sit under the minimap) whenever one is showing.
    /// </summary>
    void PlaceSidePanel(Control panel)
    {
        if (!panel.Visible) return;
        float mid = Size.Y * 0.5f;
        float top = mid - panel.Size.Y * 0.5f;
        foreach (var above in new Control[] { _racePanel, _packPanel })
            if (above.Visible) top = Math.Max(top, above.Position.Y + above.Size.Y + 14f);
        panel.GrowVertical = GrowDirection.End;
        panel.OffsetTop = panel.OffsetBottom = top - mid;
    }

    void UpdatePack()
    {
        var r = _ride;
        _packPanel.Visible = r.PackActive && !r.RaceActive;
        if (!_packPanel.Visible) return;
        var col = r.PackColor;
        bool surge = r.PackSurgeLeft > 0;
        _packTitle.Text = $"DRAFT PACK  ·  {r.PackName}  ·  {r.PackPower:0} W";
        float gap = r.PackGap;
        switch (r.PackStatus)
        {
            case RideSession.PackState.Drafting:
                _packState.Text = $"DRAFTING  -{(1f - r.PackDraft) * 100:0}%";
                _packState.AddThemeColorOverride("font_color", UiTheme.Lime);
                _packInfo.Text = gap <= 1f ? "sheltered in the bunch" : $"on the wheel  ·  {gap:0} m back";
                break;
            case RideSession.PackState.Front:
                _packState.Text = "ON THE FRONT";
                _packState.AddThemeColorOverride("font_color", UiTheme.Amber);
                _packInfo.Text = "no shelter  ·  ease off to sit in";
                break;
            default:
                _packState.Text = $"{gap:0} m TO THE WHEEL";
                _packState.AddThemeColorOverride("font_color", col.Lightened(0.2f));
                _packInfo.Text = gap < 60 ? "push to catch the last wheel" : "bridge across and sit in";
                break;
        }
        if (surge)
        {
            _packInfo.Text = (r.PackSurgeFailed ? "DROPPED  ·  " : "SURGE!  HOLD THE WHEEL  ·  ") + $"{r.PackSurgeLeft:0} s";
            _packInfo.AddThemeColorOverride("font_color", r.PackSurgeFailed ? UiTheme.Red : UiTheme.Amber);
        }
        else _packInfo.AddThemeColorOverride("font_color", UiTheme.Dim);
        float f = surge ? r.PackSurgeLeft / RideSession.PackSurgeDuration : 1f - Math.Clamp(gap / 120f, 0f, 1f);
        _packFill.Size = new Vector2(_packBar.Size.X * f, 8);
        _packFill.Color = surge ? UiTheme.Amber : col;
    }

    void UpdateRhythm()
    {
        var r = _ride;
        float next = r.NextRhythmDistance;
        bool upcoming = next is >= 0 and < 250;
        _rhythmPanel.Visible = (r.RhythmActive || upcoming) && !_forkPanel.Visible;
        if (!_rhythmPanel.Visible) return;
        int? zone = r.RhythmActive ? r.RhythmRpm : null;
        if (zone == null)
            foreach (var z in r.World.Current.Rhythm)
                if (z.Start > r.World.DistInSeg) { zone = z.Rpm; break; }
        _rhythmGauge.Target = zone ?? 90;
        _rhythmGauge.Window = RideSession.RhythmWindow;
        _rhythmGauge.Cadence = r.RhythmCadence;
        _rhythmGauge.Locked = r.RhythmLocked;
        _rhythmGauge.QueueRedraw();
        if (r.RhythmActive)
        {
            _rhythmHeader.Text = r.RhythmLocked ? $"♪  LOCKED ON {r.RhythmRpm} RPM  ♪" : $"RHYTHM ZONE  ·  HOLD {r.RhythmRpm} RPM";
            _rhythmInfo.Text = $"{r.RhythmAccuracy * 100:0}% on the beat   ·   {r.RhythmRemaining:0} m left   ·   85% = perfect";
        }
        else
        {
            _rhythmHeader.Text = $"RHYTHM ZONE IN {next:0} m  ·  {zone} RPM";
            _rhythmInfo.Text = r.Config.Mode == RideMode.Erg ? "get your cadence into the green band" : "shift to find a gear for the target cadence";
        }
    }

    void UpdateRace()
    {
        var r = _ride;
        _racePanel.Visible = r.RaceActive;
        _map.Ghost = r.GhostPosition;
        if (!r.RaceActive) return;
        float gap = r.RaceGap;
        float secs = gap / Math.Max(r.Speed, 2f);
        _raceTitle.Text = $"RACE vs {r.RaceGhostName}  ·  {r.RaceGhostPower:0} W";
        _raceGap.Text = gap >= 0 ? $"+{gap:0} m  ({secs:0.0}s)" : $"{gap:0} m  ({secs:0.0}s)";
        _raceGap.AddThemeColorOverride("font_color", gap >= 0 ? UiTheme.Lime : UiTheme.Red);
        _raceInfo.Text = $"{(gap >= 0 ? "LEADING" : "CHASING")}  ·  finish in {Math.Max(0, r.RaceToFinish):0} m";
        float f = 1f - Math.Clamp(r.RaceToFinish / Math.Max(r.RaceLength, 1f), 0f, 1f);
        _raceFill.Size = new Vector2(_raceBar.Size.X * f, 8);
    }

    void UpdateSlipstream()
    {
        var r = _ride;
        var pips = new System.Text.StringBuilder();
        for (int i = 0; i < RideSession.MaxSlipCharges; i++) pips.Append(i < r.SlipCharges ? "◆ " : "◇ ");
        if (r.SlipstreamActive)
        {
            _slipCharges.Text = $"ACTIVE  {r.SlipRemaining:0}s   {pips}";
            _slipHint.Text = r.Config.Mode == RideMode.Erg ? "speed boost" : "-40% drag · easier climbs";
            float f = r.SlipRemaining / RideSession.SlipstreamDuration;
            _slipFill.Size = new Vector2(_slipBar.Size.X * Math.Min(f, 1f), 8);
            _slipFill.Color = UiTheme.Amber.Lightened(0.2f * MathF.Sin((float)Time.GetTicksMsec() / 120f));
        }
        else
        {
            _slipCharges.Text = pips.ToString();
            _slipHint.Text = r.SlipCharges > 0 ? VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.Slipstream) : "collect power gels in Z4+";
            _slipFill.Size = new Vector2(0, 8);
        }
        _slipCharges.AddThemeColorOverride("font_color", r.SlipCharges > 0 || r.SlipstreamActive ? UiTheme.Amber : new Color(UiTheme.Dim, 0.6f));

        // Power gel guidance: they only count when you hit them in Z4+.
        float gel = r.World.NextGelDistance;
        if (gel >= 0 && gel < 300)
        {
            bool ok = r.CurrentZone >= PowerZones.GelZone;
            int need = PowerZones.LowerWatts(PowerZones.GelZone, Settings.Current.Ftp);
            _gelHint.Text = ok ? $"POWER GEL IN {gel:0} m  ·  in zone ✓" : $"POWER GEL IN {gel:0} m  ·  push to Z{PowerZones.GelZone}+ (≥ {need} W)";
            _gelHint.AddThemeColorOverride("font_color", ok ? UiTheme.Lime : UiTheme.Amber);
        }
        else _gelHint.Text = "";
    }

    void UpdateFork()
    {
        var r = _ride;
        var d = r.Fork;
        bool show = r.World.ForkAhead && d.Active;
        _forkPanel.Visible = show;
        if (!show) { _lastSel = -1; return; }
        var kids = r.World.Current.Children;
        _forkHeader.Text = $"FORK IN {r.World.DistanceToFork:0} m";
        for (int i = 0; i < 2; i++)
        {
            var k = kids[i];
            bool sel = d.Selection == i;
            string arrow = i == 0 ? "◀ LEFT" : "RIGHT ▶";
            string effort = k.IsHarder ? "PUSH ▲" : "EASE ▼";
            _forkTag[i].Text = $"{arrow}   ·   {effort}{(k.IsPlan ? "   ·   PLAN" : "")}";
            _forkName[i].Text = k.Name.ToUpper();
            _forkName[i].AddThemeColorOverride("font_color", sel ? k.Primary.Lightened(0.2f) : new Color(UiTheme.Dim, 0.8f));
            _forkStats[i].Text = $"{k.Length / 1000f:0.0} km   ·   +{k.Gain:0} m / -{k.Drop:0} m   ·   max {k.MaxGrade:0}%";
            string stake = WorldRoot.FeatureLabel(k);
            _forkStake[i].Text = stake;
            _forkStake[i].Visible = stake != "";
            var fc = WorldRoot.FeatureColor(k.Feature);
            _forkStake[i].AddThemeColorOverride("font_color", sel ? fc : new Color(fc, 0.55f));
            _forkOpt[i].AddThemeStyleboxOverride("panel", UiTheme.Box(sel ? new Color(k.Primary.Darkened(0.75f), 0.75f) : new Color(0.03f, 0.02f, 0.1f, 0.45f), sel ? k.Primary : new Color(UiTheme.Dim, 0.25f), sel ? 2 : 1, 6, 10));
        }
        if (_lastSel >= 0 && _lastSel != d.Selection) r.Music.Synth.SfxForkSelect();
        _lastSel = d.Selection;

        // Lean meter: centre = baseline, right = harder effort.
        float w = _leanBar.Size.X;
        float lean = Math.Clamp(d.Lean, -1.5f, 1.5f) / 1.5f;
        float harderSide = d.HarderIndex == 1 ? 1 : -1;
        float x = lean * harderSide;
        _leanFill.Position = new Vector2(w / 2 + Math.Min(0, x) * w / 2, 0);
        _leanFill.Size = new Vector2(Math.Abs(x) * w / 2, 10);
        _leanFill.Color = Math.Abs(d.Lean) >= 1 ? UiTheme.Magenta : new Color(UiTheme.Magenta, 0.5f);
        string metric = d.UseCadence ? "spin faster / slower than" : "push harder / ease off from";
        string unit = d.UseCadence ? "rpm" : "W";
        _forkHelp.Text = d.Manual
            ? "Locked by ◀ ▶ input"
            : $"{metric} your {d.Baseline:0} {unit} baseline  (▲ {d.UpThreshold:0} / ▼ {d.DownThreshold:0})  ·  or press ◀ ▶";
    }
}
