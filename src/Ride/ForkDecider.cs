using System;

namespace VeloGenesis.Ride;

/// <summary>
/// Picks a branch at a fork. Push harder than your recent baseline to take the harder branch,
/// ease off for the easier one, or override with left/right input. In ERG mode the trainer fixes
/// power, so cadence is used instead (spin up / spin down).
/// </summary>
public class ForkDecider
{
    public bool Active { get; private set; }
    public int Selection { get; private set; }
    public bool Manual { get; private set; }
    public int HarderIndex { get; private set; }
    public int PlanIndex { get; private set; }
    public float Baseline { get; private set; }
    public bool UseCadence { get; private set; }

    /// <summary>-1..1: how far the current effort leans towards easier (-1) or harder (+1).</summary>
    public float Lean { get; private set; }

    public float UpThreshold => UseCadence ? Baseline + 6f : Baseline * 1.12f + 10f;
    public float DownThreshold => UseCadence ? Baseline - 6f : Baseline * 0.88f - 10f;

    public void Begin(float baseline, int harderIndex, int planIndex, bool useCadence)
    {
        Active = true;
        Manual = false;
        Baseline = baseline;
        HarderIndex = harderIndex;
        PlanIndex = planIndex;
        Selection = planIndex;
        UseCadence = useCadence;
        Lean = 0;
    }

    public void Update(float metric)
    {
        if (!Active) return;
        float span = Math.Max(UpThreshold - Baseline, 1f);
        Lean = Math.Clamp((metric - Baseline) / span, -1.5f, 1.5f);
        if (Manual) return;
        if (metric >= UpThreshold) Selection = HarderIndex;
        else if (metric <= DownThreshold) Selection = 1 - HarderIndex;
    }

    public void Choose(int index)
    {
        Selection = index;
        Manual = true;
    }

    public void End() => Active = false;
}
