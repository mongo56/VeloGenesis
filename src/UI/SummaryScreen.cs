using System;
using System.IO;
using Godot;
using VeloGenesis.Core;
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
        var panel = UiTheme.Anchor(UiTheme.Panel(UiTheme.Magenta, 0.92f, 32), 0.5f, 0.5f, -700, 0, 1400, 0);
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
        var sectors = UiTheme.Label("Sectors discovered: " + string.Join("  ·  ", _sum.Sectors), 20, UiTheme.Text);
        sectors.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(sectors);

        v.AddChild(Section("EXPORT"));
        v.AddChild(UiTheme.Label(_sum.FitPath != null ? $"Saved {Path.GetFileName(_sum.FitPath)} to Documents/VeloGenesis/Rides" : "Ride too short – nothing saved.", 20, UiTheme.Dim));
        var row = UiTheme.HBox(16);
        row.AddChild(UiTheme.Button("OPEN RIDES FOLDER", () => OS.ShellOpen(Paths.Rides), 22));
        var strava = UiTheme.Button("UPLOAD TO STRAVA  (coming in milestone 2)", null, 22);
        strava.Disabled = true;
        strava.TooltipText = "For now, upload the .fit file at strava.com/upload/select";
        row.AddChild(strava);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);
        var done = UiTheme.Button("MAIN MENU  ▶", () => Done?.Invoke(), 28, 280);
        row.AddChild(done);
        FirstFocus = done;
        v.AddChild(row);
    }
}
