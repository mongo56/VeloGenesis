using System;
using System.IO;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Online;
using VeloGenesis.Ride;

namespace VeloGenesis.UI;

/// <summary>Post-ride stats, exploration results and export.</summary>
public partial class SummaryScreen : Screen
{
    public event Action Done;
    readonly RideSummary _sum;

    public SummaryScreen() { }
    public SummaryScreen(RideSummary sum) => _sum = sum;

    protected override void Build()
    {
        Dim = 0.6f;
        var st = _sum.Stats;
        var panel = UiTheme.Anchor(UiTheme.Panel(0.92f, 32), 0.5f, 0.5f, -700, 0, 1400, 0);
        AddChild(panel);
        var v = UiTheme.VBox(14);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("RIDE COMPLETE", 52, UiTheme.Magenta, bold: true));
        v.AddChild(UiTheme.Label(_sum.Mode == RideMode.Erg ? $"ERG workout · {_sum.WorkoutName}" : "Free ride", 22, UiTheme.Cyan));

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 60);
        grid.AddThemeConstantOverride("v_separation", 16);
        void Stat(string caption, string value)
        {
            var b = UiTheme.VBox(0);
            b.AddChild(UiTheme.Label(caption, 17, UiTheme.Dim));
            b.AddChild(UiTheme.Label(value, 40, UiTheme.Text, bold: true));
            grid.AddChild(b);
        }
        Stat("MOVING TIME", UiTheme.Time(st.MovingSeconds));
        Stat("DISTANCE", $"{st.DistanceM / 1000f:0.00} km");
        Stat("ELEVATION", $"+{st.Ascent:0} m");
        Stat("AVG SPEED", $"{st.AvgSpeed * 3.6f:0.0} km/h");
        Stat("AVG POWER", $"{st.AvgPower:0} W");
        Stat("NORMALIZED", $"{st.NormalizedPower:0} W");
        Stat("MAX POWER", $"{st.MaxPower:0} W");
        Stat("ENERGY", $"{st.Kilojoules:0} kJ");
        Stat("AVG HR", st.AvgHr > 0 ? $"{st.AvgHr:0} bpm" : "--");
        Stat("AVG CADENCE", st.AvgCadence > 0 ? $"{st.AvgCadence:0} rpm" : "--");
        Stat("TSS", $"{st.Tss:0}  (IF {st.IntensityFactor:0.00})");
        Stat("FORKS TAKEN", $"{_sum.ForksTaken}");
        var statsRow = UiTheme.HBox(40);
        statsRow.AddChild(grid);
        var zones = UiTheme.VBox(4);
        zones.AddChild(UiTheme.Label("TIME IN ZONES", 17, UiTheme.Dim));
        zones.AddChild(new ZoneChart { Seconds = _sum.ZoneSeconds, CustomMinimumSize = new Vector2(560, 250), MouseFilter = MouseFilterEnum.Ignore });
        statsRow.AddChild(zones);
        v.AddChild(statsRow);

        v.AddChild(Section("EXPLORATION"));
        v.AddChild(UiTheme.Label((_sum.PowerGels ? $"Power gels collected: {_sum.Gels} of {_sum.GelsSeen}   ·   Slipstreams used: {_sum.SlipstreamsUsed}   ·   " : "") + $"Ghost races won: {_sum.RacesWon} of {_sum.RacesWon + _sum.RacesLost}", 22, UiTheme.Amber));
        var extras = new System.Collections.Generic.List<string>();
        if (_sum.PacksCaught > 0) extras.Add($"Draft packs caught: {_sum.PacksCaught}  ({UiTheme.Time(_sum.DraftSeconds)} in the draft)");
        if (_sum.SurgesTotal > 0) extras.Add($"Surges held: {_sum.SurgesHeld} of {_sum.SurgesTotal}");
        if (_sum.RhythmZones > 0) extras.Add($"Rhythm zones: {_sum.RhythmPerfect} perfect, {_sum.RhythmGood} good of {_sum.RhythmZones}  ({_sum.RhythmAccuracy * 100:0}% on the beat)");
        if (extras.Count > 0) v.AddChild(UiTheme.Label(string.Join("   ·   ", extras), 22, UiTheme.Lime));
        if (_sum.NewBests.Count > 0)
            v.AddChild(UiTheme.Label("★ NEW PERSONAL BESTS   " + string.Join("   ·   ", _sum.NewBests.ConvertAll(e => $"{PersonalBests.DurationName(e.Seconds)} {e.Watts:0} W")), 24, UiTheme.Magenta, bold: true));
        v.AddChild(UiTheme.Label("Biomes: " + string.Join("  ▸  ", _sum.Biomes), 20, UiTheme.Cyan));
        var sectors = UiTheme.Label("Sectors discovered: " + string.Join("  ·  ", _sum.Sectors), 20, UiTheme.Text);
        sectors.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(sectors);

        v.AddChild(Section("EXPORT"));
        v.AddChild(UiTheme.Label(_sum.FitPath != null ? $"Saved {Path.GetFileName(_sum.FitPath)} to Documents/VeloGenesis/Rides" : "Ride too short – nothing saved.", 20, UiTheme.Dim));
        var row = UiTheme.HBox(16);
        row.AddChild(UiTheme.Button("OPEN RIDES FOLDER", () => OS.ShellOpen(Paths.Rides), 22));
        _strava = UiTheme.Button("UPLOAD TO STRAVA", OnStrava, 22);
        _strava.Disabled = _sum.FitPath == null;
        row.AddChild(_strava);
        _stravaStatus = UiTheme.Label("", 19, UiTheme.Dim);
        _stravaStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stravaStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(_stravaStatus);
        var done = UiTheme.Button("MAIN MENU  ▶", () => Done?.Invoke(), 28, 280);
        row.AddChild(done);
        FirstFocus = done;
        v.AddChild(row);

        // Strava's web uploader can't be pre-filled and ignores names in FIT files, so offer the text via the clipboard.
        _pasteRow = UiTheme.HBox(16);
        _pasteRow.Visible = false;
        _pasteRow.AddChild(UiTheme.Label("For Strava's form:", 19, UiTheme.Dim));
        _pasteRow.AddChild(UiTheme.Button("COPY TITLE", () => Copy(ActivityName(), "Title"), 19));
        _pasteRow.AddChild(UiTheme.Button("COPY DESCRIPTION", () => Copy(ActivityDescription(), "Description"), 19));
        v.AddChild(_pasteRow);

        if (_sum.FitPath != null && Strava.Connected && Settings.Current.StravaAutoUpload)
            OnStrava();
    }

    Button _strava;
    Label _stravaStatus;
    HBoxContainer _pasteRow;
    string _activityUrl;
    bool _uploading;

    void Status(string text, Color color)
    {
        if (!IsInstanceValid(_stravaStatus)) return;
        _stravaStatus.Text = text;
        _stravaStatus.AddThemeColorOverride("font_color", color);
    }

    void Copy(string text, string what)
    {
        DisplayServer.ClipboardSet(text);
        Status($"{what} copied. Paste it (Ctrl+V) into the matching field on Strava, then click Save.", UiTheme.Cyan);
    }

    async void OnStrava()
    {
        if (_activityUrl != null) { OS.ShellOpen(_activityUrl); return; }
        if (_uploading || _sum.FitPath == null) return;
        if (!Strava.Connected)
        {
            // No API app (Strava requires a subscription for one): hand the file to the web uploader.
            Strava.OpenManualUpload(_sum.FitPath);
            DisplayServer.ClipboardSet(ActivityName());
            _pasteRow.Visible = true;
            Status("Drag the highlighted .fit file onto the Strava page. The title is on your clipboard.", UiTheme.Cyan);
            return;
        }

        _uploading = true;
        _strava.Disabled = true;
        var result = await Strava.Upload(_sum.FitPath, ActivityName(), ActivityDescription(), p => Status(p, UiTheme.Cyan));
        _uploading = false;
        if (!IsInstanceValid(_strava)) return;
        _strava.Disabled = false;
        if (result.ActivityId > 0)
        {
            _activityUrl = result.Url;
            _strava.Text = "VIEW ON STRAVA  ↗";
            Status(result.Duplicate ? "Already on Strava." : "Uploaded to Strava!", UiTheme.Lime);
        }
        else
        {
            _strava.Text = "RETRY STRAVA UPLOAD";
            Status(result.Error, UiTheme.Red);
        }
    }

    string ActivityName() =>
        _sum.Mode == RideMode.Erg && !string.IsNullOrEmpty(_sum.WorkoutName) ? $"VeloGenesis · {_sum.WorkoutName}" : "VeloGenesis · Free ride";

    string ActivityDescription()
    {
        var lines = new System.Collections.Generic.List<string> { $"Procedural neon ride · {_sum.ForksTaken} forks taken" };
        if (_sum.Sectors.Count > 0) lines.Add("Sectors: " + string.Join(" · ", _sum.Sectors));
        if (_sum.PowerGels) lines.Add($"Power gels: {_sum.Gels}/{_sum.GelsSeen} · Slipstreams: {_sum.SlipstreamsUsed}");
        if (_sum.RacesWon + _sum.RacesLost > 0) lines.Add($"Ghost races won: {_sum.RacesWon}/{_sum.RacesWon + _sum.RacesLost}");
        if (_sum.Biomes.Count > 1) lines.Add("Biomes: " + string.Join(" ▸ ", _sum.Biomes));
        if (_sum.NewBests.Count > 0) lines.Add("New bests: " + string.Join(" · ", _sum.NewBests.ConvertAll(e => $"{PersonalBests.DurationName(e.Seconds)} {e.Watts:0} W")));
        return string.Join("\n", lines);
    }
}
