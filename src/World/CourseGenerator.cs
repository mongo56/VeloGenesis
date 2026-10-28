using System;
using System.Collections.Generic;
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

    /// <summary>Distance between biome changes (the next fork after this point enters a new one).</summary>
    public const float BiomeLength = 7000f;
    int _biome = DebugStartBiome;
    /// <summary>Debug: biome the course starts in.</summary>
    public static int DebugStartBiome;
    float _biomeStart;
    readonly System.Collections.Generic.List<int> _biomeQueue = new();

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

    /// <summary>
    /// Visual-only road leading up to the start line so the world doesn't end behind the rider.
    /// Seen from its far end it rises out of the horizon grid (<paramref name="depth"/> metres
    /// below the start) over <paramref name="rise"/> metres, then runs level into the start.
    /// Uses its own noise stream and no id, so the ridden course is unchanged.
    /// </summary>
    public Segment CreateLeadIn(Segment start, float length = 700f, float rise = 380f, float depth = 70f)
    {
        int n = (int)(length / Segment.Ds) + 1;
        var seg = new Segment
        {
            Id = -1,
            Kind = SectorKind.Start,
            Name = "Lead-in",
            StartDist = -(n - 1) * Segment.Ds,
            Pos = new Vector3[n],
            Heading = new float[n],
            Grade = new float[n],
            Curvature = new float[n],
            Primary = start.Primary,
            Secondary = start.Secondary,
            MountainAmp = start.MountainAmp,
            LeadInRise = rise,
        };

        // Walk backwards from the start line with a gentle wander, storing samples front to back.
        int curveSeed = WorldSeed ^ 0x5EED1E;
        var p = start.Pos[0];
        float h = start.Heading[0] + MathF.PI;
        for (int j = 0; j < n; j++)
        {
            int i = n - 1 - j;
            float y = start.Pos[0].Y - depth * (1f - Noise.SmoothStep(0, rise, i * Segment.Ds));
            seg.Pos[i] = new Vector3(p.X, y, p.Z);
            seg.Heading[i] = h - MathF.PI;
            float back = j * Segment.Ds;
            float k = Noise.Value1(back / 380f, curveSeed) * MaxCurvature * 0.7f * Noise.SmoothStep(0, 120, back);
            float hMid = h + k * Segment.Ds * 0.5f;
            h += k * Segment.Ds;
            p += Segment.ForwardOf(hMid) * Segment.Ds;
        }
        for (int i = 0; i < n - 1; i++) seg.Grade[i] = (seg.Pos[i + 1].Y - seg.Pos[i].Y) / Segment.Ds * 100f;
        seg.Grade[n - 1] = start.Grade[0];
        return seg;
    }

    /// <summary>Creates the two branches leaving the end of <paramref name="parent"/>.</summary>
    /// <summary>Roads a new branch must keep clear of (set per fork; the generator runs one fork at a time).</summary>
    List<RoadField> _avoid;

    /// <summary>How far a branch must stay from any road that isn't the stretch it grows out of.</summary>
    const float RoadClearance = 150f;

    public Segment[] CreateFork(Segment parent, float gainSoFar, IReadOnlyList<Segment> avoid = null)
    {
        _avoid = new List<RoadField>();
        if (avoid != null) foreach (var s in avoid) _avoid.Add(new RoadField(s));
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

        bool newBiome = distAtFork - _biomeStart >= BiomeLength;
        if (newBiome)
        {
            NextBiome();
            _biomeStart = distAtFork;
        }

        int hardSide = _rng.Next(2) == 0 ? -1 : 1;
        var hard = Build(parent, hardKind, len, hardGain, hardSide, hardKind == SectorKind.Rolling ? 1.15f : 1f);
        _avoid.Add(new RoadField(hard));
        var easy = Build(parent, easyKind, R(1600f, 2600f), easyGain, -hardSide, 0.9f);
        _avoid = null;
        hard.IsHarder = true;

        float hardNet = hard.Gain;
        float easyNet = Math.Max(0, easy.Gain);
        bool planHard = MathF.Abs(hardNet - needed) < MathF.Abs(easyNet - needed);
        hard.IsPlan = planHard;
        easy.IsPlan = !planHard;
        hard.EntersBiome = easy.EntersBiome = newBiome;
        AssignFeatures(hard, easy);

        // Children are always ordered left (index 0), right (index 1).
        return hardSide < 0 ? new[] { hard, easy } : new[] { easy, hard };
    }

    Segment Build(Segment parent, SectorKind kind, float length, float gainTarget, int side, float steepness)
    {
        var biome = Biome.All[_biome];
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
            BiomeIndex = _biome,
        };

        BuildGrades(seg.Grade, kind, gainTarget, grade);

        // Integrate the path. Curvature blends from the parent's so the road stays smooth.
        float wiggle = kind switch { SectorKind.Climb => 1.15f, SectorKind.Flat => 0.6f, _ => 1f };
        void Lay(int curveSeed, float wiggleScale)
        {
            Vector3 p = parent?.Pos[^1] ?? Vector3.Zero;
            float h = parent?.Heading[^1] ?? 0f;
            float k0 = parent?.Curvature[^1] ?? 0f;
            seg.Pos[0] = p;
            seg.Heading[0] = h;
            seg.Curvature[0] = k0;
            for (int i = 1; i < n; i++)
            {
                float s = i * Segment.Ds;
                float kNoise = Noise.Value1(s / 380f, curveSeed) * MaxCurvature * wiggle * wiggleScale * 1.25f;
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
        }

        // Roads must never cross or run alongside one another (nor loop back on themselves): re-roll
        // the bends, a little straighter each time, until the branch keeps its distance.
        int bestSeed = _rng.Next();
        float bestScale = 1f, bestClear = -1f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int curveSeed = attempt == 0 ? bestSeed : _rng.Next();
            float scale = MathF.Pow(0.8f, attempt);
            Lay(curveSeed, scale);
            float clear = Clearance(seg);
            if (clear > bestClear) { bestClear = clear; bestSeed = curveSeed; bestScale = scale; }
            if (clear >= RoadClearance) break;
        }
        Lay(bestSeed, bestScale);

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
        } * (kind == SectorKind.Start ? 1f : biome.Mountains);
        seg.Name = MakeName(kind);
        var palettes = biome.Palettes(kind);
        (seg.Primary, seg.Secondary) = palettes[_rng.Next(palettes.Length)];

        int gelCount = kind switch { SectorKind.Climb => _rng.Next(6, 10), SectorKind.Rolling => _rng.Next(4, 7), SectorKind.Start => 2, _ => _rng.Next(2, 4) };
        PlaceGels(seg, gelCount);
        return seg;
    }

    /// <summary>Closest the new road comes to any road to avoid, or to itself further along.</summary>
    float Clearance(Segment seg)
    {
        float best = float.MaxValue;
        var self = new RoadField(seg);
        var roads = _avoid == null ? new List<RoadField> { self } : new List<RoadField>(_avoid) { self };
        var rels = roads.ConvertAll(r => new RoadField.Relation(seg, r.Seg));
        for (int i = 0; i < seg.Count; i += 5)
        {
            var p = seg.Pos[i];
            for (int j = 0; j < roads.Count; j++)
                best = Math.Min(best, roads[j].Clearance(p.X, p.Z, rels[j], i * Segment.Ds));
        }
        return best;
    }

    void PlaceGels(Segment seg, int count)
    {
        if (!_cfg.PowerGels && _cfg.Mode == RideMode.FreeRide) count = 0;
        seg.Gels = new float[count];
        seg.GelTaken = new bool[count];
        for (int i = 0; i < count; i++)
            seg.Gels[i] = seg.Length * (i + 0.5f + R(-0.3f, 0.3f)) / count;
    }

    /// <summary>Moves on to a different biome, cycling through all of them in a shuffled order.</summary>
    void NextBiome()
    {
        if (_biomeQueue.Count == 0)
        {
            for (int i = 0; i < Biome.All.Length; i++) if (i != _biome) _biomeQueue.Add(i);
            for (int i = _biomeQueue.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_biomeQueue[i], _biomeQueue[j]) = (_biomeQueue[j], _biomeQueue[i]);
            }
        }
        _biome = _biomeQueue[0];
        _biomeQueue.RemoveAt(0);
    }

    /// <summary>
    /// Gives each branch something to ride for. The harder one tends to hold a ghost race or extra
    /// gels, the easier one a draft pack; either can carry a cadence rhythm zone.
    /// </summary>
    void AssignFeatures(Segment hard, Segment easy)
    {
        bool free = _cfg.Mode == RideMode.FreeRide;
        bool gels = _cfg.PowerGels || !free;
        var hardOpts = new System.Collections.Generic.List<(SegmentFeature f, float w)> { (SegmentFeature.Rhythm, 1f) };
        if (free && _cfg.GhostRaces) hardOpts.Add((SegmentFeature.Race, 2.2f));
        if (gels) hardOpts.Add((SegmentFeature.Gels, 1f));
        var easyOpts = new System.Collections.Generic.List<(SegmentFeature f, float w)>
        {
            (SegmentFeature.Pack, 2f), (SegmentFeature.Rhythm, 1f), (SegmentFeature.None, 0.5f),
        };
        hard.Feature = Weighted(hardOpts);
        easy.Feature = Weighted(easyOpts);
        if (hard.Feature == easy.Feature && hard.Feature == SegmentFeature.Rhythm) easy.Feature = SegmentFeature.Pack;

        foreach (var seg in new[] { hard, easy })
        {
            if (seg.Feature == SegmentFeature.Gels) PlaceGels(seg, seg.Gels.Length * 2 + 2);
            int zones = seg.Feature == SegmentFeature.Rhythm ? (seg.Length > 2200f ? 2 : 1)
                : !free && seg.Feature != SegmentFeature.Pack && _rng.NextDouble() < 0.35 ? 1 : 0;
            PlaceRhythm(seg, zones);
        }
    }

    SegmentFeature Weighted(System.Collections.Generic.List<(SegmentFeature f, float w)> opts)
    {
        float total = 0;
        foreach (var o in opts) total += o.w;
        float x = R(0, total);
        foreach (var o in opts)
        {
            if (x < o.w) return o.f;
            x -= o.w;
        }
        return opts[^1].f;
    }

    void PlaceRhythm(Segment seg, int count)
    {
        float from = 160f, to = seg.Length - WorldRoot.ForkWindow - 120f;
        var list = new System.Collections.Generic.List<RhythmZone>();
        for (int k = 0; k < count; k++)
        {
            float len = R(420f, 620f);
            float slot = (to - from) / count;
            if (slot < len + 40f) break;
            float start = from + slot * k + R(0, slot - len);
            int[] rpms = seg.Kind switch
            {
                SectorKind.Climb => new[] { 75, 80, 85 },
                SectorKind.Rolling => new[] { 85, 90, 95 },
                _ => new[] { 90, 95, 100 },
            };
            list.Add(new RhythmZone { Start = start, Length = len, Rpm = rpms[_rng.Next(rpms.Length)] });
        }
        seg.Rhythm = list.ToArray();
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
}
