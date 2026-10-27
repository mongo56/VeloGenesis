using System;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.UI;

/// <summary>Rider profile, trainer feel, graphics and audio preferences.</summary>
public partial class SettingsScreen : Screen
{
    public event Action Back;

    protected override void Build()
    {
        Dim = 0.5f;
        var s = Settings.Current;
        var panel = UiTheme.Anchor(UiTheme.Panel(UiTheme.Cyan, 0.9f, 30), 0.5f, 0.5f, -680, 0, 1360, 0);
        AddChild(panel);
        var v = UiTheme.VBox(10);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("SETTINGS", 48, UiTheme.Cyan, bold: true));

        v.AddChild(Section("RIDER"));
        var ftp = UiTheme.Spin(80, 600, 1, s.Ftp, " W");
        ftp.ValueChanged += x => s.Ftp = (int)x;
        v.AddChild(Row("FTP", ftp, "Functional threshold power – ERG workouts scale to this."));
        var weight = UiTheme.Spin(35, 160, 0.5, s.RiderWeightKg, " kg");
        weight.ValueChanged += x => s.RiderWeightKg = (float)x;
        v.AddChild(Row("Rider weight", weight));
        var bike = UiTheme.Spin(5, 30, 0.5, s.BikeWeightKg, " kg");
        bike.ValueChanged += x => s.BikeWeightKg = (float)x;
        v.AddChild(Row("Bike weight", bike));

        v.AddChild(Section("FREE RIDE"));
        var diff = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = s.TrainerDifficulty * 100, CustomMinimumSize = new Vector2(300, 28) };
        var diffLabel = UiTheme.Label($"{s.TrainerDifficulty * 100:0}%", 22);
        diff.ValueChanged += x => { s.TrainerDifficulty = (float)x / 100f; diffLabel.Text = $"{x:0}%"; };
        var dbox = UiTheme.HBox(12);
        dbox.AddChild(diff);
        dbox.AddChild(diffLabel);
        v.AddChild(Row("Trainer difficulty", dbox, "How much of the real gradient you feel. 100% = full steepness."));
        var vs = new CheckButton { Text = "On", ButtonPressed = s.VirtualShifting };
        vs.Toggled += x => s.VirtualShifting = x;
        v.AddChild(Row("Virtual shifting", vs, "Keep the bike in one gear and shift in-game. Turn off to use your real gears."));

        v.AddChild(Section("GRAPHICS"));
        var q = new OptionButton();
        foreach (var n in Quality.Names) q.AddItem(n);
        q.Selected = Math.Max(0, Array.IndexOf(Quality.Names, s.Quality));
        q.ItemSelected += i => { s.Quality = Quality.Names[i]; QualityChanged?.Invoke(); };
        v.AddChild(Row("Quality", q, "Low/Medium render at reduced resolution with FSR upscaling and lighter terrain."));
        var aa = new OptionButton();
        foreach (var d in Quality.AaDescriptions) aa.AddItem(d);
        aa.Selected = Math.Max(0, Array.IndexOf(Quality.AaModes, s.AntiAliasing));
        aa.ItemSelected += i => { s.AntiAliasing = Quality.AaModes[i]; AntiAliasingChanged?.Invoke(); };
        v.AddChild(Row("Anti-aliasing", aa, "TAA smooths shimmering lines in the distance best. Applies instantly."));
        var fps = new CheckButton { Text = "Show FPS", ButtonPressed = s.ShowFps };
        fps.Toggled += x => s.ShowFps = x;
        v.AddChild(Row("Performance", fps));

        v.AddChild(Section("AUDIO"));
        var src = new OptionButton();
        string[] sources = { "Procedural", "Files", "Both" };
        src.AddItem("Procedural synthwave");
        src.AddItem("My music folder");
        src.AddItem("Both (shuffle)");
        src.Selected = Math.Max(0, Array.IndexOf(sources, s.MusicSource));
        src.ItemSelected += i => { s.MusicSource = sources[i]; AudioChanged?.Invoke(); };
        var srcRow = UiTheme.HBox(12);
        srcRow.AddChild(src);
        srcRow.AddChild(UiTheme.Button("OPEN MUSIC FOLDER", () => OS.ShellOpen(Paths.Music), 18));
        v.AddChild(Row("Music", srcRow, "Drop .mp3 / .ogg / .wav files into Documents/VeloGenesis/Music."));
        v.AddChild(Row("Music volume", UiTheme.VolumeSlider(s.MusicVolume, x => { s.MusicVolume = x; VolumeChanged?.Invoke(); })));
        v.AddChild(Row("Effects volume", UiTheme.VolumeSlider(s.SfxVolume, x => { s.SfxVolume = x; VolumeChanged?.Invoke(); })));

        v.AddChild(Section("EXPORT"));
        var gps = new CheckButton { Text = "Virtual GPS track", ButtonPressed = s.VirtualGps };
        gps.Toggled += x => s.VirtualGps = x;
        v.AddChild(Row("FIT files", gps, "Adds map coordinates (in the open ocean) so Strava draws your route. Off = indoor ride without a map."));

        var buttons = UiTheme.HBox(16);
        var back = UiTheme.Button("◀  SAVE & BACK", () => { Settings.Save(); Back?.Invoke(); }, 24, 260);
        buttons.AddChild(back);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(UiTheme.Button(Online.Strava.Connected ? "STRAVA  ✓" : "STRAVA", () => { Settings.Save(); OpenStrava?.Invoke(); }, 22));
        buttons.AddChild(UiTheme.Button("⌨  CONTROLS / KEY BINDINGS", () => { Settings.Save(); OpenControls?.Invoke(); }, 22));
        v.AddChild(buttons);
        FirstFocus = ftp.GetLineEdit();
    }

    public event Action QualityChanged, AudioChanged, VolumeChanged, OpenControls, OpenStrava, AntiAliasingChanged;
}
