using System;
using Godot;
using VeloGenesis.Ride;

namespace VeloGenesis.World;

/// <summary>
/// Generates road segments from the ride setup. Each fork offers a harder branch (more climbing)
/// and an easier one; the "plan" branch keeps the ride on track for the requested elevation gain.
/// </summary>
public class CourseGenerator
{
    readonly RideConfig _cfg;
    readonly Random _rng;
    int _nextId;

    public int WorldSeed { get; }
    public float BaseAltitude { get; private set; }

    const float MaxCurvature = 1f / 300f;
    const float ForkCurvature = 1f / 270f;

    public CourseGenerator(RideConfig cfg)
    {
        _cfg = cfg;
        WorldSeed = cfg.Seed;
        _rng = new Random(cfg.Seed);
    }

    float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    float AvgGrade => Math.Clamp(_cfg.AvgGradient, 0.5f, 15f);

    public Segment CreateStart()
    {
        var seg = Build(null, SectorKind.Start, 900f, 0f, 0, 0f);
        seg.Name = "Boot Sequence";
        return seg;
    }

    /// <summary>Creates the two branches leaving the end of <paramref name="parent"/>.</summary>
    public Segment[] CreateFork(Segment parent, float gainSoFar)
    {
        float distAtFork = parent.StartDist + parent.Length;
        float rate = _cfg.GainPerMetre;
        float deficit = distAtFork * rate - gainSoFar; // > 0 means behind the plan
        float len = R(1600f, 2600f);
        float needed = Math.Max(0, rate * len + Math.Clamp(deficit * 0.8f, -rate * len, rate * len * 2f));
        float heightAboveBase = parent.EndElevation - BaseAltitude;

        // Harder branch
        SectorKind hardKind;
        float hardGain;
        if (_cfg.Climb && _rng.NextDouble() < 0.65)
        {
            hardKind = SectorKind.Climb;
            hardGain = Math.Max(needed * 1.6f, AvgGrade / 100f * 700f);
        }
        else
        {
            hardKind = SectorKind.Rolling;
            hardGain = Math.Max(needed * 1.6f, 25f);
        }

        // Easier branch
        SectorKind easyKind;
        float easyGain;
        if (heightAboveBase > 40f && _rng.NextDouble() < 0.7)
        {
            easyKind = SectorKind.Descent;
            easyGain = -Math.Min(heightAboveBase * 0.7f, len * AvgGrade / 100f * 0.45f);
        }
        else if (needed < 15f)
        {
            easyKind = SectorKind.Flat;
            easyGain = 0f;
        }
        else
        {
            easyKind = SectorKind.Rolling;
            easyGain = needed * 0.5f;
        }

        int hardSide = _rng.Next(2) == 0 ? -1 : 1;
        var hard = Build(parent, hardKind, len, hardGain, hardSide, hardKind == SectorKind.Rolling ? 1.15f : 1f);
        var easy = Build(parent, easyKind, R(1600f, 2600f), easyGain, -hardSide, 0.9f);
        hard.IsHarder = true;

        float hardNet = hard.Gain;
        float easyNet = Math.Max(0, easy.Gain);
        bool planHard = MathF.Abs(hardNet - needed) < MathF.Abs(easyNet - needed);
        hard.IsPlan = planHard;
        easy.IsPlan = !planHard;

        // Children are always ordered left (index 0), right (index 1).
        return hardSide < 0 ? new[] { hard, easy } : new[] { easy, hard };
    }

    Segment Build(Segment parent, SectorKind kind, float length, float gainTarget, int side, float steepness)
    {
        float grade = AvgGrade * steepness;
        if (kind == SectorKind.Climb) length = Math.Max(length, gainTarget / (grade / 100f) + 500f);
        length = Math.Min(length, 9000f);
        int n = (int)(length / Segment.Ds) + 1;

        var seg = new Segment
        {
            Id = _nextId++,
            Parent = parent,
            Side = side,
            Kind = kind,
            StartDist = parent == null ? 0 : parent.StartDist + parent.Length,
            Pos = new Vector3[n],
            Heading = new float[n],
            Grade = new float[n],
            Curvature = new float[n],
        };

        BuildGrades(seg.Grade, kind, gainTarget, grade);

        // Integrate the path. Curvature blends from the parent's so the road stays smooth.
        Vector3 p = parent?.Pos[^1] ?? Vector3.Zero;
        float h = parent?.Heading[^1] ?? 0f;
        float k0 = parent?.Curvature[^1] ?? 0f;
        int curveSeed = _rng.Next();
        float wiggle = kind switch { SectorKind.Climb => 1.15f, SectorKind.Flat => 0.6f, _ => 1f };
        seg.Pos[0] = p;
        seg.Heading[0] = h;
        seg.Curvature[0] = k0;
        for (int i = 1; i < n; i++)
        {
            float s = i * Segment.Ds;
            float kNoise = Noise.Value1(s / 380f, curveSeed) * MaxCurvature * wiggle * 1.25f;
            kNoise = Math.Clamp(kNoise, -MaxCurvature, MaxCurvature);
            float k = Noise.Lerp(k0, kNoise, Noise.SmoothStep(0, 160, s));
            k += side * ForkCurvature * (1f - Noise.SmoothStep(0, 340, s));
            seg.Curvature[i] = k;
            float hMid = h + k * Segment.Ds * 0.5f;
            h += k * Segment.Ds;
            var f = Segment.ForwardOf(hMid);
            p += f * Segment.Ds;
            p.Y += (seg.Grade[i - 1] + seg.Grade[i]) * 0.5f / 100f * Segment.Ds;
            seg.Pos[i] = p;
            seg.Heading[i] = h;
        }

        for (int i = 1; i < n; i++)
        {
            float d = seg.Pos[i].Y - seg.Pos[i - 1].Y;
            if (d > 0) seg.Gain += d; else seg.Drop -= d;
        }
        foreach (var g in seg.Grade) seg.MaxGrade = Math.Max(seg.MaxGrade, g);

        seg.MountainAmp = kind switch
        {
            SectorKind.Climb => R(1.2f, 1.6f),
            SectorKind.Flat => R(0.35f, 0.6f),
            SectorKind.Descent => R(0.8f, 1.1f),
            SectorKind.Start => 0.7f,
            _ => R(0.8f, 1.2f),
        };
        seg.Name = MakeName(kind);
        (seg.Primary, seg.Secondary) = Palette(kind);

        int gelCount = kind switch { SectorKind.Climb => _rng.Next(6, 10), SectorKind.Rolling => _rng.Next(4, 7), SectorKind.Start => 2, _ => _rng.Next(2, 4) };
        if (!_cfg.PowerGels && _cfg.Mode == RideMode.FreeRide) gelCount = 0;
        seg.Gels = new float[gelCount];
        seg.GelTaken = new bool[gelCount];
        for (int i = 0; i < gelCount; i++)
            seg.Gels[i] = seg.Length * (i + 0.5f + R(-0.3f, 0.3f)) / gelCount;
        return seg;
    }

    /// <summary>Fills the grade profile (percent) for a segment so it starts and ends flat.</summary>
    void BuildGrades(float[] grade, SectorKind kind, float gainTarget, float avg)
    {
        int n = grade.Length;
        float len = (n - 1) * Segment.Ds;
        int noiseSeed = _rng.Next();

        void Bump(float s0, float half, float peak, float downScale)
        {
            // Sine-shaped hill: rises over `half` metres, falls over half*downScale.
            for (int i = 0; i < n; i++)
            {
                float s = i * Segment.Ds;
                if (s >= s0 && s < s0 + half) grade[i] += peak * MathF.Sin(MathF.PI * (s - s0) / half);
                else if (s >= s0 + half && s < s0 + half + half * downScale)
                    grade[i] -= peak / downScale * MathF.Sin(MathF.PI * (s - s0 - half) / (half * downScale));
            }
        }

        switch (kind)
        {
            case SectorKind.Rolling:
            {
                float remaining = Math.Max(0, gainTarget);
                float s = R(60, 160);
                float typicalHill = Math.Clamp(gainTarget / Math.Max(1, len / 700f), 8f, 45f);
                while (remaining > 3f)
                {
                    float hgt = Math.Min(remaining, typicalHill * R(0.6f, 1.4f));
                    float g = avg * R(0.75f, 1.25f);
                    float half = hgt / (g / 100f);           // metres of climbing at mean grade g
                    float down = R(0.8f, 1.5f);
                    if (s + half * (1 + down) > len - 60f)
                    {
                        half = (len - 60f - s) / (1 + down);
                        if (half < 40f) break;
                        hgt = half * g / 100f;
                    }
                    Bump(s, half, g * MathF.PI / 2f, down);
                    remaining -= hgt;
                    s += half * (1 + down) + R(30, 220);
                }
                break;
            }
            case SectorKind.Climb:
            case SectorKind.Descent:
            {
                float sign = kind == SectorKind.Climb ? 1f : -1f;
                float rise = MathF.Abs(gainTarget);
                float g = kind == SectorKind.Climb ? avg : avg * 0.9f;
                float clen = Math.Min(rise / (g / 100f), len - 260f);
                float s0 = R(100, Math.Max(110, len - clen - 140f));
                const float ramp = 120f;
                int climbNoise = _rng.Next();
                for (int i = 0; i < n; i++)
                {
                    float s = i * Segment.Ds - s0;
                    if (s < 0 || s > clen) continue;
                    float w = Noise.SmoothStep(0, ramp, s) * Noise.SmoothStep(0, ramp, clen - s);
                    float variation = 1f + 0.35f * Noise.Value1(s / 220f, climbNoise);
                    grade[i] = sign * g * w * variation;
                }
                // Rescale so the climb delivers exactly the requested rise.
                float total = 0;
                for (int i = 0; i < n; i++) total += grade[i] / 100f * Segment.Ds;
                if (MathF.Abs(total) > 1f)
                    for (int i = 0; i < n; i++) grade[i] *= rise * sign / total;
                break;
            }
        }

        // Micro undulation everywhere, faded out at the ends so forks are flat.
        float micro = kind == SectorKind.Flat || kind == SectorKind.Start ? 0.6f : 0.8f;
        for (int i = 0; i < n; i++)
        {
            float s = i * Segment.Ds;
            float w = Noise.SmoothStep(0, 80, s) * Noise.SmoothStep(0, 80, len - s);
            grade[i] = Math.Clamp(grade[i] + micro * Noise.Value1(s / 90f, noiseSeed) * w, -16f, 18f);
        }
    }

    static readonly string[] Adjectives =
    {
        "Neon", "Ion", "Photon", "Vector", "Quantum", "Chrome", "Laser", "Nova", "Cobalt", "Magenta",
        "Static", "Pulse", "Hex", "Binary", "Prism", "Solar", "Midnight", "Cyber", "Voltage", "Echo",
        "Turbo", "Vapor", "Holo", "Plasma", "Sector-7", "Arcade", "Grid", "Fractal",
    };

    static readonly string[] ClimbNouns = { "Ascent", "Spire", "Ridge", "Summit", "Pass", "Climb", "Peak" };
    static readonly string[] RollNouns = { "Waves", "Circuit", "Rollers", "Dunes", "Switchback", "Run" };
    static readonly string[] DescentNouns = { "Plunge", "Cascade", "Dive", "Drop", "Canyon", "Freefall" };
    static readonly string[] FlatNouns = { "Expanse", "Valley", "Causeway", "Flats", "Highway", "Boulevard" };

    string MakeName(SectorKind kind)
    {
        var nouns = kind switch
        {
            SectorKind.Climb => ClimbNouns,
            SectorKind.Descent => DescentNouns,
            SectorKind.Flat => FlatNouns,
            _ => RollNouns,
        };
        return $"{Adjectives[_rng.Next(Adjectives.Length)]} {nouns[_rng.Next(nouns.Length)]}";
    }

    static readonly Color Cyan = new(0.1f, 0.9f, 1f), Magenta = new(1f, 0.15f, 0.8f), Orange = new(1f, 0.45f, 0.08f),
        Red = new(1f, 0.12f, 0.3f), Amber = new(1f, 0.75f, 0.15f), Violet = new(0.6f, 0.3f, 1f), Lime = new(0.45f, 1f, 0.3f),
        Teal = new(0.1f, 1f, 0.7f), Blue = new(0.25f, 0.45f, 1f), Pink = new(1f, 0.4f, 0.75f);

    (Color, Color) Palette(SectorKind kind)
    {
        (Color, Color)[] options = kind switch
        {
            SectorKind.Climb => new[] { (Orange, Magenta), (Red, Amber), (Amber, Red) },
            SectorKind.Descent => new[] { (Cyan, Blue), (Teal, Cyan), (Blue, Violet) },
            SectorKind.Flat => new[] { (Lime, Cyan), (Teal, Violet) },
            SectorKind.Start => new[] { (Cyan, Magenta) },
            _ => new[] { (Magenta, Cyan), (Violet, Pink), (Pink, Cyan) },
        };
        return options[_rng.Next(options.Length)];
    }
}
