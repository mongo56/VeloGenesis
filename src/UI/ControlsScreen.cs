using System;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.UI;

/// <summary>Key / gamepad remapping: two keyboard and two gamepad slots per action.</summary>
public partial class ControlsScreen : Screen
{
    public event Action Back;

    VBoxContainer _rows;
    Label _status;
    string _listenAction;
    int _listenSlot;
    bool _listenPad;
    ulong _listenStartMs;
    Button _listenButton;

    protected override void Build()
    {
        Dim = 0.55f;
        var panel = UiTheme.Anchor(UiTheme.Panel(0.92f, 28), 0.5f, 0.5f, -720, -470, 1440, 940);
        AddChild(panel);
        var v = UiTheme.VBox(10);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("CONTROLS", 48, UiTheme.Cyan, bold: true));

        var header = UiTheme.HBox(12);
        header.AddChild(Col(UiTheme.Label("ACTION", 18, UiTheme.Magenta, bold: true), 470));
        header.AddChild(Col(UiTheme.Label("KEY 1", 18, UiTheme.Magenta, bold: true), 190));
        header.AddChild(Col(UiTheme.Label("KEY 2", 18, UiTheme.Magenta, bold: true), 190));
        header.AddChild(Col(UiTheme.Label("GAMEPAD 1", 18, UiTheme.Magenta, bold: true), 210));
        header.AddChild(Col(UiTheme.Label("GAMEPAD 2", 18, UiTheme.Magenta, bold: true), 210));
        v.AddChild(header);

        var scroll = new ScrollContainer { FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = UiTheme.VBox(6);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        v.AddChild(scroll);

        _status = UiTheme.Label("", 19, UiTheme.Amber);
        v.AddChild(_status);
        var help = UiTheme.Label("Select a slot (click or Enter), then press the new key or gamepad button.  Esc cancels  ·  Delete / Backspace clears the slot.  " +
                                 "A key can only belong to one action – assigning it moves it.  The left stick (forks) and triggers (simulator power) always work too.", 16, UiTheme.Dim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(help);

        var buttons = UiTheme.HBox(16);
        var back = UiTheme.Button("◀  BACK", () => Back?.Invoke(), 24, 220);
        buttons.AddChild(back);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(UiTheme.Button("RESET TO DEFAULTS", () => { InputSetup.ResetToDefaults(); _status.Text = "Defaults restored."; Rebuild(); }, 20));
        v.AddChild(buttons);
        FirstFocus = back;
        Rebuild();
    }

    protected override void Cancel() => Back?.Invoke();

    static Control Col(Control c, float width)
    {
        c.CustomMinimumSize = new Vector2(width, 0);
        return c;
    }

    void Rebuild()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        foreach (var b in InputSetup.Bindings)
        {
            var row = UiTheme.HBox(12);
            row.AddChild(Col(UiTheme.Label(b.Label, 20), 470));
            var keys = InputSetup.KeysFor(b.Action);
            var pads = InputSetup.PadsFor(b.Action);
            for (int i = 0; i < InputSetup.Slots; i++) row.AddChild(SlotButton(b.Action, i, false, InputSetup.KeyName(keys[i]), 190));
            for (int i = 0; i < InputSetup.Slots; i++) row.AddChild(SlotButton(b.Action, i, true, InputSetup.PadName(pads[i]), 210));
            _rows.AddChild(row);
        }
    }

    Button SlotButton(string action, int slot, bool pad, string text, float width)
    {
        var btn = UiTheme.Button(text == "" ? "—" : text, null, 19, width);
        btn.Pressed += () => StartListening(btn, action, slot, pad);
        return btn;
    }

    void StartListening(Button btn, string action, int slot, bool pad)
    {
        _listenAction = action;
        _listenSlot = slot;
        _listenPad = pad;
        _listenButton = btn;
        _listenStartMs = Time.GetTicksMsec();
        btn.Text = pad ? "press a button…" : "press a key…";
        _status.Text = "";
    }

    void StopListening()
    {
        var (action, slot, pad) = (_listenAction, _listenSlot, _listenPad);
        _listenAction = null;
        _listenButton = null;
        Rebuild();
        // Keep the cursor on the slot that was just edited (the old buttons are gone).
        int row = Array.FindIndex(InputSetup.Bindings, b => b.Action == action);
        if (row >= 0) ((Control)_rows.GetChild(_rows.GetChildCount() - InputSetup.Bindings.Length + row).GetChild(1 + (pad ? InputSetup.Slots : 0) + slot)).GrabFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (_listenAction == null) { base._Input(e); return; }
        // Ignore the press that opened the slot (e.g. gamepad A / Enter).
        if (Time.GetTicksMsec() - _listenStartMs < 200) return;

        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            GetViewport().SetInputAsHandled();
            if (key == Key.Escape) { StopListening(); return; }
            if (key is Key.Delete or Key.Backspace)
            {
                if (_listenPad) InputSetup.SetPad(_listenAction, _listenSlot, JoyButton.Invalid);
                else InputSetup.SetKey(_listenAction, _listenSlot, Key.None);
                StopListening();
                return;
            }
            if (_listenPad) { _status.Text = "Press a gamepad button for this slot (Esc to cancel)."; return; }
            InputSetup.SetKey(_listenAction, _listenSlot, key);
            _status.Text = $"Bound {InputSetup.KeyName(key)}.";
            StopListening();
        }
        else if (e is InputEventJoypadButton j && j.Pressed)
        {
            GetViewport().SetInputAsHandled();
            if (!_listenPad) { _status.Text = "Press a keyboard key for this slot (Esc to cancel)."; return; }
            InputSetup.SetPad(_listenAction, _listenSlot, j.ButtonIndex);
            _status.Text = $"Bound gamepad {InputSetup.PadName(j.ButtonIndex)}.";
            StopListening();
        }
    }
}
