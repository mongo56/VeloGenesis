using System;
using Godot;

namespace VeloGenesis.World;

/// <summary>Smoothed follow camera with several viewpoints (C / Y to cycle).</summary>
public partial class ChaseCamera : Camera3D
{
    public enum View { Chase, Close, Cinematic, High, Side }
    public View Mode = View.Chase;

    Vector3 _pos, _look;
    float _orbit;
    bool _init;

    public string ModeName => Mode.ToString().ToUpper();

    public void Cycle()
    {
        Mode = (View)(((int)Mode + 1) % 5);
    }

    public void Follow(Vector3 rider, Vector3 forward, float speed, float dt, float fovBoost = 0f)
    {
        var flat = new Vector3(forward.X, 0, forward.Z);
        flat = flat.LengthSquared() > 1e-4f ? flat.Normalized() : Vector3.Forward;
        var right = flat.Cross(Vector3.Up);
        Vector3 targetPos, targetLook;
        float lag = 5f;

        switch (Mode)
        {
            case View.Close:
                targetPos = rider - flat * 3.4f + Vector3.Up * 1.5f;
                targetLook = rider + flat * 10f + Vector3.Up * 1.1f;
                lag = 7f;
                break;
            case View.Cinematic:
                _orbit += dt * 0.12f;
                var o = right * MathF.Cos(_orbit) * 9f + flat * MathF.Sin(_orbit) * 9f;
                targetPos = rider + o + Vector3.Up * (2.2f + MathF.Sin(_orbit * 0.7f) * 1.2f);
                targetLook = rider + Vector3.Up * 0.9f;
                lag = 3f;
                break;
            case View.Side:
                targetPos = rider + right * 3.3f - flat * 0.6f + Vector3.Up * 1.05f;
                targetLook = rider + flat * 0.3f + Vector3.Up * 0.75f;
                lag = 8f;
                break;
            case View.High:
                targetPos = rider - flat * 20f + Vector3.Up * 10f;
                targetLook = rider + flat * 25f;
                lag = 3f;
                break;
            default:
                targetPos = rider - flat * 6.5f + Vector3.Up * 2.4f;
                targetLook = rider + flat * 14f + Vector3.Up * 1.0f;
                break;
        }

        if (!_init) { _pos = targetPos; _look = targetLook; _init = true; }
        float k = 1f - MathF.Exp(-dt * lag);
        _pos = _pos.Lerp(targetPos, k);
        _look = _look.Lerp(targetLook, 1f - MathF.Exp(-dt * lag * 1.4f));
        GlobalPosition = _pos;
        if (_pos.DistanceSquaredTo(_look) > 0.01f) LookAt(_look, Vector3.Up);
        Fov = Mathf.Lerp(Fov, 68f + Math.Clamp(speed * 3.6f - 25f, 0, 40) * 0.2f + fovBoost, 1f - MathF.Exp(-dt * 2.5f));
    }

    public void Snap() => _init = false;
}
