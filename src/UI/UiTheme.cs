using System;
using Godot;

namespace VeloGenesis.UI;

/// <summary>Neon UI theme and small widget factory helpers.</summary>
public static class UiTheme
{
    public static readonly Color Cyan = new(0.1f, 0.92f, 1f);
    public static readonly Color Magenta = new(1f, 0.18f, 0.82f);
    public static readonly Color Amber = new(1f, 0.78f, 0.2f);
    public static readonly Color Lime = new(0.45f, 1f, 0.35f);
    public static readonly Color Red = new(1f, 0.25f, 0.3f);
    public static readonly Color Text = new(0.92f, 0.95f, 1f);
    public static readonly Color Dim = new(0.62f, 0.66f, 0.82f);
    public static readonly Color PanelBg = new(0.02f, 0.01f, 0.07f, 0.74f);

    static Font _font, _bold, _font3D;
    static Theme _theme;

    public static Font Font => _font ??= new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Arial" }, FontWeight = 400 };
    public static Font Bold => _bold ??= new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Arial" }, FontWeight = 700 };
    public static Font Font3D => _font3D ??= new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Arial" }, FontWeight = 600, MultichannelSignedDistanceField = true };

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 2, int radius = 6, int margin = 14)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(margin);
        return sb;
    }

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Theme { DefaultFont = Font, DefaultFontSize = 24 };
            t.SetColor("font_color", "Label", Text);
            // Dark outline keeps text readable over the bright neon world.
            t.SetConstant("outline_size", "Label", 3);
            t.SetColor("font_outline_color", "Label", new Color(0, 0, 0.02f, 0.85f));

            var normal = Box(new Color(0.03f, 0.02f, 0.1f, 0.85f), new Color(Cyan, 0.55f), 2, 6, 12);
            var hover = Box(new Color(0.06f, 0.05f, 0.18f, 0.92f), Cyan, 2, 6, 12);
            var pressed = Box(new Color(0.25f, 0.04f, 0.25f, 0.95f), Magenta, 2, 6, 12);
            var focus = Box(new Color(0, 0, 0, 0), Magenta, 3, 6, 12);
            var disabled = Box(new Color(0.03f, 0.02f, 0.06f, 0.6f), new Color(Dim, 0.3f), 1, 6, 12);
            foreach (var type in new[] { "Button", "OptionButton", "CheckButton", "CheckBox" })
            {
                t.SetStylebox("normal", type, normal);
                t.SetStylebox("hover", type, hover);
                t.SetStylebox("pressed", type, pressed);
                t.SetStylebox("hover_pressed", type, pressed);
                t.SetStylebox("focus", type, focus);
                t.SetStylebox("disabled", type, disabled);
                t.SetColor("font_color", type, Text);
                t.SetColor("font_hover_color", type, Cyan);
                t.SetColor("font_pressed_color", type, Text);
                t.SetColor("font_hover_pressed_color", type, Text);
                t.SetColor("font_focus_color", type, Text);
                t.SetColor("font_disabled_color", type, new Color(Dim, 0.5f));
            }

            var edit = Box(new Color(0.01f, 0.01f, 0.05f, 0.9f), new Color(Cyan, 0.4f), 1, 4, 8);
            var editFocus = Box(new Color(0.02f, 0.02f, 0.08f, 0.95f), Magenta, 2, 4, 8);
            t.SetStylebox("normal", "LineEdit", edit);
            t.SetStylebox("focus", "LineEdit", editFocus);
            t.SetColor("font_color", "LineEdit", Text);
            t.SetColor("caret_color", "LineEdit", Magenta);

            t.SetStylebox("panel", "PanelContainer", Box(PanelBg, new Color(Cyan, 0.35f), 1, 8, 16));
            t.SetStylebox("panel", "PopupMenu", Box(new Color(0.03f, 0.02f, 0.1f, 0.97f), Cyan, 1, 4, 8));
            t.SetStylebox("hover", "PopupMenu", Box(new Color(0.25f, 0.04f, 0.25f, 0.95f), Magenta, 0, 2, 4));
            t.SetColor("font_color", "PopupMenu", Text);
            t.SetColor("font_hover_color", "PopupMenu", Cyan);
            t.SetFontSize("font_size", "PopupMenu", 22);

            var sliderBg = Box(new Color(0.05f, 0.04f, 0.15f), new Color(Cyan, 0.3f), 1, 3, 0);
            sliderBg.SetContentMarginAll(3);
            t.SetStylebox("slider", "HSlider", sliderBg);
            t.SetStylebox("grabber_area", "HSlider", Box(new Color(Magenta, 0.8f), Magenta, 0, 3, 0));
            t.SetStylebox("grabber_area_highlight", "HSlider", Box(Magenta, Magenta, 0, 3, 0));

            t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.02f, 0.01f, 0.07f, 0.95f), Cyan, 1, 4, 8));
            _theme = t;
            return t;
        }
    }

    public static Label Label(string text, int size = 24, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left, bool bold = false)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (bold) l.AddThemeFontOverride("font", Bold);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public static Button Button(string text, Action onPressed, int size = 26, float minWidth = 0)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeFontOverride("font", Bold);
        if (minWidth > 0) b.CustomMinimumSize = new Vector2(minWidth, 0);
        if (onPressed != null) b.Pressed += onPressed;
        return b;
    }

    public static PanelContainer Panel(Color? border = null, float alpha = 0.74f, int margin = 16)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(new Color(PanelBg, alpha), new Color(border ?? Cyan, 0.45f), 1, 8, margin));
        return p;
    }

    public static VBoxContainer VBox(int sep = 10)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        return v;
    }

    public static HBoxContainer HBox(int sep = 10)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        return h;
    }

    public static SpinBox Spin(double min, double max, double step, double value, string suffix = "")
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, CustomMinimumSize = new Vector2(180, 0) };
        s.GetLineEdit().AddThemeFontSizeOverride("font_size", 24);
        return s;
    }

    /// <summary>Heavy outline + shadow for text floating directly over the 3D scene.</summary>
    public static T Overlay<T>(T l, int outline = 12) where T : Label
    {
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0.02f, 0.92f));
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        l.AddThemeConstantOverride("shadow_offset_x", 3);
        l.AddThemeConstantOverride("shadow_offset_y", 4);
        l.AddThemeConstantOverride("shadow_outline_size", outline + 4);
        return l;
    }

    /// <summary>0–100% slider with a live percentage label.</summary>
    public static HBoxContainer VolumeSlider(float value, Action<float> set)
    {
        var row = HBox(12);
        var sl = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(300, 28), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var pct = Label($"{value * 100:0}%", 22);
        pct.CustomMinimumSize = new Vector2(70, 0);
        sl.ValueChanged += x => { pct.Text = $"{x * 100:0}%"; set((float)x); };
        row.AddChild(sl);
        row.AddChild(pct);
        return row;
    }

    /// <summary>Anchors a control to a point of its parent (0..1) with a pixel offset and size.</summary>
    public static T Anchor<T>(T c, float ax, float ay, float x, float y, float w = 0, float h = 0) where T : Control
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = x;
        c.OffsetTop = y;
        c.OffsetRight = x + w;
        c.OffsetBottom = y + h;
        c.GrowHorizontal = ax >= 0.99f ? Control.GrowDirection.Begin : ax > 0.01f ? Control.GrowDirection.Both : Control.GrowDirection.End;
        c.GrowVertical = ay >= 0.99f ? Control.GrowDirection.Begin : ay > 0.01f ? Control.GrowDirection.Both : Control.GrowDirection.End;
        return c;
    }

    /// <summary>Colour of the 7-zone power zone for a fraction of FTP.</summary>
    public static Color ZoneColor(float pctFtp) => Ride.PowerZones.Color(Ride.PowerZones.Zone(pctFtp));

    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
