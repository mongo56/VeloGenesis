using System;
using System.Collections.Generic;
using Godot;
using VeloGenesis.Core;

namespace VeloGenesis.World;

public class MeshData
{
    public Vector3[] V;
    public Vector3[] N;
    public Vector2[] UV;
    public int[] I;
    /// <summary>Grid size for terrain meshes (rows along the road × columns across it).</summary>
    public int Rows, Cols;

    public ArrayMesh ToMesh()
    {
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = V;
        arr[(int)Mesh.ArrayType.Normal] = N;
        if (UV != null) arr[(int)Mesh.ArrayType.TexUV] = UV;
        arr[(int)Mesh.ArrayType.Index] = I;
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }
}

/// <summary>CPU-side geometry for one segment. Built on a worker thread, uploaded on the main thread.</summary>
public class SegmentGeometry
{
    public MeshData Road, Walls, Terrain, Rails;
    /// <summary>Lamp posts at ground level, arm pointing over the road (local +X).</summary>
    public List<Transform3D> Pylons = new();
    public List<Transform3D> RailPosts = new();
    public List<Transform3D> Gantries = new();
    public List<(Transform3D xf, Color color)> Rocks = new();
    public List<(Transform3D xf, Color color)> Towers = new();
    public List<(Transform3D xf, int km)> Gates = new();
    public List<Transform3D> Gels = new();
    public List<(Transform3D xf, Color color)> Palms = new();
    public List<Transform3D> TunnelRings = new();
    public List<(Transform3D xf, Color color)> Floaters = new();
    public List<(Transform3D xf, Color color)> FloatRings = new();
    public List<(Transform3D xf, Color color)> Buildings = new();
    public List<(Transform3D xf, Color color)> Beams = new();
    /// <summary>Props standing on (or floating above) the terrain, with the ground height under them.</summary>
    public List<(PropList list, int index, float ground)> Grounded = new();

    public List<(Transform3D xf, Color color)> ListOf(PropList l) => l switch
    {
        PropList.Rocks => Rocks,
        PropList.Towers => Towers,
        PropList.Palms => Palms,
        PropList.Floaters => Floaters,
        PropList.FloatRings => FloatRings,
        _ => Beams,
    };
}

public enum PropList { Rocks, Towers, Palms, Floaters, FloatRings, Beams }

/// <summary>A prop instance on the terrain that moves down with it when the terrain is carved.</summary>
public class GroundedProp
{
    public MultiMesh Mesh;
    public int Index;
    public Transform3D Xf;
    public float Ground;
    public bool Hidden;
}

/// <summary>
/// Coarse copy of a road for carving terrain around it: the terrain may not rise above the road
/// by more than a ~29° slope away from it, so no mountain ever hangs over a road. Points of the
/// road that are close along the route (the stretch the terrain was built around) don't count;
/// the same road looping back later does.
/// </summary>
public class RoadField
{
    public const float Flat = 7f, Slope = 0.55f, Sink = 0.4f;
    readonly float[] _x, _z, _y, _s;
    readonly float _minX, _maxX, _minZ, _maxZ;
    public readonly float MinY;
    public readonly Segment Seg;

    public RoadField(Segment seg)
    {
        Seg = seg;
        int n = (seg.Count + 3) / 4;
        _x = new float[n]; _z = new float[n]; _y = new float[n]; _s = new float[n];
        _minX = _minZ = MinY = float.MaxValue;
        _maxX = _maxZ = float.MinValue;
        for (int k = 0; k < n; k++)
        {
            int i = Math.Min(k * 4, seg.Count - 1);
            var p = seg.Pos[i];
            _x[k] = p.X; _z[k] = p.Z; _y[k] = p.Y; _s[k] = i * Segment.Ds;
            _minX = Math.Min(_minX, p.X); _maxX = Math.Max(_maxX, p.X);
            _minZ = Math.Min(_minZ, p.Z); _maxZ = Math.Max(_maxZ, p.Z);
            MinY = Math.Min(MinY, p.Y);
        }
    }

    /// <summary>How distances along the route are measured between a terrain's own road and this one.</summary>
    public readonly struct Relation
    {
        /// <summary>0 = same road, 1 = one follows the other along the route, 2 = branches of a common fork.</summary>
        public readonly int Kind;
        public readonly float OwnerStart, RoadStart, Fork;

        public Relation(Segment owner, Segment road)
        {
            OwnerStart = owner.StartDist;
            RoadStart = road.StartDist;
            Fork = Math.Min(OwnerStart, RoadStart);
            if (owner == road) Kind = 0;
            else Kind = IsAncestor(road, owner) || IsAncestor(owner, road) ? 1 : 2;
        }

        // The lead-in (id -1) runs straight into the start, so it is the ancestor of every segment.
        static bool IsAncestor(Segment a, Segment b)
        {
            if (a.Id < 0) return true;
            for (var p = b.Parent; p != null; p = p.Parent) if (p == a) return true;
            return false;
        }

        public float PathDistance(float sOwner, float sRoad) => Kind switch
        {
            0 => MathF.Abs(sOwner - sRoad),
            1 => MathF.Abs(OwnerStart + sOwner - (RoadStart + sRoad)),
            _ => OwnerStart + sOwner - Fork + RoadStart + sRoad - Fork,
        };
    }

    /// <summary>
    /// Highest the terrain may be at (x, z) because of this road (+inf when it does not constrain
    /// height y). <paramref name="cell"/> is the size of the terrain cell around the point: the
    /// road may pass anywhere inside it, so the slope starts that much closer.
    /// </summary>
    public float Allowed(float x, float z, float y, in Relation rel, float sOwner, float cell)
    {
        if (y <= MinY - Sink) return float.MaxValue;
        // Even the nearest point of the road's bounding box is too far away to constrain y.
        float dx = Math.Max(Math.Max(_minX - x, x - _maxX), 0f), dz = Math.Max(Math.Max(_minZ - z, z - _maxZ), 0f);
        float reach = (y - MinY + Sink) / Slope + Flat + cell;
        if (dx * dx + dz * dz >= reach * reach) return float.MaxValue;
        float best = float.MaxValue;
        for (int k = 0; k < _x.Length; k++)
        {
            float ax = _x[k] - x, az = _z[k] - z;
            float d = MathF.Sqrt(ax * ax + az * az);
            if (rel.PathDistance(sOwner, _s[k]) < d * 1.5f + 60f) continue;
            best = Math.Min(best, _y[k] - Sink + Math.Max(0f, d - Flat - cell) * Slope);
        }
        return best;
    }

    /// <summary>Distance from (x, z) to the nearest point of this road that is far away along the route.</summary>
    public float Clearance(float x, float z, in Relation rel, float sOwner)
    {
        float best = float.MaxValue;
        for (int k = 0; k < _x.Length; k++)
        {
            float ax = _x[k] - x, az = _z[k] - z;
            float d = MathF.Sqrt(ax * ax + az * az);
            if (d < best && rel.PathDistance(sOwner, _s[k]) >= d * 1.5f + 60f) best = d;
        }
        return best;
    }

    /// <summary>Height of, distance to and route position of the nearest point of the road.</summary>
    public (float y, float dist, float s) Nearest(float x, float z)
    {
        float best = float.MaxValue, by = 0, bs = 0;
        for (int k = 0; k < _x.Length; k++)
        {
            float ax = _x[k] - x, az = _z[k] - z, d2 = ax * ax + az * az;
            if (d2 < best) { best = d2; by = _y[k]; bs = _s[k]; }
        }
        return (by, MathF.Sqrt(best), bs);
    }

    /// <summary>
    /// The terrain's own road keeps its shoulders; beyond them the ground may fall away as steeply
    /// as a cliff, so a road passing far below is never buried.
    /// </summary>
    public static float Floor(float ownY, float lat) => ownY - Sink - Math.Max(0f, lat - Flat) * 2.5f;
}

public static class WorldBuilder
{
    public const float RoadHalfWidth = 3.6f;
    public const float RiderLane = 1.5f;
    /// <summary>The mountains roll down from here to the terrain's outer edge, ending below the horizon grid.</summary>
    public const float OuterStart = 380f, OuterEnd = 620f;
    /// <summary>A last column far below the edge, so the rim of the terrain can never be seen against the sky.</summary>
    const float SkirtLat = 640f, SkirtDrop = 320f;

    /// <summary>Height of the generated landscape at a lateral offset from the road.</summary>
    public static float TerrainHeight(float roadY, float latAbs, float wx, float wz, float amp, int seed, float outerEnd = OuterEnd)
    {
        float b = Noise.SmoothStep(5f, 40f, latAbs);
        float m = Noise.SmoothStep(50f, 230f, latAbs);
        float hills = Noise.Fbm2(wx / 110f, wz / 110f, seed, 3) * 9f + 3f;
        float ridges = Noise.Ridged2(wx / 320f, wz / 320f, seed + 7, 4);
        // Smaller crags and gullies on the slopes so the ranges don't read as smooth sheets.
        float crags = Noise.Fbm2(wx / 70f, wz / 70f, seed + 13, 3) * 16f;
        float h = roadY - 0.35f + b * hills * (0.6f + 0.4f * amp) + m * (ridges * 125f + 15f + crags) * amp;
        float outer = Noise.SmoothStep(outerEnd * (OuterStart / OuterEnd), outerEnd, latAbs);
        return h + (roadY - 120f - h) * outer;
    }

    static float AmpAt(Segment seg, float s)
    {
        // Forks sit in open valleys: the mountains ease down to low hills towards both ends, so
        // neighbouring strips (which may be laid out a little differently) meet without a step.
        float w = Noise.SmoothStep(0, 300, s) * Noise.SmoothStep(0, 300, seg.Length - s);
        return Noise.Lerp(0.25f, seg.MountainAmp, w);
    }

    public static SegmentGeometry Build(Segment seg, Segment sibling, Quality q, int worldSeed)
    {
        ComputeReach(seg);
        var g = new SegmentGeometry
        {
            Road = BuildRoad(seg),
            Walls = BuildWalls(seg),
            Terrain = BuildTerrain(seg, sibling, q, worldSeed),
        };
        g.Rails = BuildRails(seg, sibling, g.RailPosts);
        var rng = new Random(seg.Id * 7919 + worldSeed);
        BuildProps(seg, sibling, q, worldSeed, rng, g);
        if (seg.LeadInRise > 0)
        {
            ClearRisingPart(seg, g);
            g.Grounded.Clear();
        }
        return g;
    }

    static MeshData BuildRoad(Segment seg)
    {
        int n = seg.Count;
        var d = new MeshData { V = new Vector3[n * 2], N = new Vector3[n * 2], UV = new Vector2[n * 2], I = new int[(n - 1) * 6] };
        // Branches overlap right at a fork; lift one slightly to avoid z-fighting.
        float lift = 0.02f + (seg.Side > 0 ? 0.015f : 0f);
        for (int i = 0; i < n; i++)
        {
            var r = seg.Right(i);
            var p = seg.Pos[i] + Vector3.Up * lift;
            d.V[i * 2] = p - r * RoadHalfWidth;
            d.V[i * 2 + 1] = p + r * RoadHalfWidth;
            d.N[i * 2] = d.N[i * 2 + 1] = Vector3.Up;
            float s = seg.StartDist + i * Segment.Ds;
            d.UV[i * 2] = new Vector2(0, s);
            d.UV[i * 2 + 1] = new Vector2(1, s);
        }
        Quads(d.I, n, 2);
        return d;
    }

    static MeshData BuildWalls(Segment seg)
    {
        int n = seg.Count;
        const float h = 0.45f;
        var d = new MeshData { V = new Vector3[n * 4], N = new Vector3[n * 4], UV = new Vector2[n * 4], I = new int[(n - 1) * 12] };
        for (int i = 0; i < n; i++)
        {
            var r = seg.Right(i);
            var p = seg.Pos[i];
            float s = seg.StartDist + i * Segment.Ds;
            for (int side = 0; side < 2; side++)
            {
                var basePos = p + r * (side == 0 ? -RoadHalfWidth : RoadHalfWidth);
                int o = side * n * 2 + i * 2;
                d.V[o] = basePos;
                d.V[o + 1] = basePos + Vector3.Up * h;
                d.N[o] = d.N[o + 1] = side == 0 ? r : -r;
                d.UV[o] = new Vector2(s, 0);
                d.UV[o + 1] = new Vector2(s, 1);
            }
        }
        int k = 0;
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = side * n * 2 + i * 2;
                int b = a + 2;
                d.I[k++] = a; d.I[k++] = a + 1; d.I[k++] = b;
                d.I[k++] = b; d.I[k++] = a + 1; d.I[k++] = b + 1;
            }
        return d;
    }

    // Guard-rail beam cross-section (lateral offset outwards from the rail line, height), closed.
    static readonly Vector2[] RailProfile =
    {
        new(0f, 0.6f), new(0f, 0.82f), new(0.025f, 0.86f), new(0.07f, 0.86f), new(0.07f, 0.56f), new(0.025f, 0.56f),
    };
    const float RailOffset = 0.35f;

    /// <summary>
    /// Chrome guard rails along both edges, with posts every 4 m. On the side facing the sibling
    /// branch the rail only starts once the two roads have separated.
    /// </summary>
    static MeshData BuildRails(Segment seg, Segment sibling, List<Transform3D> posts)
    {
        int n = seg.Count;
        int innerSign = -seg.Side;
        var v = new List<Vector3>();
        var nr = new List<Vector3>();
        var idx = new List<int>();
        int pc = RailProfile.Length;

        bool Present(int i, int sign)
        {
            if (i * Segment.Ds < seg.LeadInRise) return false;
            if (sibling == null || sign != innerSign || i >= sibling.Count) return true;
            var a = seg.Pos[i];
            var b = sibling.Pos[i];
            return new Vector2(b.X - a.X, b.Z - a.Z).Length() * 0.5f > RoadHalfWidth + 1.5f;
        }

        foreach (int sign in new[] { -1, 1 })
        {
            for (int f = 0; f < pc; f++)
            {
                var a2 = RailProfile[f];
                var b2 = RailProfile[(f + 1) % pc];
                // Outward normal of this face in profile space (profile is wound clockwise).
                var e = b2 - a2;
                var n2 = new Vector2(-e.Y, e.X).Normalized();
                int start = -1;
                for (int i = 0; i < n; i++)
                {
                    bool here = Present(i, sign);
                    if (!here) { start = -1; continue; }
                    var r = seg.Right(i) * sign;
                    var basePos = seg.Pos[i] + r * (RoadHalfWidth + RailOffset);
                    var nrm = (r * n2.X + Vector3.Up * n2.Y).Normalized();
                    v.Add(basePos + r * a2.X + Vector3.Up * a2.Y);
                    v.Add(basePos + r * b2.X + Vector3.Up * b2.Y);
                    nr.Add(nrm);
                    nr.Add(nrm);
                    int k = v.Count - 2;
                    if (start >= 0)
                    {
                        // Wind each quad so it faces along its normal.
                        var pa = v[k - 2]; var pb = v[k - 1]; var pcv = v[k];
                        bool flip = (pb - pa).Cross(pcv - pa).Dot(nrm) > 0;
                        if (flip) { idx.Add(k - 2); idx.Add(k); idx.Add(k - 1); idx.Add(k - 1); idx.Add(k); idx.Add(k + 1); }
                        else { idx.Add(k - 2); idx.Add(k - 1); idx.Add(k); idx.Add(k - 1); idx.Add(k + 1); idx.Add(k); }
                    }
                    start = i;
                }
            }
            for (int i = 1; i < n; i += 2)
            {
                if (!Present(i, sign)) continue;
                var r = seg.Right(i) * sign;
                var p = seg.Pos[i] + r * (RoadHalfWidth + RailOffset + 0.11f) + Vector3.Up * 0.35f;
                posts.Add(new Transform3D(Basis.FromEuler(new Vector3(0, -seg.Heading[i], 0)) * Basis.FromScale(new Vector3(0.09f, 0.9f, 0.09f)), p));
            }
        }
        return new MeshData { V = v.ToArray(), N = nr.ToArray(), I = idx.ToArray() };
    }

    /// <summary>
    /// How far out from road sample i (towards the sibling branch) the ground is equally far from
    /// both roads: the shared valley line between the branches. Both branches stop their terrain on
    /// this same line, so their edges meet. Also returns the sibling road's height nearest that point.
    /// </summary>
    static float SiblingGap(Segment seg, Segment sibling, int i, int sign, out float sibY)
    {
        var p = seg.Pos[i];
        var r = seg.Right(i) * sign;
        float y = p.Y;
        float DistToSibling(float t)
        {
            var q = p + r * t;
            float best = float.MaxValue;
            for (int k = 0; k < sibling.Count; k += 3)
            {
                var sp = sibling.Pos[k];
                float d2 = new Vector2(sp.X - q.X, sp.Z - q.Z).LengthSquared();
                if (d2 < best) { best = d2; y = sp.Y; }
            }
            return MathF.Sqrt(best);
        }
        float hi = SkirtLat;
        if (DistToSibling(hi) > hi) { sibY = y; return float.MaxValue; }
        float lo = 0;
        for (int it = 0; it < 14; it++)
        {
            float mid = (lo + hi) * 0.5f;
            if (DistToSibling(mid) > mid) lo = mid; else hi = mid;
        }
        DistToSibling(lo);
        sibY = y;
        return lo;
    }

    /// <summary>
    /// How far terrain rows may reach on each side before they would cross their neighbours on
    /// the inside of a bend (most of the tightest bend radius within ±200 m), per road sample.
    /// </summary>
    static void ComputeReach(Segment seg)
    {
        if (seg.ReachRight != null) return;
        int n = seg.Count;
        seg.ReachRight = new float[n];
        seg.ReachLeft = new float[n];
        for (int i = 0; i < n; i++)
        {
            float right = 0, left = 0;
            for (int j = Math.Max(0, i - 100); j <= Math.Min(n - 1, i + 100); j += 2)
            {
                // Positive curvature turns right, so its centre lies on the right.
                float k = seg.Curvature[j];
                right = Math.Max(right, k);
                left = Math.Max(left, -k);
            }
            seg.ReachRight[i] = right > 1e-5f ? 0.8f / right : float.MaxValue;
            seg.ReachLeft[i] = left > 1e-5f ? 0.8f / left : float.MaxValue;
        }
        // Start out laid like the end of the parent's terrain so the strips meet edge to edge
        // (never wider than this road allows), then ease into this road's own reach.
        var parent = seg.Parent;
        if (parent?.ReachRight == null) return;
        for (int i = 0; i < Math.Min(n, 100); i++)
        {
            float t = Noise.SmoothStep(0, 200, i * Segment.Ds);
            seg.ReachRight[i] = Mathf.Lerp(Math.Min(parent.ReachRight[^1], seg.ReachRight[i]), seg.ReachRight[i], t);
            seg.ReachLeft[i] = Mathf.Lerp(Math.Min(parent.ReachLeft[^1], seg.ReachLeft[i]), seg.ReachLeft[i], t);
        }
    }

    /// <summary>
    /// Lateral distance where a point nominally <paramref name="lat"/> metres out actually goes:
    /// unchanged near the road, and beyond that compressed to fit inside a bend's reach.
    /// </summary>
    static float Squeeze(Segment seg, int i, int sign, float lat)
    {
        const float keep = 40f;
        float reach = sign > 0 ? seg.ReachRight[i] : seg.ReachLeft[i];
        if (lat <= keep || reach >= SkirtLat) return lat;
        float maxPos = Math.Max(reach, keep + 30f);
        return keep + (lat - keep) * (maxPos - keep) / (SkirtLat - keep);
    }

    /// <summary>World position <paramref name="lat"/> metres to one side of road sample i, as the terrain lays it out.</summary>
    static Vector3 Lateral(Segment seg, int i, int sign, float lat) => seg.Pos[i] + seg.Right(i) * (sign * Squeeze(seg, i, sign, lat));

    /// <summary>
    /// Ground height for a point placed <paramref name="lat"/> metres out by <see cref="Lateral"/>.
    /// Heights follow the true distance from the road; where a bend squeezes the strip, the
    /// mountains roll off sooner (to where the strip ends) instead of being crushed into cliffs.
    /// </summary>
    static float GroundHeight(Segment seg, int i, int sign, float lat, float wx, float wz, float amp, int seed) =>
        TerrainHeight(seg.Pos[i].Y, Squeeze(seg, i, sign, lat), wx, wz, amp, seed, Squeeze(seg, i, sign, OuterEnd));

    static MeshData BuildTerrain(Segment seg, Segment sibling, Quality q, int seed)
    {
        var cols = new List<float> { -SkirtLat };
        for (int c = q.TerrainColumns.Length - 1; c >= 0; c--) cols.Add(-q.TerrainColumns[c]);
        cols.Add(0);
        foreach (var c in q.TerrainColumns) cols.Add(c);
        cols.Add(SkirtLat);
        int nc = cols.Count;

        var rows = new List<int>();
        for (int i = 0; i < seg.Count; i += q.TerrainRowStep) rows.Add(i);
        if (rows[^1] != seg.Count - 1) rows.Add(seg.Count - 1);
        // The first and last rows are repeated as curtains hanging far below, so the cut ends of
        // the strip (and the seams between strips) never show the inside of a mountain.
        rows.Insert(0, rows[0]);
        rows.Add(rows[^1]);
        int nr = rows.Count;

        var d = new MeshData { V = new Vector3[nr * nc], N = new Vector3[nr * nc], UV = new Vector2[nr * nc], I = new int[(nr - 1) * (nc - 1) * 6], Rows = nr, Cols = nc };
        // The sibling branch lies on this side; the terrain is clipped at the bisector there.
        int innerSign = -seg.Side;

        for (int ri = 0; ri < nr; ri++)
        {
            int i = rows[ri];
            var p = seg.Pos[i];
            var r = seg.Right(i);
            float s = i * Segment.Ds;
            float amp = AmpAt(seg, s);

            float sepHalf = float.MaxValue, sibY = p.Y;
            if (sibling != null && innerSign != 0) sepHalf = SiblingGap(seg, sibling, i, innerSign, out sibY);

            for (int ci = 0; ci < nc; ci++)
            {
                float lat = cols[ci];
                float latAbs = MathF.Abs(lat);
                int sign = lat < 0 ? -1 : 1;
                float baseY = p.Y;
                // Where the column actually goes: squeezed on the inside of bends so rows never cross.
                float posLat = Squeeze(seg, i, sign, latAbs);
                bool inner = innerSign != 0 && sign == innerSign;
                if (inner && posLat > RoadHalfWidth + 0.5f && sepHalf < 1e6f)
                {
                    // Blend towards the shared mid elevation so both branches meet without a seam.
                    float f = posLat / Math.Max(sepHalf, 0.01f);
                    baseY = Noise.Lerp(p.Y, (p.Y + sibY) * 0.5f, Noise.SmoothStep(0.25f, 1f, f));
                    if (posLat > sepHalf)
                    {
                        latAbs *= sepHalf / posLat;
                        posLat = sepHalf;
                    }
                }
                var w = p + r * (sign * posLat);
                bool skirt = (ci == 0 || ci == nc - 1 || ri == 0 || ri == nr - 1);
                float y = skirt ? baseY - 120f - SkirtDrop : TerrainHeight(baseY, posLat, w.X, w.Z, amp, seed, Squeeze(seg, i, sign, OuterEnd));
                // Between two branches the land falls to a shared valley floor on the halfway
                // line, so the terrain from both sides meets there at the same height.
                if (inner && !skirt && sepHalf < 1e6f)
                {
                    // Measured against where this side really ends (a bend may stop it short).
                    float edge = Math.Min(sepHalf, Squeeze(seg, i, sign, SkirtLat));
                    y = Noise.Lerp(y, (p.Y + sibY) * 0.5f - 2f, Noise.SmoothStep(0.55f, 1f, posLat / Math.Max(edge, 0.01f)));
                }
                // Lead-in: the landscape flattens into the horizon grid towards its far end.
                if (seg.LeadInRise > 0)
                    y = baseY - 0.35f + (y - baseY + 0.35f) * Noise.SmoothStep(0, seg.LeadInRise, s);
                d.V[ri * nc + ci] = new Vector3(w.X, y, w.Z);
                d.UV[ri * nc + ci] = new Vector2(posLat, s);
            }
        }

        TerrainNormals(d);
        Quads(d.I, nr, nc);
        return d;
    }

    /// <summary>
    /// Smooth normals from grid neighbours. The outer ring of the grid is the hanging curtain, so
    /// its steep faces are left out of the surface normals and it borrows the nearest inner one.
    /// </summary>
    static void TerrainNormals(MeshData d)
    {
        int nr = d.Rows, nc = d.Cols;
        for (int ri = 0; ri < nr; ri++)
            for (int ci = 0; ci < nc; ci++)
            {
                int r = Math.Clamp(ri, 1, nr - 2), c = Math.Clamp(ci, 1, nc - 2);
                var dx = d.V[r * nc + Math.Min(c + 1, nc - 2)] - d.V[r * nc + Math.Max(c - 1, 1)];
                var dz = d.V[Math.Min(r + 1, nr - 2) * nc + c] - d.V[Math.Max(r - 1, 1) * nc + c];
                var nrm = dx.Cross(dz);
                d.N[ri * nc + ci] = nrm.LengthSquared() > 1e-8f ? nrm.Normalized() : Vector3.Up;
            }
    }

    static readonly (int dr, int dc)[] Neighbours = { (0, 1), (0, -1), (1, 0), (-1, 0) };

    /// <summary>
    /// Lowers a segment's terrain wherever it would rise over a road too steeply (other roads, or
    /// its own looping back). Returns a carved copy, or null when nothing changed. Worker-thread safe.
    /// </summary>
    public static MeshData CarveTerrain(MeshData t, Segment owner, IReadOnlyList<RoadField> roads)
    {
        var rels = new RoadField.Relation[roads.Count];
        for (int j = 0; j < roads.Count; j++) rels[j] = new RoadField.Relation(owner, roads[j].Seg);
        int nr = t.Rows, nc = t.Cols;
        Vector3[] v = null;
        for (int k = 0; k < t.V.Length; k++)
        {
            var p = v?[k] ?? t.V[k];
            // Largest distance to a grid neighbour: a road could cross anywhere in that cell.
            int ri = k / nc, ci = k % nc;
            float cell = 0;
            foreach (var (dr, dc) in Neighbours)
            {
                int r2 = ri + dr, c2 = ci + dc;
                if (r2 < 0 || r2 >= nr || c2 < 0 || c2 >= nc) continue;
                var q = t.V[r2 * nc + c2];
                cell = Math.Max(cell, new Vector2(q.X - p.X, q.Z - p.Z).Length());
            }
            cell *= 1.42f; // across the cell's diagonal
            float sOwner = t.UV[k].Y;
            float allowed = float.MaxValue;
            for (int j = 0; j < roads.Count; j++) allowed = Math.Min(allowed, roads[j].Allowed(p.X, p.Z, p.Y, rels[j], sOwner, cell));
            if (allowed >= p.Y) continue;
            int i = Math.Clamp((int)MathF.Round(sOwner / Segment.Ds), 0, owner.Count - 1);
            float y = Math.Max(allowed, RoadField.Floor(owner.Pos[i].Y, t.UV[k].X));
            if (y >= p.Y - 0.01f) continue;
            v ??= (Vector3[])t.V.Clone();
            v[k].Y = y;
        }
        if (v == null) return null;
        var d = new MeshData { V = v, N = new Vector3[v.Length], UV = t.UV, I = t.I, Rows = t.Rows, Cols = t.Cols };
        TerrainNormals(d);
        return d;
    }

    /// <summary>How far the ground under a prop sinks when carved for these roads.</summary>
    public static float GroundDrop(float x, float z, float ground, RoadField own, IReadOnlyList<RoadField> roads)
    {
        var (ownY, lat, sOwner) = own.Nearest(x, z);
        float allowed = float.MaxValue;
        foreach (var r in roads) allowed = Math.Min(allowed, r.Allowed(x, z, ground, new RoadField.Relation(own.Seg, r.Seg), sOwner, 8f));
        if (allowed >= ground) return 0;
        return Math.Max(0f, ground - Math.Max(allowed, RoadField.Floor(ownY, lat)));
    }

    /// <summary>
    /// Carves freshly built geometry (terrain and the props on it) around other roads and around
    /// its own road where that loops back on itself.
    /// </summary>
    public static void CarveNew(Segment seg, SegmentGeometry g, IReadOnlyList<RoadField> others)
    {
        var own = new RoadField(seg);
        var roads = new List<RoadField>(others) { own };
        var carved = CarveTerrain(g.Terrain, seg, roads);
        if (carved == null) return;
        g.Terrain = carved;
        for (int k = 0; k < g.Grounded.Count; k++)
        {
            var (list, index, ground) = g.Grounded[k];
            var items = g.ListOf(list);
            var (xf, color) = items[index];
            float drop = GroundDrop(xf.Origin.X, xf.Origin.Z, ground, own, roads);
            if (drop <= 0) continue;
            xf.Origin -= new Vector3(0, drop, 0);
            items[index] = (xf, color);
            g.Grounded[k] = (list, index, ground - drop);
        }
    }

    /// <summary>Triangulates a rows × cols vertex grid (clockwise when viewed from above).</summary>
    static void Quads(int[] idx, int rows, int cols)
    {
        int k = 0;
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = r * cols + c, b = a + 1, cc = a + cols, dd = cc + 1;
                idx[k++] = a; idx[k++] = cc; idx[k++] = b;
                idx[k++] = b; idx[k++] = cc; idx[k++] = dd;
            }
    }

    /// <summary>Removes props standing on the part of the lead-in that rises out of the grid.</summary>
    static void ClearRisingPart(Segment seg, SegmentGeometry g)
    {
        float limit = seg.LeadInRise + 40f;
        bool Rising(Vector3 w)
        {
            float best = float.MaxValue;
            int bi = 0;
            for (int i = 0; i < seg.Count; i += 2)
            {
                float d = new Vector2(seg.Pos[i].X - w.X, seg.Pos[i].Z - w.Z).LengthSquared();
                if (d < best) { best = d; bi = i; }
            }
            return bi * Segment.Ds < limit;
        }
        g.Pylons.RemoveAll(x => Rising(x.Origin));
        g.Gantries.RemoveAll(x => Rising(x.Origin));
        g.TunnelRings.RemoveAll(x => Rising(x.Origin));
        g.Gels.Clear();
        foreach (var list in new[] { g.Rocks, g.Towers, g.Palms, g.Floaters, g.FloatRings, g.Buildings, g.Beams })
            list.RemoveAll(c => Rising(c.xf.Origin));
    }

    /// <summary>Lowest terrain height across a footprint of the given radius around a lateral position.</summary>
    static float LowestGround(Segment seg, int i, int sign, float latAbs, float radius, float amp, int seed)
    {
        float lowest = float.MaxValue;
        var fwd = Segment.ForwardOf(seg.Heading[i]);
        var r = seg.Right(i);
        foreach (var (dl, df) in new[] { (0f, 0f), (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            float lat = Math.Max(5f, latAbs + dl * radius);
            var w = Lateral(seg, i, sign, lat) + fwd * df * radius;
            lowest = Math.Min(lowest, GroundHeight(seg, i, sign, lat, w.X, w.Z, amp, seed));
        }
        return lowest;
    }

    static void BuildProps(Segment seg, Segment sibling, Quality q, int seed, Random rng, SegmentGeometry g)
    {
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        int innerSign = -seg.Side;
        var biome = seg.Biome;

        float SepHalfAt(int i) => sibling == null || innerSign == 0 ? float.MaxValue : SiblingGap(seg, sibling, i, innerSign, out _);

        Basis Facing(int i) => Basis.FromEuler(new Vector3(0, -seg.Heading[i], 0));

        // Street lamps along both road edges (local +X reaches over the road).
        for (float s = 10; s < seg.Length; s += 40f)
        {
            int i = (int)(s / Segment.Ds);
            var r = seg.Right(i);
            foreach (int side in new[] { -1, 1 })
            {
                var basis = side < 0 ? Facing(i) : Facing(i) * new Basis(Vector3.Up, MathF.PI);
                g.Pylons.Add(new Transform3D(basis, seg.Pos[i] + r * side * (RoadHalfWidth + 0.75f)));
            }
        }

        // Occasional truss gantries spanning the road, clear of the fork at either end.
        for (float s = R(220f, 520f); s < seg.Length - 150f; s += R(450f, 900f))
        {
            int i = (int)(s / Segment.Ds);
            if (SepHalfAt(i) < RoadHalfWidth + PropMeshes.GantryHalfSpan + 2f) continue;
            g.Gantries.Add(new Transform3D(Facing(i), seg.Pos[i]));
        }

        // Kilometre gates.
        int firstKm = (int)MathF.Floor(seg.StartDist / 1000f) + 1;
        for (int km = firstKm; km * 1000f < seg.StartDist + seg.Length; km++)
        {
            float s = km * 1000f - seg.StartDist;
            if (s < 30 || s > seg.Length - 30) continue;
            int i = (int)(s / Segment.Ds);
            g.Gates.Add((new Transform3D(Facing(i), seg.Pos[i]), km));
        }

        // Rock formations: half-buried faceted boulders, often in small clusters, wearing the same
        // neon grid as the terrain so they read as part of the landscape.
        int rockClusters = (int)(seg.Length / 70f * q.SceneryDensity * biome.Rocks);
        for (int c = 0; c < rockClusters; c++)
        {
            float s0 = R(0, seg.Length);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float lat0 = 16f + MathF.Pow((float)rng.NextDouble(), 1.3f) * 190f;
            int rocks = 1 + rng.Next(4);
            float clusterSize = Noise.Lerp(2f, 9f, MathF.Pow((float)rng.NextDouble(), 1.8f)) * (1f + lat0 / 150f);
            for (int k = 0; k < rocks; k++)
            {
                float s = Math.Clamp(s0 + R(-12f, 12f) * (1f + clusterSize * 0.2f), 0, seg.Length);
                int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
                float latAbs = Math.Max(12f, lat0 + R(-10f, 10f));
                if (sign == innerSign && latAbs > SepHalfAt(i) - 14f) continue;
                var w = Lateral(seg, i, sign, latAbs);
                float size = clusterSize * (k == 0 ? 1f : R(0.35f, 0.75f));
                float y = LowestGround(seg, i, sign, latAbs, size, AmpAt(seg, s), seed);
                var basis = Basis.FromEuler(new Vector3(R(-0.2f, 0.2f), R(0, MathF.Tau), R(-0.2f, 0.2f)))
                    * Basis.FromScale(new Vector3(size * R(0.9f, 1.5f), size * R(0.45f, 0.9f), size * R(0.8f, 1.3f)));
                g.Rocks.Add((new Transform3D(basis, new Vector3(w.X, y - size * 0.15f, w.Z)), seg.Primary));
                g.Grounded.Add((PropList.Rocks, g.Rocks.Count - 1, y));
            }
        }

        // A few tall light beams per sector, always rooted below the ground.
        int beams = rng.NextDouble() < 0.6 * q.SceneryDensity * biome.Beams ? 1 + (rng.NextDouble() < 0.35 * biome.Beams ? 1 + (biome.Beams > 1.5f ? 2 : 0) : 0) : 0;
        for (int b = 0; b < beams; b++)
        {
            float s = R(seg.Length * 0.15f, seg.Length * 0.85f);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            if (sign == innerSign) sign = -sign;
            float latAbs = R(70f, 200f);
            var w = Lateral(seg, i, sign, latAbs);
            float ground = GroundHeight(seg, i, sign, latAbs, w.X, w.Z, AmpAt(seg, s), seed) - 6f;
            float beamH = R(220f, 360f);
            float width = R(1.6f, 2.6f);
            g.Beams.Add((new Transform3D(Basis.FromScale(new Vector3(width, beamH, width)), new Vector3(w.X, ground + beamH * 0.5f, w.Z)), rng.Next(2) == 0 ? seg.Primary : seg.Secondary));
            g.Grounded.Add((PropList.Beams, g.Beams.Count - 1, ground + 6f));
        }

        // Synthwave palms lining the flatter sectors.
        float palmRate = seg.Kind switch { SectorKind.Flat => 1f, SectorKind.Start => 0.8f, SectorKind.Rolling => 0.6f, SectorKind.Descent => 0.5f, _ => 0.2f };
        int palmCount = (int)(seg.Length / 45f * palmRate * q.SceneryDensity * biome.Palms);
        for (int c = 0; c < palmCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(9f, 55f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 10f) continue;
            var w = Lateral(seg, i, sign, latAbs);
            float y = GroundHeight(seg, i, sign, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
            float size = R(7f, 13f);
            var basis = Basis.FromEuler(new Vector3(R(-0.06f, 0.06f), R(0, MathF.Tau), R(-0.06f, 0.06f))) * Basis.FromScale(Vector3.One * size);
            g.Palms.Add((new Transform3D(basis, new Vector3(w.X, y - 0.3f, w.Z)), rng.Next(3) == 0 ? seg.Primary : seg.Secondary));
            g.Grounded.Add((PropList.Palms, g.Palms.Count - 1, y));
        }

        // Tunnels of light rings over the road.
        int tunnels = rng.NextDouble() < (seg.Kind == SectorKind.Climb ? 0.75 : 0.45) ? 1 + (rng.NextDouble() < 0.3 ? 1 : 0) : 0;
        for (int t = 0; t < tunnels; t++)
        {
            float start = R(100f, Math.Max(110f, seg.Length - 400f));
            float len = R(120f, 300f);
            for (float s = start; s < Math.Min(start + len, seg.Length - 30f); s += 16f)
            {
                int i = (int)(s / Segment.Ds);
                g.TunnelRings.Add(new Transform3D(Facing(i) * Basis.FromEuler(new Vector3(-MathF.PI / 2, 0, 0)), seg.Pos[i] + Vector3.Up * 0.3f));
            }
        }

        // Floating geometry above the landscape.
        int floatCount = (int)(seg.Length / 320f * q.SceneryDensity * biome.Floaters);
        for (int c = 0; c < floatCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(35f, 190f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 25f) continue;
            var w = Lateral(seg, i, sign, latAbs);
            float ground = GroundHeight(seg, i, sign, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
            float y = ground + R(25f, 75f);
            var color = rng.Next(2) == 0 ? seg.Primary : seg.Secondary;
            if (rng.NextDouble() < 0.3)
            {
                g.Floaters.Add((new Transform3D(Basis.FromScale(new Vector3(1, 1.4f, 1) * R(5f, 12f)), new Vector3(w.X, y, w.Z)), color));
                g.Grounded.Add((PropList.Floaters, g.Floaters.Count - 1, ground));
            }
            else
            {
                g.FloatRings.Add((new Transform3D(Basis.FromEuler(new Vector3(R(-1.2f, 1.2f), R(0, MathF.Tau), R(-0.4f, 0.4f))) * Basis.FromScale(Vector3.One * R(8f, 16f)), new Vector3(w.X, y, w.Z)), color));
                g.Grounded.Add((PropList.FloatRings, g.FloatRings.Count - 1, ground));
            }
        }

        // Distant neon megacity on the horizon in some sectors.
        if (seg.Kind is SectorKind.Flat or SectorKind.Descent or SectorKind.Rolling or SectorKind.Start && rng.NextDouble() < biome.CityChance)
        {
            int sideSign = rng.Next(2) == 0 ? -1 : 1;
            if (sideSign == innerSign) sideSign = -sideSign;
            float centre = R(seg.Length * 0.3f, seg.Length * 0.8f);
            int blocks = (int)(R(12, 28) * Math.Max(0.4f, q.SceneryDensity) * biome.CityBlocks);
            for (int c = 0; c < blocks; c++)
            {
                float s = Math.Clamp(centre + R(-500f, 500f), 0, seg.Length);
                int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
                float latAbs = R(480f, 950f);
                var w = seg.Pos[i] + seg.Right(i) * sideSign * latAbs;
                // The road curves, so check the whole segment: never put a skyscraper near the route.
                bool clear = true;
                for (int k = 0; k < seg.Count && clear; k += 20)
                    clear = new Vector2(seg.Pos[k].X - w.X, seg.Pos[k].Z - w.Z).LengthSquared() > 420f * 420f;
                if (!clear) continue;
                float hgt = R(70f, 240f) * (1f - Math.Abs(s - centre) / 900f);
                float baseY = seg.Pos[i].Y - 90f;
                var basis = Basis.FromEuler(new Vector3(0, -seg.Heading[i] + R(-0.2f, 0.2f), 0)) * Basis.FromScale(new Vector3(R(18f, 45f), hgt, R(18f, 45f)));
                g.Buildings.Add((new Transform3D(basis, new Vector3(w.X, baseY + hgt * 0.5f, w.Z)), rng.Next(3) == 0 ? seg.Primary : seg.Secondary));
            }
        }

        // Balanced-rock stacks (hoodoos): 2–4 boulders piled up, shrinking towards the top.
        int stackCount = (int)(seg.Length / 140f * q.SceneryDensity * biome.Stacks);
        for (int c = 0; c < stackCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(45f, 200f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 20f) continue;
            var w = Lateral(seg, i, sign, latAbs);
            float y = GroundHeight(seg, i, sign, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
            float size = R(5f, 11f);
            var basePos = new Vector3(w.X, y - size * 0.25f, w.Z);
            int pieces = 2 + rng.Next(3);
            float top = basePos.Y;
            for (int k = 0; k < pieces; k++)
            {
                float sy = size * R(0.55f, 0.85f);
                var scale = new Vector3(size * R(0.9f, 1.25f), sy, size * R(0.85f, 1.2f));
                float cy = k == 0 ? basePos.Y : top + sy * 0.3f;
                var p = new Vector3(basePos.X + R(-0.15f, 0.15f) * size, cy, basePos.Z + R(-0.15f, 0.15f) * size);
                var basis = Basis.FromEuler(new Vector3(R(-0.15f, 0.15f), R(0, MathF.Tau), R(-0.15f, 0.15f))) * Basis.FromScale(scale);
                g.Towers.Add((new Transform3D(basis, p), seg.Secondary));
                g.Grounded.Add((PropList.Towers, g.Towers.Count - 1, y));
                top = cy + sy * 1.0f;
                size *= R(0.6f, 0.8f);
            }
        }

        // Power gels floating over the riding lane.
        foreach (float s in seg.Gels)
        {
            seg.Sample(s, out var p, out float h, out _);
            g.Gels.Add(new Transform3D(Basis.Identity, p + Segment.RightOf(h) * RiderLane + Vector3.Up * 2.6f));
        }
    }
}
