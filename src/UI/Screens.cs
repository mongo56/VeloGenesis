using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Devices;

namespace VeloGenesis.UI;

/// <summary>Base for full-screen menus drawn over the 3D world.</summary>
public partial class Screen : Control
{
    protected Control FirstFocus;
    protected float Dim = 0.35f;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiTheme.Theme;
        var bg = new ColorRect { Color = new Color(0.01f, 0, 0.04f, Dim), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        Build();
        CallDeferred(MethodName.Focus);
    }

    protected virtual void Build() { }

    public void Focus() => FirstFocus?.GrabFocus();

    protected static Label Section(string text) => UiTheme.Label(text, 20, UiTheme.Magenta, bold: true);

    protected static HBoxContainer Row(string label, Control field, string help = null)
    {
        var h = UiTheme.HBox(16);
        var l = UiTheme.Label(label, 22, UiTheme.Text);
        l.CustomMinimumSize = new Vector2(300, 0);
        h.AddChild(l);
        h.AddChild(field);
        if (help != null)
        {
            var hl = UiTheme.Label(help, 17, UiTheme.Dim);
            hl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            hl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            h.AddChild(hl);
        }
        return h;
    }
}

public partial class MainMenu : Screen
{
    public event Action StartRide, OpenDevices, OpenSettings, Quit;
    Label _status;

    protected override void Build()
    {
        Dim = 0f;
        var panel = UiTheme.Anchor(UiTheme.Panel(UiTheme.Magenta, 0.55f, 36), 0, 0.5f, 90, 0, 600, 0);
        AddChild(panel);
        var v = UiTheme.VBox(14);
        panel.AddChild(v);
        var title = UiTheme.Label("VELOGENESIS", 84, UiTheme.Magenta, bold: true);
        title.AddThemeColorOverride("font_shadow_color", new Color(UiTheme.Cyan, 0.6f));
        title.AddThemeConstantOverride("shadow_offset_x", 3);
        title.AddThemeConstantOverride("shadow_offset_y", 3);
        v.AddChild(title);
        v.AddChild(UiTheme.Label("procedural neon worlds for your smart trainer", 22, UiTheme.Cyan));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        var start = UiTheme.Button("▶   START RIDE", () => StartRide?.Invoke(), 34);
        FirstFocus = start;
        v.AddChild(start);
        v.AddChild(UiTheme.Button("DEVICES", () => OpenDevices?.Invoke(), 26));
        v.AddChild(UiTheme.Button("SETTINGS", () => OpenSettings?.Invoke(), 26));
        v.AddChild(UiTheme.Button("OPEN RIDES FOLDER", () => OS.ShellOpen(Paths.Rides), 26));
        v.AddChild(UiTheme.Button("QUIT", () => Quit?.Invoke(), 26));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        _status = UiTheme.Label("", 18, UiTheme.Dim);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(_status);
        DeviceManager.Changed += RefreshStatus;
        RefreshStatus();
    }

    public override void _ExitTree() => DeviceManager.Changed -= RefreshStatus;

    void RefreshStatus()
    {
        if (!IsInstanceValid(_status)) return;
        var t = DeviceManager.BleTrainer;
        string trainer = t == null ? "simulator (no trainer paired)" : $"{t.Name} – {t.Status}";
        string hr = DeviceManager.Hr == null ? "none" : $"{DeviceManager.Hr.Name} – {DeviceManager.Hr.Status}";
        _status.Text = $"Trainer: {trainer}\nHeart rate: {hr}";
    }
}

public partial class PauseMenu : Screen
{
    public event Action Resume, EndRide, Discard, VolumeChanged;

    protected override void Build()
    {
        Dim = 0.6f;
        var panel = UiTheme.Anchor(UiTheme.Panel(UiTheme.Magenta, 0.9f, 32), 0.5f, 0.5f, -430, 0, 860, 0);
        AddChild(panel);
        var v = UiTheme.VBox(14);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("PAUSED", 56, UiTheme.Magenta, HorizontalAlignment.Center, true));
        var resume = UiTheme.Button("RESUME", () => Resume?.Invoke(), 30);
        FirstFocus = resume;
        v.AddChild(resume);
        v.AddChild(UiTheme.Button("FINISH & SAVE RIDE", () => EndRide?.Invoke(), 26));
        Button discard = null;
        discard = UiTheme.Button("DISCARD RIDE", () =>
        {
            if (discard.Text.StartsWith("CONFIRM")) Discard?.Invoke();
            else discard.Text = "CONFIRM DISCARD (no file saved)";
        }, 22);
        v.AddChild(discard);

        var s = Settings.Current;
        foreach (var (label, value, set) in new (string, float, Action<float>)[]
        {
            ("Music", s.MusicVolume, x => s.MusicVolume = x),
            ("Effects", s.SfxVolume, x => s.SfxVolume = x),
        })
        {
            var row = UiTheme.HBox(12);
            var l = UiTheme.Label(label, 22);
            l.CustomMinimumSize = new Vector2(110, 0);
            row.AddChild(l);
            row.AddChild(UiTheme.VolumeSlider(value, x => { set(x); VolumeChanged?.Invoke(); }));
            v.AddChild(row);
        }
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var help = UiTheme.Label(
            "CONTROLS  (change them in SETTINGS → CONTROLS)\n" +
            $"Gear up  —  {InputSetup.Describe(InputSetup.GearUp)}     Gear down  —  {InputSetup.Describe(InputSetup.GearDown)}\n" +
            $"Fork  —  push harder / ease off, or {InputSetup.Describe(InputSetup.ForkLeft, false)}  /  {InputSetup.Describe(InputSetup.ForkRight, false)}, D-pad, left stick\n" +
            $"Slipstream  —  {InputSetup.Describe(InputSetup.Slipstream)}     Camera  —  {InputSetup.Describe(InputSetup.Camera)}\n" +
            $"Pause  —  {InputSetup.Describe(InputSetup.Pause)}     Next track  —  {InputSetup.Describe(InputSetup.NextTrack)}     Screenshot  —  {InputSetup.Describe(InputSetup.Screenshot, false)}\n" +
            $"Simulator power  —  {InputSetup.Describe(InputSetup.SimPowerUp, false)} / {InputSetup.Describe(InputSetup.SimPowerDown, false)} or gamepad triggers", 17, UiTheme.Dim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(help);
    }
}
