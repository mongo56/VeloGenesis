using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace VeloGenesis.Core;

/// <summary>
/// Window mode, window size and 3D render resolution. Fullscreen always uses the monitor's native
/// resolution (no video-mode switch, so alt-tab and the browser music player stay smooth); a lower
/// render resolution is how to trade sharpness for frame rate there.
/// </summary>
public static class Display
{
    public static readonly string[] Modes = { "Windowed", "Borderless", "Exclusive" };
    public static readonly string[] ModeNames = { "Windowed", "Borderless fullscreen", "Exclusive fullscreen" };

    /// <summary>Render scales offered in Settings; 0 = the quality preset's own scale.</summary>
    public static readonly float[] RenderScales = { 0f, 1f, 0.85f, 0.75f, 0.67f, 0.5f };
    public static readonly string[] RenderScaleNames = { "Auto (by quality)", "100% (native)", "85%", "75%", "67%", "50%" };

    static readonly Vector2I[] CommonSizes =
    {
        new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1080), new(2560, 1440),
        new(3440, 1440), new(3840, 1600), new(3840, 2160), new(5120, 2160),
    };

    /// <summary>Default window when no size has been chosen (also the project setting).</summary>
    static readonly Vector2I DefaultSize = new(1600, 900);

    static int Screen => DisplayServer.WindowGetCurrentScreen();

    public static Vector2I NativeSize => DisplayServer.ScreenGetSize(Screen);

    public static bool IsFullscreen => Settings.Current.DisplayMode != "Windowed";

    /// <summary>Window sizes that fit on this monitor (16:9 and 21:9), largest last.</summary>
    public static List<Vector2I> WindowSizes()
    {
        var usable = DisplayServer.ScreenGetUsableRect(Screen).Size;
        var list = CommonSizes.Where(s => s.X <= usable.X && s.Y <= usable.Y).ToList();
        if (list.Count == 0) list.Add(usable);
        var chosen = ChosenSize();
        if (chosen != Vector2I.Zero && !list.Contains(chosen)) list.Add(chosen);
        return list.OrderBy(s => s.X * s.Y).ToList();
    }

    static Vector2I ChosenSize() => new(Settings.Current.WindowWidth, Settings.Current.WindowHeight);

    /// <summary>The window size used in windowed mode.</summary>
    public static Vector2I WindowSize
    {
        get
        {
            var s = ChosenSize();
            return s.X > 0 && s.Y > 0 ? s : DefaultSize;
        }
    }

    /// <summary>Applies the saved window mode and size.</summary>
    public static void Apply()
    {
        var s = Settings.Current;
        switch (s.DisplayMode)
        {
            case "Borderless":
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case "Exclusive":
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                var usable = DisplayServer.ScreenGetUsableRect(Screen);
                var size = new Vector2I(Math.Min(WindowSize.X, usable.Size.X), Math.Min(WindowSize.Y, usable.Size.Y));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
                break;
        }
    }

    /// <summary>F11: windowed ⇄ fullscreen (back to whichever fullscreen mode was used last).</summary>
    public static void ToggleFullscreen()
    {
        var s = Settings.Current;
        if (IsFullscreen)
        {
            s.LastFullscreenMode = s.DisplayMode;
            s.DisplayMode = "Windowed";
        }
        else s.DisplayMode = s.LastFullscreenMode == "Exclusive" ? "Exclusive" : "Borderless";
        Settings.Save();
        Apply();
    }

    /// <summary>The 3D render scale in effect: the user's choice, else the quality preset's.</summary>
    public static float RenderScale(Quality q) => Settings.Current.RenderScale > 0 ? Settings.Current.RenderScale : q.RenderScale;

    public static string SizeName(Vector2I s)
    {
        float aspect = s.X / (float)s.Y;
        string ratio = MathF.Abs(aspect - 16f / 9f) < 0.02f ? "16:9" : aspect > 2.2f ? "21:9" : MathF.Abs(aspect - 16f / 10f) < 0.02f ? "16:10" : $"{aspect:0.00}:1";
        return $"{s.X} × {s.Y}  ({ratio})";
    }
}
