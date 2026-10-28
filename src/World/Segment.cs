using System;
using Godot;

namespace VeloGenesis.World;

public enum SectorKind { Start, Rolling, Climb, Descent, Flat }

/// <summary>What a branch offers besides its terrain – shown on the fork signs so the choice has stakes.</summary>
public enum SegmentFeature { None, Race, Pack, Rhythm, Gels }

/// <summary>A stretch of road where the rider tries to hold a target cadence.</summary>
public struct RhythmZone
{
    public float Start, Length;
    public int Rpm;
    public float End => Start + Length;
}

/// <summary>
/// One stretch of road between forks. Every segment ends in a fork with two child segments,
/// generated lazily as the rider approaches.
/// </summary>
public class Segment
{
    /// <summary>Path sample spacing in metres.</summary>
    public const float Ds = 2f;

    public int Id;
    public Segment Parent;
    public Segment[] Children;
    /// <summary>-1 = branches left from its parent, +1 = right, 0 = start.</summary>
    public int Side;
    public SectorKind Kind;
    public string Name;
    public Color Primary, Secondary;
    public bool IsHarder;
    public bool IsPlan;
    public SegmentFeature Feature;
    /// <summary>Index into <see cref="World.Biome.All"/>.</summary>
    public int BiomeIndex;
    public Biome Biome => Biome.All[BiomeIndex];
    /// <summary>True on the first segments of a new biome (both branches of that fork).</summary>
    public bool EntersBiome;
    public RhythmZone[] Rhythm = Array.Empty<RhythmZone>();

    /// <summary>Ride distance at the start of this segment.</summary>
    public float StartDist;

    public Vector3[] Pos;
    public float[] Heading;
    public float[] Grade;     // percent
    public float[] Curvature; // rad / m

    public float Gain, Drop, MaxGrade;
    /// <summary>
    /// > 0 only for the visual lead-in behind the start: over this many metres from its far end
    /// the road and landscape rise out of the horizon grid (no props, rails or lamps there).
    /// </summary>
    public float LeadInRise;
    public float MountainAmp = 1f;

    public float[] Gels = Array.Empty<float>();
    public bool[] GelTaken = Array.Empty<bool>();
    public MultiMesh GelMesh;

    /// <summary>Large scenery instances that must be hidden if any road comes within `clear` metres.</summary>
    public readonly System.Collections.Generic.List<(MultiMesh mm, int index, Vector2 pos, float clear)> Blockers = new();

    /// <summary>Terrain as last built / carved, kept so later roads can carve it again.</summary>
    public MeshData TerrainData;
    public MeshInstance3D TerrainNode;
    public readonly System.Collections.Generic.List<GroundedProp> GroundedProps = new();
    /// <summary>Scenery instances hidden as blockers (never moved back into view).</summary>
    public readonly System.Collections.Generic.HashSet<(MultiMesh, int)> Hidden = new();
    public float MinY;
    /// <summary>Per road sample: how far terrain may reach to the right / left (inside of bends).</summary>
    public float[] ReachRight, ReachLeft;

    public Node3D Visual;
    public bool Disposed;

    public int Count => Pos.Length;
    public float Length => (Pos.Length - 1) * Ds;
    public float EndElevation => Pos[^1].Y;
    public float AvgGrade => Length > 0 ? (Pos[^1].Y - Pos[0].Y) / Length * 100f : 0f;

    public static Vector3 ForwardOf(float heading) => new(MathF.Sin(heading), 0, -MathF.Cos(heading));
    public static Vector3 RightOf(float heading) => new(MathF.Cos(heading), 0, MathF.Sin(heading));

    public Vector3 Right(int i) => RightOf(Heading[i]);

    public void Sample(float s, out Vector3 pos, out float heading, out float grade)
    {
        float f = Math.Clamp(s / Ds, 0, Count - 1.0001f);
        int i = (int)f;
        float t = f - i;
        pos = Pos[i].Lerp(Pos[i + 1], t);
        heading = Mathf.Lerp(Heading[i], Heading[i + 1], t);
        grade = Mathf.Lerp(Grade[i], Grade[i + 1], t);
    }

    public float ElevationAt(float s)
    {
        Sample(s, out var p, out _, out _);
        return p.Y;
    }
}
