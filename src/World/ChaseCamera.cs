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

    /// <summary>Debug (--inspect=right,up,back[,lookY[,lookFwd]]): fixed close-up at this offset from the rider, looking at height lookY (hips by default), lookFwd metres ahead.</summary>
    public static Vector3? Inspect;
    public static float InspectLookY = 0.95f, InspectLookFwd;

    /// <summary>
    /// Debug (--orbit): close-up camera circling the rider. Left-drag orbits, right-drag moves the look point
    /// up / down, the wheel zooms.
    /// </summary>
    public static bool Orbit;
    float _yaw = -1.9f, _pitch = 0.25f, _dist = 1.8f, _lookY = 1.15f;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Orbit) return;
        if (e is InputEventMouseMotion m)
        {
            if ((m.ButtonMask & MouseButtonMask.Left) != 0)
            {
                _yaw -= m.Relative.X * 0.008f;
                _pitch = Math.Clamp(_pitch + m.Relative.Y * 0.006f, -0.6f, 1.4f);
            }
            else if ((m.ButtonMask & MouseButtonMask.Right) != 0)
                _lookY = Math.Clamp(_lookY - m.Relative.Y * 0.004f, 0.1f, 1.9f);
        }
        else if (e is InputEventMouseButton b && b.Pressed)
        {
            if (b.ButtonIndex == MouseButton.WheelUp) _dist = Math.Max(0.4f, _dist * 0.9f);
            else if (b.ButtonIndex == MouseButton.WheelDown) _dist = Math.Min(8f, _dist / 0.9f);
        }
    }

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

        if (Orbit)
        {
            // Orbit in the rider's frame (yaw 0 = straight behind), so it turns with the road.
            var look = rider + Vector3.Up * _lookY;
            var dir = (-flat * MathF.Cos(_yaw) + right * MathF.Sin(_yaw)) * MathF.Cos(_pitch) + Vector3.Up * MathF.Sin(_pitch);
            GlobalPosition = look + dir * _dist;
            LookAt(look, Vector3.Up);
            Fov = 50f;
            return;
        }
        if (Inspect is Vector3 io)
        {
            GlobalPosition = rider + right * io.X + Vector3.Up * io.Y - flat * io.Z;
            LookAt(rider + Vector3.Up * InspectLookY + flat * (InspectLookFwd - 0.05f), Vector3.Up);
            Fov = 40f;
            return;
        }

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
                // From the road side of the rider so the guard rail never blocks the view.
                targetPos = rider - right * 3.3f - flat * 0.6f + Vector3.Up * 1.05f;
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
