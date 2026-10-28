using System;
using System.Collections.Generic;
using VeloGenesis.UI;
using VeloGenesis.World;

namespace VeloGenesis.Ride;

/// <summary>
/// Cadence rhythm zones: arches mark a stretch of road with a target cadence. Hold it within the
/// window and your light trail locks green; off the beat, a metronome ticks at the target to pull
/// you back. Your accuracy over the zone earns a grade (and a slipstream charge for a perfect one).
/// Works the same in ERG, where cadence is the one thing you control.
/// </summary>
public partial class RideSession
{
    public const float RhythmWindow = 5f;
    const float LockDelay = 1.2f;

    RhythmZone _rz;
    bool _rzActive, _rzLocked;
    float _rzIn, _rzTotal, _rzLockTimer, _rzTick;
    readonly HashSet<(int seg, int zone)> _rzDone = new();
    int _rhythmZones, _rhythmPerfect, _rhythmGood;
    float _rhythmAccSum;

    public bool RhythmActive => _rzActive;
    public int RhythmRpm => _rz.Rpm;
    public bool RhythmLocked => _rzLocked;
    /// <summary>Share of the zone ridden so far within the cadence window (0..1).</summary>
    public float RhythmAccuracy => _rzTotal > 0 ? _rzIn / _rzTotal : 1f;
    public float RhythmProgress => _rzActive ? Math.Clamp((World.DistInSeg - _rz.Start) / _rz.Length, 0, 1) : 0;
    public float RhythmRemaining => _rzActive ? Math.Max(0, _rz.End - World.DistInSeg) : 0;
    /// <summary>Cadence used for the rhythm meter (3 s average).</summary>
    public float RhythmCadence => _c3.Value;

    /// <summary>Distance to the next rhythm zone on this segment, or -1.</summary>
    public float NextRhythmDistance
    {
        get
        {
            var seg = World.Current;
            if (seg == null || _rzActive) return -1;
            foreach (var z in seg.Rhythm)
                if (z.Start > World.DistInSeg) return z.Start - World.DistInSeg;
            return -1;
        }
    }

    /// <summary>Debug: add a 90 rpm zone just ahead on the current segment.</summary>
    public bool DebugRhythm;

    void UpdateRhythm(float dt)
    {
        if (Attract) return;
        var seg = World.Current;
        if (DebugRhythm)
        {
            DebugRhythm = false;
            seg.Rhythm = new[] { new RhythmZone { Start = World.DistInSeg + 60f, Length = 400f, Rpm = 90 } };
        }
        float s = World.DistInSeg;
        if (!_rzActive)
        {
            for (int i = 0; i < seg.Rhythm.Length; i++)
            {
                var z = seg.Rhythm[i];
                if (s < z.Start || s >= z.End || !_rzDone.Add((seg.Id, i))) continue;
                _rz = z;
                _rzActive = true;
                _rzLocked = false;
                _rzIn = _rzTotal = _rzLockTimer = _rzTick = 0;
                Music.Synth.SfxRhythmStart();
                Godot.GD.Print($"Rhythm zone: {z.Rpm} rpm over {z.Length:0} m on {seg.Name}");
                Hud?.Banner($"HOLD {z.Rpm} RPM  ·  ±{RhythmWindow:0}  ·  {z.Length:0} m", "RHYTHM ZONE", WorldRoot.RhythmColor);
                break;
            }
            if (!_rzActive) return;
        }

        if (s >= _rz.End) { FinishRhythm(); return; }

        float cad = _c3.Value;
        bool inWindow = MathF.Abs(cad - _rz.Rpm) <= RhythmWindow;
        _rzTotal += dt;
        if (inWindow) _rzIn += dt;

        // Lock / unlock with a short delay so a single pedal stroke doesn't flip it.
        _rzLockTimer = inWindow == _rzLocked ? 0 : _rzLockTimer + dt;
        if (_rzLockTimer >= LockDelay)
        {
            _rzLocked = inWindow;
            _rzLockTimer = 0;
            Music.Synth.SfxRhythmLock(_rzLocked);
            SetRhythmTrail(_rzLocked);
        }

        // Metronome at the target cadence while you're off the beat.
        if (!_rzLocked)
        {
            _rzTick -= dt;
            if (_rzTick <= 0)
            {
                _rzTick += 60f / _rz.Rpm;
                if (_rzTick < 0) _rzTick = 60f / _rz.Rpm;
                Music.Synth.SfxRhythmTick();
            }
        }
    }

    void SetRhythmTrail(bool locked)
    {
        if (SlipstreamActive) return;
        _trail.SetColor(locked ? WorldRoot.RhythmColor : World.Current.Primary);
    }

    void FinishRhythm()
    {
        _rzActive = false;
        if (_rzLocked) SetRhythmTrail(false);
        _rzLocked = false;
        float acc = RhythmAccuracy;
        _rhythmZones++;
        _rhythmAccSum += acc;
        int grade = acc >= 0.85f ? 2 : acc >= 0.6f ? 1 : 0;
        Godot.GD.Print($"Rhythm zone done: {acc * 100:0}% (grade {grade})");
        Music.Synth.SfxRhythmResult(grade);
        if (grade == 2)
        {
            _rhythmPerfect++;
            string reward = "";
            if (Config.PowerGels && SlipCharges < MaxSlipCharges)
            {
                SlipCharges++;
                reward = "  ·  +1 SLIPSTREAM CHARGE";
            }
            Hud?.Banner($"{acc * 100:0}% ON THE BEAT{reward}", "PERFECT RHYTHM", UiTheme.Lime);
        }
        else if (grade == 1)
        {
            _rhythmGood++;
            Hud?.Banner($"{acc * 100:0}% ON THE BEAT", "GOOD RHYTHM", UiTheme.Cyan);
        }
        else Hud?.Banner($"{acc * 100:0}% ON THE BEAT  ·  aim for {_rz.Rpm} ±{RhythmWindow:0} rpm", "OFF THE BEAT", UiTheme.Dim);
    }
}
