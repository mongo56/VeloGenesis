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

    static Font _font, _bold, _semi, _caption, _font3D;
    static Theme _theme;
    static ShaderMaterial _glassMat;

    // Chakra Petch (SIL OFL, assets/fonts); symbols it lacks (◀ ▲ ◆ ♪ …) fall back to system fonts.
    static FontFile Load(string weight)
    {
        var f = (FontFile)GD.Load<FontFile>($"res://assets/fonts/ChakraPetch-{weight}.ttf").Duplicate();
        f.Fallbacks = new Godot.Collections.Array<Font> { new SystemFont { FontNames = new[] { "Segoe UI Symbol", "Segoe UI", "Bahnschrift", "Arial" } } };
        return f;
    }

    public static Font Font => _font ??= Load("Medium");
    public static Font Bold => _bold ??= Load("Bold");
    public static Font SemiBold => _semi ??= Load("SemiBold");
    /// <summary>Small uppercase panel captions, slightly letter-spaced.</summary>
    public static Font Caption => _caption ??= new FontVariation { BaseFont = SemiBold, SpacingGlyph = 2 };
    public static Font Font3D => _font3D ??= MakeFont3D();

    static Font MakeFont3D()
    {
        var f = Load("Bold");
        f.MultichannelSignedDistanceField = true;
        f.MsdfPixelRange = 16;
        f.MsdfSize = 64;
        return f;
    }

    /// <summary>Caption colour: the single HUD accent, softened.</summary>
    public static readonly Color CaptionColor = new(0.45f, 0.85f, 1f, 0.8f);

    /// <summary>Chamfered box (top-left and bottom-right corners cut at 45°), the shape used for all UI.</summary>
    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 2, int radius = 6, int margin = 14)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 1 };
        sb.SetBorderWidthAll(borderWidth);
        sb.CornerRadiusTopLeft = sb.CornerRadiusBottomRight = radius * 2;
        sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = 0;
        sb.SetContentMarginAll(margin);
        return sb;
    }

    /// <summary>Frosted dark glass: blurs and darkens whatever is behind the panel.</summary>
    public static ShaderMaterial GlassMaterial => _glassMat ??= new ShaderMaterial { Shader = new Shader { Code = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float bg_alpha = 0.6;
void fragment() {
    vec4 c = COLOR;
    vec3 behind = textureLod(screen_tex, SCREEN_UV, 3.5).rgb;
    float l = dot(behind, vec3(0.299, 0.587, 0.114));
    behind = mix(vec3(l), behind, 0.55) * 0.45;
    float scan = step(0.5, fract(FRAGCOORD.y * 0.5)) * 0.012;
    COLOR = vec4(mix(behind, c.rgb, c.a) + vec3(0.3, 0.8, 1.0) * scan, clamp(c.a / bg_alpha, 0.0, 1.0));
}
" } };

    public static StyleBoxFlat GlassBox(Color border, float alpha = 0.6f, int margin = 16)
    {
        var sb = Box(new Color(0.04f, 0.015f, 0.1f, alpha), new Color(border, 0.42f), 1, 7, margin);
        sb.BorderWidthTop = 2;
        return sb;
    }

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Theme { DefaultFont = Font, DefaultFontSize = 24 };
            t.SetColor("font_color", "Label", Text);

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

            t.SetStylebox("panel", "PanelContainer", GlassBox(Cyan));
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

    /// <summary>Frosted glass panel. Every panel shares the cyan accent; <paramref name="alert"/> switches it to magenta.</summary>
    public static PanelContainer Panel(float alpha = 0.6f, int margin = 16, bool alert = false)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Material = GlassMaterial };
        p.AddThemeStyleboxOverride("panel", GlassBox(alert ? Magenta : Cyan, alpha, margin));
        return p;
    }

    /// <summary>Small letter-spaced caption above a value.</summary>
    public static Label CaptionLabel(string text, int size = 16, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = Label(text, size, CaptionColor, align);
        l.AddThemeFontOverride("font", Caption);
        return l;
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

    /// <summary>Text floating directly over the 3D scene: a soft halo in its own colour instead of a black outline.</summary>
    public static T Overlay<T>(T l, int glow = 12) where T : Label
    {
        var c = l.HasThemeColorOverride("font_color") ? l.GetThemeColor("font_color") : Text;
        l.AddThemeColorOverride("font_shadow_color", new Color(c, 0.22f));
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 0);
        l.AddThemeConstantOverride("shadow_outline_size", glow);
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
