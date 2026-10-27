using System;
using System.Collections.Generic;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Devices;
using VeloGenesis.Ride;
using VeloGenesis.World;

namespace VeloGenesis.UI;

/// <summary>
/// Scripted ~70 s showcase for the trailer (run with --demo, ideally under Godot's --write-movie).
/// Drives the simulator power, cameras and features on a fixed timeline and overlays captions.
/// </summary>
public partial class DemoDirector : CanvasLayer
{
    const float RideStart = 18f;
    public const float Duration = RideStart + 63.5f;

    readonly Main _main;
    RideSession _ride;
    SetupScreen _setup;
    readonly List<(float t, Action a)> _cues = new();
    int _next;
    float _t;

    Label _title, _subtitle, _caption;
    ColorRect _black;
    float _titleA, _titleTarget, _captionA, _captionTarget, _blackA = 1f, _blackTarget;

    public DemoDirector() { }
    public DemoDirector(Main main) => _main = main;

    public override void _Ready()
    {
        Layer = 30;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Theme = UiTheme.Theme;
        AddChild(root);

        _title = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("VELOGENESIS", 150, UiTheme.Magenta, HorizontalAlignment.Center, true), 22), 0.5f, 0.4f, -900, -110, 1800, 180);
        _title.AddThemeColorOverride("font_shadow_color", new Color(UiTheme.Cyan, 0.7f));
        _title.AddThemeConstantOverride("shadow_offset_x", 5);
        _title.AddThemeConstantOverride("shadow_offset_y", 5);
        _subtitle = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("", 40, UiTheme.Cyan, HorizontalAlignment.Center, true), 12), 0.5f, 0.4f, -900, 80, 1800, 60);
        _caption = UiTheme.Anchor(UiTheme.Overlay(UiTheme.Label("", 44, UiTheme.Text, HorizontalAlignment.Center, true), 14), 0.5f, 0.68f, -900, 0, 1800, 70);
        _black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_title);
        root.AddChild(_subtitle);
        root.AddChild(_caption);
        root.AddChild(_black);
        _title.Modulate = _subtitle.Modulate = _caption.Modulate = new Color(1, 1, 1, 0);

        BuildTimeline();
    }

    void Cue(float t, Action a) => _cues.Add((t, a));
    void Effort(float w) => DeviceManager.Simulator.Effort = w;
    void Cam(ChaseCamera.View v) => _ride.Camera.Mode = v;
    void Hud(bool on) { if (_ride.Hud != null) _ride.Hud.Visible = on; }
    void Title(string sub) { _subtitle.Text = sub; _titleTarget = 1; }
    void Caption(string text, float anchorY = 0.68f)
    {
        _caption.Text = text;
        _caption.AnchorTop = _caption.AnchorBottom = anchorY;
        _captionTarget = 1;
    }
    void ClearCaption() => _captionTarget = 0;

    void BuildTimeline()
    {
        // ---- Menu: main menu over the self-riding flythrough, then the ride setup screen.
        Cue(0f, () => _blackTarget = 0);
        Cue(1.5f, () => Caption("Turn your smart trainer into a ride through procedural neon worlds", 0.905f));
        Cue(6.5f, () =>
        {
            ClearCaption();
            _setup = _main.DemoShowSetup();
        });
        Cue(7f, () => Caption("Free ride: set the gradient, climbing & distance — the world is built around you", 0.905f));
        Cue(11.5f, () => { _setup?.DemoSelectMode(RideMode.Erg); Caption("…or an ERG workout generated from your FTP", 0.905f); });
        Cue(15.5f, () => { _setup?.DemoSelectMode(RideMode.FreeRide); _setup?.DemoFocusStart(); ClearCaption(); });
        Cue(16.8f, () => _blackTarget = 1);
        Cue(RideStart - 0.3f, () =>
        {
            var s = Settings.Current;
            _ride = _main.DemoStartRide(new RideConfig
            {
                Mode = RideMode.FreeRide,
                AvgGradient = s.LastAvgGradient,
                ElevationGain = s.LastElevationGain,
                DistanceKm = s.LastDistanceKm,
                Climb = true,
                Seed = 2,
                GhostRaces = false, // the trailer triggers its own race
            });
        });

        // ---- Ride
        float r = RideStart;
        Cue(r, () =>
        {
            Effort(235);
            Cam(ChaseCamera.View.Chase);
            _ride.Hud?.SetCleanMode(true);
            _blackTarget = 0;
            Caption("Every ride is generated live — no two rides are the same");
        });
        Cue(r + 5.3f, () => { ClearCaption(); _blackTarget = 1; });
        Cue(r + 5.9f, () =>
        {
            // Jump to just before the first fork.
            var w = _ride.World;
            _ride.DebugSkip = Math.Max(1f, w.Current.Length - w.DistInSeg - 150f);
            Cam(ChaseCamera.View.Close);
        });
        Cue(r + 6.3f, () => { _blackTarget = 0; Caption("At every fork: push harder for the climb… or ease off"); });
        Cue(r + 8.5f, () => Effort(345));
        Cue(r + 18.5f, () => { ClearCaption(); Effort(285); Cam(ChaseCamera.View.Chase); });
        Cue(r + 22f, () => { _ride.DebugGhost = true; Effort(255); Caption("Race holographic ghost riders paced from your FTP"); });
        Cue(r + 30f, () => Caption("Grab power gels in Z4+ … then unleash the slipstream"));
        Cue(r + 32f, () => { _ride.DebugSlip = 3; Effort(310); });
        Cue(r + 37f, () => { Cam(ChaseCamera.View.Side); Effort(640); Caption("Stand up and sprint — the rider reacts to your power"); });
        Cue(r + 44f, () => { Cam(ChaseCamera.View.Chase); Effort(290); Caption("Bluetooth smart trainers · ERG workouts · virtual gears · Strava-ready rides"); });
        Cue(r + 50.5f, () => { ClearCaption(); Hud(false); Cam(ChaseCamera.View.Cinematic); Effort(260); });
        Cue(r + 55f, () => Title("ride the grid"));
        Cue(r + 61f, () => _blackTarget = 1);
        Cue(Duration, () =>
        {
            _ride.Finish(false); // don't leave a FIT file behind
            GetTree().Quit();
        });
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        while (_next < _cues.Count && _t >= _cues[_next].t) _cues[_next++].a();

        _titleA = Mathf.MoveToward(_titleA, _titleTarget, dt * 1.2f);
        _captionA = Mathf.MoveToward(_captionA, _captionTarget, dt * 2.5f);
        _blackA = Mathf.MoveToward(_blackA, _blackTarget, dt * (_blackTarget > _blackA ? 1.6f : 1.2f));
        _title.Modulate = new Color(1, 1, 1, _titleA);
        _subtitle.Modulate = new Color(1, 1, 1, Math.Clamp(_titleA * 1.5f - 0.5f, 0, 1));
        _caption.Modulate = new Color(1, 1, 1, _captionA);
        _black.Color = new Color(0, 0, 0, _blackA);
    }
}
