using System;

namespace VeloGenesis.Ride;

/// <summary>
/// Virtual drivetrain for free ride. The bike stays in one physical gear on the trainer; we change
/// the grade sent to the trainer so the resistance at a given cadence matches the virtual gear.
/// </summary>
public class Gearing
{
    public const float WheelCircumference = 2.105f;
    /// <summary>Ratio we assume when the trainer doesn't report flywheel speed (≈50x17).</summary>
    const float FallbackTrainerRatio = 2.94f;
    /// <summary>Mass most trainers assume internally in sim mode.</summary>
    const float TrainerAssumedMass = 85f;

    public static readonly float[] Ratios =
    {
        0.80f, 0.90f, 1.00f, 1.10f, 1.21f, 1.33f, 1.46f, 1.60f, 1.75f, 1.91f, 2.08f, 2.26f,
        2.45f, 2.65f, 2.86f, 3.08f, 3.31f, 3.55f, 3.80f, 4.06f, 4.33f, 4.61f, 4.90f, 5.20f,
    };

    public int Index = 11;
    public int Count => Ratios.Length;
    public float Ratio => Ratios[Index];

    float _smoothedGrade;

    public bool Shift(int dir)
    {
        int n = Math.Clamp(Index + dir, 0, Ratios.Length - 1);
        if (n == Index) return false;
        Index = n;
        return true;
    }

    /// <summary>
    /// Grade to send to the trainer so that pedalling at <paramref name="cadence"/> in the current
    /// virtual gear costs the same power as riding at that virtual speed on <paramref name="feelGrade"/>.
    /// </summary>
    public float TrainerGrade(float feelGrade, float cadence, float trainerSpeed, float riderMass, float crr, float cw, float dt)
    {
        float vVirtual = cadence / 60f * Ratio * WheelCircumference;
        float vTrainer = trainerSpeed > 0.5f ? trainerSpeed : cadence / 60f * FallbackTrainerRatio * WheelCircumference;

        float target;
        if (cadence < 20f || vTrainer < 1.5f)
        {
            // Not pedalling enough to solve reliably: just bias the grade by gear.
            target = feelGrade + (Ratio / Ratios[11] - 1f) * 4f;
        }
        else
        {
            float powerNeeded = RiderPhysics.ResistiveForce(riderMass, crr, cw, feelGrade, vVirtual) * vVirtual;
            float forceAtTrainer = powerNeeded / vTrainer;
            float sin = (forceAtTrainer - cw * vTrainer * vTrainer) / (TrainerAssumedMass * RiderPhysics.G) - crr;
            sin = Math.Clamp(sin, -0.3f, 0.3f);
            target = MathF.Tan(MathF.Asin(sin)) * 100f;
        }

        target = Math.Clamp(target, -10f, 20f);
        _smoothedGrade += (target - _smoothedGrade) * (1f - MathF.Exp(-dt / 0.8f));
        return _smoothedGrade;
    }
}
