using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloGenesis.Ride;

public enum WorkoutType { Endurance, Tempo, SweetSpot, Threshold, VO2Max, OverUnder, Pyramid, Surprise }

public class Interval
{
    public float Duration;   // seconds
    public float StartPct;   // fraction of FTP
    public float EndPct;
    public string Label;

    public Interval(float minutes, float start, float end, string label)
    {
        Duration = minutes * 60f;
        StartPct = start;
        EndPct = end;
        Label = label;
    }

    public bool IsRamp => Math.Abs(StartPct - EndPct) > 0.005f;
    public float PctAt(float t) => StartPct + (EndPct - StartPct) * Math.Clamp(t / Duration, 0, 1);
}

public class Workout
{
    public string Name;
    public string Description;
    public List<Interval> Intervals = new();
    public float TotalDuration => Intervals.Sum(i => i.Duration);

    /// <summary>Finds the interval containing time t. Returns -1 when the workout is finished.</summary>
    public int Locate(float t, out float tIn)
    {
        float acc = 0;
        for (int i = 0; i < Intervals.Count; i++)
        {
            if (t < acc + Intervals[i].Duration) { tIn = t - acc; return i; }
            acc += Intervals[i].Duration;
        }
        tIn = 0;
        return -1;
    }

    public float TargetPct(float t)
    {
        int i = Locate(t, out float tIn);
        return i < 0 ? 0.5f : Intervals[i].PctAt(tIn);
    }
}

/// <summary>Builds structured workouts scaled to the rider's FTP and the chosen duration.</summary>
public static class WorkoutGenerator
{
    public static readonly WorkoutType[] All = (WorkoutType[])Enum.GetValues(typeof(WorkoutType));

    public static string Title(WorkoutType t) => t switch
    {
        WorkoutType.SweetSpot => "Sweet Spot",
        WorkoutType.VO2Max => "VO2 Max",
        WorkoutType.OverUnder => "Over-Unders",
        WorkoutType.Surprise => "Surprise Me",
        _ => t.ToString(),
    };

    public static Workout Generate(WorkoutType type, int minutes, int seed)
    {
        var rng = new Random(seed);
        if (type == WorkoutType.Surprise)
            type = All[rng.Next(All.Length - 1)];

        var w = new Workout { Name = Title(type) };
        float total = Math.Max(20, minutes);
        float warm = Math.Clamp(total * 0.15f, 6, 15);
        float cool = Math.Clamp(total * 0.1f, 5, 10);
        float main = total - warm - cool;

        w.Intervals.Add(new Interval(warm * 0.7f, 0.45f, 0.72f, "Warm-up"));
        w.Intervals.Add(new Interval(warm * 0.3f, 0.65f, 0.65f, "Warm-up"));

        var set = new List<Interval>();
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float Snap(float pct) => MathF.Round(pct * 100f) / 100f;
        float used = 0;
        void Add(Interval i) { set.Add(i); used += i.Duration / 60f; }
        bool Fits(float m) => used + m <= main + 0.01f;

        switch (type)
        {
            case WorkoutType.Endurance:
                w.Description = "Steady aerobic riding with short surges to stay sharp.";
                while (Fits(10.5f))
                {
                    var p = Snap(R(0.64f, 0.72f));
                    Add(new Interval(10, p, p, "Endurance"));
                    var s = Snap(R(1.05f, 1.2f));
                    Add(new Interval(0.5f, s, s, "Surge"));
                }
                break;
            case WorkoutType.Tempo:
                w.Description = "Long tempo blocks – comfortably hard.";
                while (Fits(20)) { var p = Snap(R(0.8f, 0.86f)); Add(new Interval(15, p, p, "Tempo")); Add(new Interval(5, 0.6f, 0.6f, "Recover")); }
                break;
            case WorkoutType.SweetSpot:
            {
                w.Description = "Sweet spot intervals just under threshold.";
                float len = rng.Next(2) == 0 ? 10 : (main >= 60 ? 20 : 12);
                while (Fits(len + 5)) { var p = Snap(R(0.88f, 0.93f)); Add(new Interval(len, p, p, "Sweet Spot")); Add(new Interval(5, 0.55f, 0.55f, "Recover")); }
                break;
            }
            case WorkoutType.Threshold:
            {
                w.Description = "Threshold repeats at FTP.";
                float len = rng.Next(8, 13);
                while (Fits(len + 5)) { var p = Snap(R(0.97f, 1.03f)); Add(new Interval(len, p, p, "Threshold")); Add(new Interval(5, 0.55f, 0.55f, "Recover")); }
                break;
            }
            case WorkoutType.VO2Max:
            {
                w.Description = "Hard VO2 max efforts with equal recovery.";
                float len = rng.Next(3) == 0 ? 4 : 3;
                int reps = 0;
                while (Fits(len * 2))
                {
                    var p = Snap(R(1.12f, 1.2f));
                    Add(new Interval(len, p, p, "VO2 Max"));
                    Add(new Interval(len, 0.5f, 0.5f, "Recover"));
                    if (++reps % 5 == 0 && Fits(8)) Add(new Interval(8, 0.6f, 0.6f, "Set break"));
                }
                break;
            }
            case WorkoutType.OverUnder:
                w.Description = "Alternate just under and just over FTP to train lactate clearance.";
                while (Fits(14))
                {
                    for (int k = 0; k < 3; k++) { Add(new Interval(2, 0.95f, 0.95f, "Under")); Add(new Interval(1, 1.08f, 1.08f, "Over")); }
                    Add(new Interval(5, 0.55f, 0.55f, "Recover"));
                }
                break;
            case WorkoutType.Pyramid:
                w.Description = "Build up and back down through VO2 efforts.";
                foreach (var m in new[] { 1f, 2, 3, 4, 3, 2, 1 })
                {
                    if (!Fits(m * 2)) break;
                    float p = Snap(1.22f - m * 0.03f);
                    Add(new Interval(m, p, p, "Pyramid"));
                    Add(new Interval(m, 0.5f, 0.5f, "Recover"));
                }
                break;
        }

        // Pad whatever is left with endurance riding, split around the main set.
        float rest = main - used;
        if (rest > 0.5f)
        {
            w.Intervals.Add(new Interval(rest * 0.5f, 0.65f, 0.65f, "Endurance"));
            w.Intervals.AddRange(set);
            w.Intervals.Add(new Interval(rest * 0.5f, 0.65f, 0.65f, "Endurance"));
        }
        else w.Intervals.AddRange(set);

        w.Intervals.Add(new Interval(cool, 0.65f, 0.42f, "Cool-down"));
        return w;
    }
}
