using System;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.Devices;

/// <summary>
/// Keyboard / gamepad driven virtual trainer for testing without hardware.
/// Free ride: W/S (or triggers) change your effort in watts. ERG: power follows the target,
/// W/S change cadence (to test cadence-based fork choices).
/// </summary>
public class SimulatedTrainer : ITrainer
{
    float _effort = 180f;
    float _ergCadenceOffset;
    bool _erg;
    int _ergTarget;
    float _power, _cadence;
    double _t;

    /// <summary>Set by the ride each frame so cadence can follow virtual speed and gearing.</summary>
    public float VirtualSpeed;
    public float GearRatio = 2.6f;
    /// <summary>Attract-mode / automated runs: hold this power without input.</summary>
    public bool AutoPilot;

    public string Name => "Simulator (keyboard)";
    public ConnState State => ConnState.Connected;
    public bool IsSimulated => true;
    public float Power => _power;
    public float Cadence => _cadence;
    public float TrainerSpeed => 0;
    public int HeartRate => 0;
    public double DataAge => 0;
    public float Effort { get => _effort; set => _effort = Math.Clamp(value, 0, 1200); }

    public void SetErgTarget(int watts) { _erg = true; _ergTarget = watts; }
    public void SetSimulation(float gradePercent, float crr, float cw) { _erg = false; }

    public void Tick(double dt)
    {
        _t += dt;
        float f = (float)dt;
        if (!AutoPilot)
        {
            float up = Input.GetActionStrength(InputSetup.SimPowerUp);
            float down = Input.GetActionStrength(InputSetup.SimPowerDown);
            if (_erg) _ergCadenceOffset = Math.Clamp(_ergCadenceOffset + (up - down) * 20f * f, -35, 35);
            else _effort = Math.Clamp(_effort + (up - down) * 120f * f, 0, 1200);
        }

        float wobble = 6f * MathF.Sin((float)_t * 1.7f) + 4f * MathF.Sin((float)_t * 0.43f);
        float target = _erg ? _ergTarget : _effort;
        _power = Mathf.Lerp(_power, Math.Max(0, target + (target > 0 ? wobble : 0)), 1f - MathF.Exp(-f * (_erg ? 1.5f : 4f)));

        float cad;
        if (_erg) cad = _power > 5 ? 88 + _ergCadenceOffset + 2f * MathF.Sin((float)_t * 0.9f) : 0;
        else cad = _power < 5 ? 0 : VirtualSpeed / (GearRatio * 2.105f) * 60f;
        _cadence = Mathf.Lerp(_cadence, Math.Clamp(cad, 0, 140), 1f - MathF.Exp(-f * 3f));
    }

    public void Disconnect() { }
}
