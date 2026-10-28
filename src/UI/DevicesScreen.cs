using System;
using Godot;
using VeloGenesis.Devices;

namespace VeloGenesis.UI;

/// <summary>Bluetooth pairing for the trainer and heart rate strap, with live readouts.</summary>
public partial class DevicesScreen : Screen
{
    public event Action Back;
    Button _scanBtn;
    VBoxContainer _list;
    Label _trainerLabel, _hrLabel, _live, _scanStatus;
    Button _trainerDisc, _hrDisc;
    CheckButton _showAll;
    double _refresh;

    protected override void Build()
    {
        Dim = 0.5f;
        var panel = UiTheme.Anchor(UiTheme.Panel(0.9f, 30), 0.5f, 0.5f, -640, -440, 1280, 880);
        AddChild(panel);
        var v = UiTheme.VBox(12);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("DEVICES", 48, UiTheme.Cyan, bold: true));

        v.AddChild(Section("CONNECTED"));
        var tr = UiTheme.HBox(16);
        _trainerLabel = UiTheme.Label("", 22);
        _trainerLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tr.AddChild(_trainerLabel);
        _trainerDisc = UiTheme.Button("DISCONNECT", () => DeviceManager.DisconnectTrainer(true), 20);
        tr.AddChild(_trainerDisc);
        v.AddChild(tr);
        var hr = UiTheme.HBox(16);
        _hrLabel = UiTheme.Label("", 22);
        _hrLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hr.AddChild(_hrLabel);
        _hrDisc = UiTheme.Button("DISCONNECT", () => DeviceManager.DisconnectHr(true), 20);
        hr.AddChild(_hrDisc);
        v.AddChild(hr);
        _live = UiTheme.Label("", 26, UiTheme.Amber, bold: true);
        v.AddChild(_live);

        v.AddChild(Section("NEARBY"));
        var srow = UiTheme.HBox(16);
        _scanBtn = UiTheme.Button("SCAN", ToggleScan, 24, 200);
        srow.AddChild(_scanBtn);
        _showAll = new CheckButton { Text = "Show all named devices" };
        _showAll.AddThemeFontSizeOverride("font_size", 18);
        _showAll.Toggled += _ => RebuildList();
        srow.AddChild(_showAll);
        _scanStatus = UiTheme.Label("", 18, UiTheme.Dim);
        srow.AddChild(_scanStatus);
        v.AddChild(srow);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = UiTheme.VBox(6);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        v.AddChild(scroll);

        var help = UiTheme.Label("Wake the trainer by pedalling a few strokes, and make sure no other app (Zwift, the Cycplus app…) is connected to it – Bluetooth trainers accept only one controller at a time.", 17, UiTheme.Dim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(help);

        var back = UiTheme.Button("◀  BACK", () => { DeviceManager.Scanner.Stop(); Back?.Invoke(); }, 24, 200);
        v.AddChild(back);
        FirstFocus = _scanBtn;

        DeviceManager.Changed += Refresh;
        DeviceManager.Scanner.Changed += RebuildList;
        Refresh();
        if (DeviceManager.BleTrainer == null) ToggleScan();
    }

    public override void _ExitTree()
    {
        DeviceManager.Changed -= Refresh;
        DeviceManager.Scanner.Changed -= RebuildList;
        DeviceManager.Scanner.Stop();
    }

    void ToggleScan()
    {
        if (DeviceManager.Scanner.Scanning) DeviceManager.Scanner.Stop();
        else DeviceManager.Scanner.Start();
        RebuildList();
    }

    void Refresh()
    {
        if (!IsInstanceValid(this)) return;
        var t = DeviceManager.BleTrainer;
        _trainerLabel.Text = t == null ? "Trainer:  none – using keyboard simulator" : $"Trainer:  {t.Name}  ·  {t.Status}" + (t.LastControlResult != "" ? $"  ·  {t.LastControlResult}" : "");
        _trainerDisc.Visible = t != null;
        var h = DeviceManager.Hr;
        _hrLabel.Text = h == null ? "Heart rate:  none" : $"Heart rate:  {h.Name}  ·  {h.Status}";
        _hrDisc.Visible = h != null;
    }

    void RebuildList()
    {
        if (!IsInstanceValid(_list)) return;
        var sc = DeviceManager.Scanner;
        _scanBtn.Text = sc.Scanning ? "STOP SCAN" : "SCAN";
        _scanStatus.Text = sc.Error != "" ? sc.Error : sc.Scanning ? "Scanning…" : "";
        foreach (var c in _list.GetChildren()) c.QueueFree();
        foreach (var d in sc.Devices)
        {
            if (!d.IsTrainer && !d.IsHeartRate && !_showAll.ButtonPressed) continue;
            var row = UiTheme.HBox(16);
            string kind = d.IsTrainer ? "TRAINER" : d.IsHeartRate ? "HEART RATE" : "OTHER";
            var l = UiTheme.Label($"{(string.IsNullOrWhiteSpace(d.Name) ? "(unnamed)" : d.Name)}   ·   {kind}   ·   {d.Rssi} dBm", 22, d.IsTrainer ? UiTheme.Cyan : d.IsHeartRate ? UiTheme.Red : UiTheme.Dim);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(l);
            var dev = d;
            if (d.IsTrainer || !d.IsHeartRate)
                row.AddChild(UiTheme.Button("USE AS TRAINER", () => { sc.Stop(); _ = DeviceManager.ConnectTrainer(dev.Address, dev.Name); }, 18));
            if (d.IsHeartRate || !d.IsTrainer)
                row.AddChild(UiTheme.Button("USE AS HR", () => { sc.Stop(); _ = DeviceManager.ConnectHr(dev.Address, dev.Name); }, 18));
            _list.AddChild(row);
        }
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        var t = DeviceManager.Trainer;
        int hr = DeviceManager.HeartRate;
        _live.Text = $"{t.Power:0} W   ·   {t.Cadence:0} rpm   ·   {(t.TrainerSpeed * 3.6f):0.0} km/h   ·   {(hr > 0 ? hr + " bpm" : "-- bpm")}";
        Refresh();
    }
}
