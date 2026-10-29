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
        // Full height with a scrolling middle, so every row stays reachable in short or wide windows.
        var panel = UiTheme.Anchor(UiTheme.Panel(0.9f, 30), 0.5f, 0, -680, 24, 1360, 0);
        panel.AnchorBottom = 1;
        panel.OffsetBottom = -24;
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
        var mode = new OptionButton();
        foreach (var n in Display.ModeNames) mode.AddItem(n);
        mode.Selected = Math.Max(0, Array.IndexOf(Display.Modes, s.DisplayMode));
        var size = new OptionButton();
        var sizes = Display.WindowSizes();
        foreach (var sz in sizes) size.AddItem(Display.SizeName(sz));
        size.Selected = Math.Max(0, sizes.IndexOf(Display.WindowSize));
        var displayRow = UiTheme.HBox(12);
        displayRow.AddChild(mode);
        displayRow.AddChild(size);
        var display = Row("Display", displayRow, "");
        var displayHelp = (Label)display.GetChild(display.GetChildCount() - 1);
        v.AddChild(display);

        var q = new OptionButton();
        foreach (var n in Quality.Names) q.AddItem(n);
        q.Selected = Math.Max(0, Array.IndexOf(Quality.Names, s.Quality));
        var scale = new OptionButton();
        foreach (var n in Display.RenderScaleNames) scale.AddItem("Render " + n);
        scale.Selected = Math.Max(0, Array.IndexOf(Display.RenderScales, s.RenderScale));
        var qualityRow = UiTheme.HBox(12);
        qualityRow.AddChild(q);
        qualityRow.AddChild(scale);
        var quality = Row("Quality", qualityRow, "");
        var qualityHelp = (Label)quality.GetChild(quality.GetChildCount() - 1);
        v.AddChild(quality);

        void ShowDisplay()
        {
            var screen = Display.NativeSize;
            var px = Display.IsFullscreen ? screen : Display.WindowSize;
            size.Visible = !Display.IsFullscreen;
            displayHelp.Text = Display.IsFullscreen
                ? $"Uses the monitor's native {screen.X} × {screen.Y}. F11 switches to a window and back."
                : "F11 switches to fullscreen and back.";
            float rs = Display.RenderScale(Quality.Get(s.Quality));
            qualityHelp.Text = $"3D renders at {(int)(px.X * rs)} × {(int)(px.Y * rs)}" + (rs < 0.99f ? ", upscaled with FSR." : ".") + " Lower quality also lightens the terrain.";
        }
        ShowDisplay();
        mode.ItemSelected += i => { s.DisplayMode = Display.Modes[i]; Display.Apply(); ShowDisplay(); };
        size.ItemSelected += i => { s.WindowWidth = sizes[(int)i].X; s.WindowHeight = sizes[(int)i].Y; Display.Apply(); ShowDisplay(); };
        q.ItemSelected += i => { s.Quality = Quality.Names[i]; QualityChanged?.Invoke(); ShowDisplay(); };
        scale.ItemSelected += i => { s.RenderScale = Display.RenderScales[i]; RenderScaleChanged?.Invoke(); ShowDisplay(); };
        var aa = new OptionButton();
        foreach (var d in Quality.AaDescriptions) aa.AddItem(d);
        aa.Selected = Math.Max(0, Array.IndexOf(Quality.AaModes, s.AntiAliasing));
        aa.ItemSelected += i => { s.AntiAliasing = Quality.AaModes[i]; AntiAliasingChanged?.Invoke(); };
        v.AddChild(Row("Anti-aliasing", aa, "TAA smooths shimmering lines in the distance best. Applies instantly."));
        var fps = new CheckButton { Text = "Show FPS", ButtonPressed = s.ShowFps };
        fps.Toggled += x => s.ShowFps = x;
        var steady = new CheckButton { Text = "Steady HUD", ButtonPressed = s.SteadyHud };
        steady.Toggled += x => s.SteadyHud = x;
        var hudRow = UiTheme.HBox(24);
        hudRow.AddChild(fps);
        hudRow.AddChild(steady);
        v.AddChild(Row("HUD", hudRow, "Steady HUD fades everything but power, cadence and HR during calm stretches."));

        v.AddChild(Section("AUDIO"));
        var src = new OptionButton();
        string[] sources = { "Procedural", "Files", "Both", "External" };
        src.AddItem("Procedural synthwave");
        src.AddItem("My music folder");
        src.AddItem("Both (shuffle)");
        src.AddItem("YouTube Music / other player");
        src.Selected = Math.Max(0, Array.IndexOf(sources, s.MusicSource));
        var srcRow = UiTheme.HBox(12);
        srcRow.AddChild(src);
        var folder = UiTheme.Button("OPEN MUSIC FOLDER", () => OS.ShellOpen(Paths.Music), 18);
        var liked = UiTheme.Button("LIKED MUSIC", () => OS.ShellOpen(Audio.ExternalPlayer.LikedMusicUrl), 18);
        var ytm = UiTheme.Button("YT MUSIC HOME", () => OS.ShellOpen(Audio.ExternalPlayer.YouTubeMusicUrl), 18);
        ytm.TooltipText = "Your Supermix and other mixes are at the top of the home page.";
        srcRow.AddChild(folder);
        srcRow.AddChild(liked);
        srcRow.AddChild(ytm);
        var musicRow = Row("Music", srcRow, "");
        var musicHelp = (Label)musicRow.GetChild(musicRow.GetChildCount() - 1);
        void ShowSource()
        {
            bool ext = s.MusicSource == "External";
            folder.Visible = !ext;
            liked.Visible = ytm.Visible = ext;
            musicHelp.Text = ext
                ? "Play YouTube Music (or Spotify…) in your browser. Skip song controls it; the volume is set in the browser."
                : "Drop .mp3 / .ogg / .wav files into Documents/VeloGenesis/Music.";
        }
        ShowSource();
        src.ItemSelected += i => { s.MusicSource = sources[i]; ShowSource(); AudioChanged?.Invoke(); };
        v.AddChild(musicRow);
        v.AddChild(Row("Music volume", UiTheme.VolumeSlider(s.MusicVolume, x => { s.MusicVolume = x; VolumeChanged?.Invoke(); })));
        v.AddChild(Row("Effects volume", UiTheme.VolumeSlider(s.SfxVolume, x => { s.SfxVolume = x; VolumeChanged?.Invoke(); })));
        v.AddChild(Row("Wind volume", UiTheme.VolumeSlider(s.WindVolume, x => { s.WindVolume = x; VolumeChanged?.Invoke(); }), "Air rushing past – rises with your speed and when you sprint."));

        v.AddChild(Section("EXPORT"));
        var gps = new CheckButton { Text = "Virtual GPS track", ButtonPressed = s.VirtualGps };
        gps.Toggled += x => s.VirtualGps = x;
        v.AddChild(Row("FIT files", gps, "Adds map coordinates (in the open ocean) so Strava draws your route. Off = indoor ride without a map."));

        var buttons = UiTheme.HBox(16);
        var back = UiTheme.Button("◀  SAVE & BACK", Cancel, 24, 260);
        buttons.AddChild(back);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(UiTheme.Button(Online.Strava.Connected ? "STRAVA  ✓" : "STRAVA", () => { Settings.Save(); OpenStrava?.Invoke(); }, 22));
        buttons.AddChild(UiTheme.Button("⌨  CONTROLS / KEY BINDINGS", () => { Settings.Save(); OpenControls?.Invoke(); }, 22));
        v.AddChild(buttons);
        FirstFocus = ftp.GetLineEdit();

        var scroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        var body = UiTheme.VBox(10);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(body);
        foreach (var c in v.GetChildren())
        {
            if (c.GetIndex() == 0 || c == buttons) continue;
            v.RemoveChild(c);
            body.AddChild(c);
        }
        v.AddChild(scroll);
        v.MoveChild(scroll, 1);
        // Start at the top once the first layout pass is done (focus-follow can scroll before sizes settle).
        GetTree().CreateTimer(0).Timeout += () => scroll.ScrollVertical = 0;
    }

    protected override void Cancel()
    {
        Settings.Save();
        Back?.Invoke();
    }

    public event Action RenderScaleChanged, QualityChanged, AudioChanged, VolumeChanged, OpenControls, OpenStrava, AntiAliasingChanged;
}
