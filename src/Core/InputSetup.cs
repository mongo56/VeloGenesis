using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace VeloGenesis.Core;

/// <summary>
/// Game input actions (keyboard + gamepad). Each action has two keyboard slots and two gamepad
/// slots; user overrides are stored in <see cref="Settings"/> and applied on top of the defaults.
/// Analog axes (left stick for forks, triggers for simulator power) are fixed.
/// </summary>
public static class InputSetup
{
    public const string GearUp = "gear_up";
    public const string GearDown = "gear_down";
    public const string ForkLeft = "fork_left";
    public const string ForkRight = "fork_right";
    public const string Pause = "pause";
    public const string Camera = "camera_toggle";
    public const string NextTrack = "next_track";
    public const string ToggleHud = "toggle_hud";
    public const string SimPowerUp = "sim_power_up";
    public const string SimPowerDown = "sim_power_down";
    public const string Screenshot = "screenshot";
    public const string Slipstream = "slipstream";
    public const string TimeSlower = "time_slower";
    public const string TimeFaster = "time_faster";

    public const int Slots = 2;

    public sealed record Binding(string Action, string Label, Key[] Keys, JoyButton[] Pads);

    public static readonly Binding[] Bindings =
    {
        new(GearUp, "Gear up  (ERG: intensity +5%)", new[] { Key.Up, Key.E }, new[] { JoyButton.RightShoulder, JoyButton.DpadUp }),
        new(GearDown, "Gear down  (ERG: intensity -5%)", new[] { Key.Down, Key.Q }, new[] { JoyButton.LeftShoulder, JoyButton.DpadDown }),
        new(ForkLeft, "Fork: take left branch", new[] { Key.Left, Key.A }, new[] { JoyButton.DpadLeft }),
        new(ForkRight, "Fork: take right branch", new[] { Key.Right, Key.D }, new[] { JoyButton.DpadRight }),
        new(Slipstream, "Use slipstream", new[] { Key.Space, Key.B }, new[] { JoyButton.A, JoyButton.X }),
        new(Pause, "Pause", new[] { Key.Escape, Key.P }, new[] { JoyButton.Start }),
        new(Camera, "Change camera", new[] { Key.C }, new[] { JoyButton.Y }),
        new(ToggleHud, "Show / hide HUD", new[] { Key.H }, Array.Empty<JoyButton>()),
        new(NextTrack, "Next music track", new[] { Key.N }, new[] { JoyButton.Back }),
        new(Screenshot, "Screenshot", new[] { Key.F12 }, Array.Empty<JoyButton>()),
        new(SimPowerUp, "Simulator: more power", new[] { Key.W, Key.Equal }, Array.Empty<JoyButton>()),
        new(SimPowerDown, "Simulator: less power", new[] { Key.S, Key.Minus }, Array.Empty<JoyButton>()),
        new(TimeSlower, "Test rides: slower time", new[] { Key.F6 }, Array.Empty<JoyButton>()),
        new(TimeFaster, "Test rides: faster time", new[] { Key.F7 }, Array.Empty<JoyButton>()),
    };

    static readonly (string action, JoyAxis axis, float dir)[] Axes =
    {
        (ForkLeft, JoyAxis.LeftX, -1f),
        (ForkRight, JoyAxis.LeftX, 1f),
        (SimPowerUp, JoyAxis.TriggerRight, 1f),
        (SimPowerDown, JoyAxis.TriggerLeft, 1f),
    };

    public static void Register()
    {
        foreach (var b in Bindings)
            if (!InputMap.HasAction(b.Action)) InputMap.AddAction(b.Action, 0.45f);
        Apply();
    }

    /// <summary>Rebuilds the input map from defaults + the user's overrides.</summary>
    public static void Apply()
    {
        foreach (var b in Bindings)
        {
            InputMap.ActionEraseEvents(b.Action);
            foreach (var k in KeysFor(b.Action).Where(k => k != Key.None))
                InputMap.ActionAddEvent(b.Action, new InputEventKey { PhysicalKeycode = k });
            foreach (var p in PadsFor(b.Action).Where(p => p != JoyButton.Invalid))
                InputMap.ActionAddEvent(b.Action, new InputEventJoypadButton { ButtonIndex = p, Device = -1 });
        }
        foreach (var (action, axis, dir) in Axes)
            InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = dir, Device = -1 });
    }

    static Binding Find(string action) => Bindings.First(b => b.Action == action);

    public static Key[] KeysFor(string action)
    {
        if (Settings.Current.KeyBindings.TryGetValue(action, out var custom)) return Pad(custom.Select(k => (Key)k), Key.None);
        return Pad(Find(action).Keys, Key.None);
    }

    public static JoyButton[] PadsFor(string action)
    {
        if (Settings.Current.PadBindings.TryGetValue(action, out var custom)) return Pad(custom.Select(p => (JoyButton)p), JoyButton.Invalid);
        return Pad(Find(action).Pads, JoyButton.Invalid);
    }

    static T[] Pad<T>(IEnumerable<T> items, T empty)
    {
        var list = items.Take(Slots).ToList();
        while (list.Count < Slots) list.Add(empty);
        return list.ToArray();
    }

    /// <summary>Binds a key to a slot. The key is removed from any other action. Key.None clears.</summary>
    public static void SetKey(string action, int slot, Key key)
    {
        var s = Settings.Current;
        if (key != Key.None)
            foreach (var b in Bindings)
            {
                var keys = KeysFor(b.Action);
                if (!keys.Contains(key) || (b.Action == action)) continue;
                s.KeyBindings[b.Action] = keys.Select(k => k == key ? 0L : (long)k).ToList();
            }
        var mine = KeysFor(action);
        for (int i = 0; i < mine.Length; i++) if (i != slot && mine[i] == key) mine[i] = Key.None;
        mine[slot] = key;
        s.KeyBindings[action] = mine.Select(k => (long)k).ToList();
        Settings.Save();
        Apply();
    }

    /// <summary>Binds a gamepad button to a slot. The button is removed from any other action.</summary>
    public static void SetPad(string action, int slot, JoyButton button)
    {
        var s = Settings.Current;
        if (button != JoyButton.Invalid)
            foreach (var b in Bindings)
            {
                var pads = PadsFor(b.Action);
                if (!pads.Contains(button) || b.Action == action) continue;
                s.PadBindings[b.Action] = pads.Select(p => p == button ? -1L : (long)p).ToList();
            }
        var mine = PadsFor(action);
        for (int i = 0; i < mine.Length; i++) if (i != slot && mine[i] == button) mine[i] = JoyButton.Invalid;
        mine[slot] = button;
        s.PadBindings[action] = mine.Select(p => (long)p).ToList();
        Settings.Save();
        Apply();
    }

    public static void ResetToDefaults()
    {
        Settings.Current.KeyBindings.Clear();
        Settings.Current.PadBindings.Clear();
        Settings.Save();
        Apply();
    }

    public static string KeyName(Key k)
    {
        if (k == Key.None) return "";
        var logical = DisplayServer.KeyboardGetKeycodeFromPhysical(k);
        string name = OS.GetKeycodeString(logical == Key.None ? k : logical);
        return name switch { "Up" => "↑", "Down" => "↓", "Left" => "←", "Right" => "→", "Equal" => "=", "Minus" => "-", _ => name };
    }

    public static string PadName(JoyButton b) => b switch
    {
        JoyButton.Invalid => "",
        JoyButton.A => "A", JoyButton.B => "B", JoyButton.X => "X", JoyButton.Y => "Y",
        JoyButton.LeftShoulder => "LB", JoyButton.RightShoulder => "RB",
        JoyButton.LeftStick => "L-stick click", JoyButton.RightStick => "R-stick click",
        JoyButton.Back => "Back", JoyButton.Start => "Start", JoyButton.Guide => "Guide",
        JoyButton.DpadUp => "D-pad ↑", JoyButton.DpadDown => "D-pad ↓", JoyButton.DpadLeft => "D-pad ←", JoyButton.DpadRight => "D-pad →",
        _ => $"Button {(int)b}",
    };

    /// <summary>Short human description of the current bindings, e.g. "Space / B · pad A".</summary>
    public static string Describe(string action, bool includePad = true)
    {
        string keys = string.Join(" / ", KeysFor(action).Where(k => k != Key.None).Select(KeyName));
        var pads = PadsFor(action).Where(p => p != JoyButton.Invalid).Select(PadName).ToList();
        if (!includePad || pads.Count == 0) return keys == "" ? "unbound" : keys;
        return (keys == "" ? "" : keys + "  ·  ") + "pad " + string.Join(" / ", pads);
    }
}
