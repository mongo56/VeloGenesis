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

    /// <summary>Hides the device status and key-hint lines (for trailers / screenshots).</summary>
    public void SetCleanMode(bool clean)
    {
        _status.Visible = !clean;
        _hint.Visible = !clean;
    }

    public Hud() { }

    public Hud(RideSession ride) => _ride = ride;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Theme;

        // Power / cadence / HR
        var left = UiTheme.Anchor(UiTheme.Panel(), 0, 0, 24, 24, 340, 0);
        AddChild(left);
        var lv = UiTheme.VBox(2);
        left.AddChild(lv);
        lv.AddChild(UiTheme.Label("POWER", 18, UiTheme.Dim));
        var prow = UiTheme.HBox(8);
        _power = UiTheme.Label("0", 104, UiTheme.Text, bold: true);
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
        var slip = UiTheme.Anchor(UiTheme.Panel(UiTheme.Amber), 0, 0, 24, 330, 340, 0);
        AddChild(slip);
        slip.Visible = _ride.Config.PowerGels;

        // Ghost race panel (under the minimap)
        _racePanel = UiTheme.Anchor(UiTheme.Panel(new Color(0.55f, 0.95f, 1f), 0.8f), 1, 0, -390, 320, 366, 0);
        AddChild(_racePanel);
        var rv = UiTheme.VBox(2);
        _racePanel.AddChild(rv);
        _raceTitle = UiTheme.Label("", 18, UiTheme.Dim);
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
        var sv = UiTheme.VBox(4);
        slip.AddChild(sv);
        var srow = UiTheme.HBox(12);
        srow.AddChild(UiTheme.Label("SLIPSTREAM", 18, UiTheme.Dim));
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
        var gp = UiTheme.Anchor(UiTheme.Panel(UiTheme.Magenta), 1, 0.5f, -330, 0, 306, 0);
        _gearPanel = gp;
        AddChild(gp);
        var gv = UiTheme.VBox(2);
        gp.AddChild(gv);
        gv.AddChild(UiTheme.Label("GEAR", 18, UiTheme.Dim));
        _gearBig = UiTheme.Label("", 64, UiTheme.Magenta, bold: true);
        gv.AddChild(_gearBig);
        _gearSub = UiTheme.Label("", 18, UiTheme.Dim);
        gv.AddChild(_gearSub);

        // ERG
        var ep = UiTheme.Anchor(UiTheme.Panel(UiTheme.Amber), 1, 0.5f, -390, 0, 366, 0);
        _ergPanel = ep;
        AddChild(ep);
        var ev = UiTheme.VBox(2);
        ep.AddChild(ev);
        ev.AddChild(UiTheme.Label("ERG TARGET", 18, UiTheme.Dim));
        _ergTarget = UiTheme.Label("", 64, UiTheme.Amber, bold: true);
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
        grv.AddChild(UiTheme.Label("GRADE", 18, UiTheme.Dim));
        _grade = UiTheme.Label("0.0%", 60, UiTheme.Text, bold: true);
        grv.AddChild(_grade);
        _gradeSub = UiTheme.Label("", 16, UiTheme.Dim);
        grv.AddChild(_gradeSub);

        _profile = UiTheme.Anchor(new ProfileGraph { MouseFilter = MouseFilterEnum.Ignore }, 0.5f, 1, -610, -196, 1220, 150);
        AddChild(_profile);
        _workout = UiTheme.Anchor(new WorkoutGraph { MouseFilter = MouseFilterEnum.Ignore }, 0.5f, 1, -610, -300, 1220, 92);
        AddChild(_workout);

        // Fork chooser
        _forkPanel = UiTheme.Anchor(UiTheme.Panel(UiTheme.Magenta, 0.8f), 0.5f, 0, -460, 170, 920, 0);
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
            v.AddChild(_forkTag[i]);
            v.AddChild(_forkName[i]);
            v.AddChild(_forkStats[i]);
            opts.AddChild(p);
        }
        _leanBar = new ColorRect { Color = new Color(1, 1, 1, 0.1f), CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        _leanFill = new ColorRect { Color = UiTheme.Magenta, MouseFilter = MouseFilterEnum.Ignore };
        _leanBar.AddChild(_leanFill);
        fv.AddChild(_leanBar);
        _forkHelp = UiTheme.Label("", 17, UiTheme.Dim, HorizontalAlignment.Center);
        fv.AddChild(_forkHelp);

        _bannerTop = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("", 24, UiTheme.Text, HorizontalAlignment.Center), 8), 0.5f, 0.3f, -600, -40, 1200, 30);
        _bannerName = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("", 64, UiTheme.Text, HorizontalAlignment.Center, true), 16), 0.5f, 0.3f, -600, -6, 1200, 80);
        AddChild(_bannerTop);
        AddChild(_bannerName);
        _toast = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("", 32, UiTheme.Amber, HorizontalAlignment.Center, true), 12), 0.5f, 0.42f, -600, 0, 1200, 40);
        AddChild(_toast);

        _status = UiTheme.Anchor(UiTheme.Label("", 16, UiTheme.Dim), 0, 1, 24, -34, 900, 24);
        AddChild(_status);
        _hint = UiTheme.Anchor(UiTheme.Label("", 16, new Color(UiTheme.Dim, 0.8f), HorizontalAlignment.Right), 1, 1, -924, -34, 900, 24);
        AddChild(_hint);
    }

    static Control Stat(string caption, out Label value, int size)
    {
        var v = UiTheme.VBox(0);
        v.AddChild(UiTheme.Label(caption, 16, UiTheme.Dim));
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

        _toastT -= dt;
        _toast.Modulate = new Color(1, 1, 1, Math.Clamp(_toastT, 0, 1));
        _bannerT -= dt;
        float ba = Math.Clamp(_bannerT, 0, 1);
        _bannerTop.Modulate = _bannerName.Modulate = new Color(1, 1, 1, ba);

        var tr = DeviceManager.Trainer;
        string hrName = DeviceManager.Hr != null ? $"{DeviceManager.Hr.Name} ({DeviceManager.Hr.State})" : "none";
        _status.Text = $"TRAINER  {tr.Name} {(tr.IsSimulated ? $"({VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.SimPowerUp, false)} / {VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.SimPowerDown, false)} to change power)" : $"({(DeviceManager.BleTrainer?.Status ?? "")})")}   ·   HR  {hrName}   ·   ♪ {r.NowPlaying}"
            + (s.ShowFps ? $"   ·   {Engine.GetFramesPerSecond():0} fps" : "");
        _hint.Text = (VeloGenesis.Main.TestMode ? $"TIME ×{Engine.TimeScale:0}  [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.TimeSlower, false)}/{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.TimeFaster, false)}]   ·   " : "")
            + $"CAM {r.CameraMode}  [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.Camera, false)}]   ·   HUD [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.ToggleHud, false)}]   ·   SKIP SONG [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.NextTrack, false)}]   ·   PAUSE [{VeloGenesis.Core.InputSetup.Describe(VeloGenesis.Core.InputSetup.Pause, false)}]";
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
            _forkOpt[i].AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.03f, 0.02f, 0.1f, sel ? 0.95f : 0.5f), sel ? k.Primary : new Color(UiTheme.Dim, 0.25f), sel ? 3 : 1, 6, 10));
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
