using System;

namespace VeloGenesis.Ride;

/// <summary>Converts measured power into virtual road speed using a standard cycling force model.</summary>
public class RiderPhysics
{
    public const float G = 9.81f;
    public const float Rho = 1.225f;

    public float Mass = 84f;
    public float CdA = 0.30f;
    public float Crr = 0.004f;
    /// <summary>Current speed in m/s.</summary>
    public float Speed;

    /// <summary>Multiplier on air drag; below 1 while drafting in a slipstream.</summary>
    public float DraftFactor = 1f;

    /// <summary>Wind resistance coefficient (0.5·ρ·CdA) in kg/m, as FTMS expects it.</summary>
    public float Cw => 0.5f * Rho * CdA * DraftFactor;

    public static float ResistiveForce(float mass, float crr, float cw, float gradePct, float v)
    {
        float theta = MathF.Atan(gradePct / 100f);
        return mass * G * (MathF.Sin(theta) + crr * MathF.Cos(theta)) + cw * v * v;
    }

    public void Step(float power, float gradePct, float dt)
    {
        // Below ~1.5 m/s P/v explodes; cap the drive force so starts feel natural.
        float drive = power / MathF.Max(Speed, 1.5f);
        float resist = ResistiveForce(Mass, Crr, Cw, gradePct, Speed);
        float acc = (drive - resist) / (Mass + 1.2f); // + rotating wheel mass
        Speed = Math.Clamp(Speed + acc * dt, 0f, 27f);
        if (power < 1f && Speed < 0.4f && gradePct >= -0.5f) Speed = 0f;
    }
}
