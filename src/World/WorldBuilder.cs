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
    public MeshData Road, Walls, Terrain;
    public List<Transform3D> Pylons = new();
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
}

public static class WorldBuilder
{
    public const float RoadHalfWidth = 3.6f;
    public const float RiderLane = 1.5f;

    /// <summary>Height of the generated landscape at a lateral offset from the road.</summary>
    public static float TerrainHeight(float roadY, float latAbs, float wx, float wz, float amp, int seed)
    {
        float b = Noise.SmoothStep(5f, 40f, latAbs);
        float m = Noise.SmoothStep(50f, 230f, latAbs);
        float hills = Noise.Fbm2(wx / 110f, wz / 110f, seed, 3) * 9f + 3f;
        float ridges = Noise.Ridged2(wx / 320f, wz / 320f, seed + 7, 4);
        return roadY - 0.35f + b * hills * (0.6f + 0.4f * amp) + m * (ridges * 125f + 15f) * amp;
    }

    static float AmpAt(Segment seg, float s)
    {
        // Mountain height blends to 1 at both ends so neighbouring segments meet seamlessly.
        float w = Noise.SmoothStep(0, 250, s) * Noise.SmoothStep(0, 250, seg.Length - s);
        return Noise.Lerp(1f, seg.MountainAmp, w);
    }

    public static SegmentGeometry Build(Segment seg, Segment sibling, Quality q, int worldSeed)
    {
        var g = new SegmentGeometry
        {
            Road = BuildRoad(seg),
            Walls = BuildWalls(seg),
            Terrain = BuildTerrain(seg, sibling, q, worldSeed),
        };
        var rng = new Random(seg.Id * 7919 + worldSeed);
        BuildProps(seg, sibling, q, worldSeed, rng, g);
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

    static MeshData BuildTerrain(Segment seg, Segment sibling, Quality q, int seed)
    {
        var cols = new List<float>();
        for (int c = q.TerrainColumns.Length - 1; c >= 0; c--) cols.Add(-q.TerrainColumns[c]);
        cols.Add(0);
        foreach (var c in q.TerrainColumns) cols.Add(c);
        int nc = cols.Count;

        var rows = new List<int>();
        for (int i = 0; i < seg.Count; i += q.TerrainRowStep) rows.Add(i);
        if (rows[^1] != seg.Count - 1) rows.Add(seg.Count - 1);
        int nr = rows.Count;

        var d = new MeshData { V = new Vector3[nr * nc], N = new Vector3[nr * nc], UV = new Vector2[nr * nc], I = new int[(nr - 1) * (nc - 1) * 6] };
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
            if (sibling != null && innerSign != 0 && i < sibling.Count)
            {
                var sp = sibling.Pos[i];
                sepHalf = new Vector2(sp.X - p.X, sp.Z - p.Z).Length() * 0.5f;
                sibY = sp.Y;
            }

            for (int ci = 0; ci < nc; ci++)
            {
                float lat = cols[ci];
                float latAbs = MathF.Abs(lat);
                float baseY = p.Y;
                bool inner = innerSign != 0 && MathF.Sign(lat) == innerSign;
                if (inner && latAbs > RoadHalfWidth + 0.5f && sepHalf < 1e6f)
                {
                    // Blend towards the shared mid elevation so both branches meet without a seam.
                    float f = latAbs / Math.Max(sepHalf, 0.01f);
                    baseY = Noise.Lerp(p.Y, (p.Y + sibY) * 0.5f, Noise.SmoothStep(0.25f, 1f, f));
                    latAbs = Math.Min(latAbs, sepHalf);
                    lat = latAbs * innerSign;
                }
                var w = p + r * lat;
                float y = TerrainHeight(baseY, latAbs, w.X, w.Z, amp, seed);
                d.V[ri * nc + ci] = new Vector3(w.X, y, w.Z);
                d.UV[ri * nc + ci] = new Vector2(latAbs, s);
            }
        }

        // Smooth normals from grid neighbours.
        for (int ri = 0; ri < nr; ri++)
            for (int ci = 0; ci < nc; ci++)
            {
                var dx = d.V[ri * nc + Math.Min(ci + 1, nc - 1)] - d.V[ri * nc + Math.Max(ci - 1, 0)];
                var dz = d.V[Math.Min(ri + 1, nr - 1) * nc + ci] - d.V[Math.Max(ri - 1, 0) * nc + ci];
                var nrm = dx.Cross(dz);
                d.N[ri * nc + ci] = nrm.LengthSquared() > 1e-8f ? nrm.Normalized() : Vector3.Up;
            }

        Quads(d.I, nr, nc);
        return d;
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

    static void BuildProps(Segment seg, Segment sibling, Quality q, int seed, Random rng, SegmentGeometry g)
    {
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        int innerSign = -seg.Side;

        float SepHalfAt(int i)
        {
            if (sibling == null || innerSign == 0 || i >= sibling.Count) return float.MaxValue;
            var a = seg.Pos[i];
            var b = sibling.Pos[i];
            return new Vector2(b.X - a.X, b.Z - a.Z).Length() * 0.5f;
        }

        Basis Facing(int i) => Basis.FromEuler(new Vector3(0, -seg.Heading[i], 0));

        // Light pylons along both road edges.
        for (float s = 10; s < seg.Length; s += 40f)
        {
            int i = (int)(s / Segment.Ds);
            var r = seg.Right(i);
            foreach (int side in new[] { -1, 1 })
                g.Pylons.Add(new Transform3D(Facing(i), seg.Pos[i] + r * side * (RoadHalfWidth + 0.7f) + Vector3.Up * 1.1f));
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
        int rockClusters = (int)(seg.Length / 70f * q.SceneryDensity);
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
                var w = seg.Pos[i] + seg.Right(i) * sign * latAbs;
                float y = TerrainHeight(seg.Pos[i].Y, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
                float size = clusterSize * (k == 0 ? 1f : R(0.35f, 0.75f));
                var basis = Basis.FromEuler(new Vector3(R(-0.2f, 0.2f), R(0, MathF.Tau), R(-0.2f, 0.2f)))
                    * Basis.FromScale(new Vector3(size * R(0.9f, 1.5f), size * R(0.45f, 0.9f), size * R(0.8f, 1.3f)));
                g.Rocks.Add((new Transform3D(basis, new Vector3(w.X, y - size * 0.2f, w.Z)), seg.Primary));
            }
        }

        // A few tall light beams per sector, always rooted below the ground.
        int beams = rng.NextDouble() < 0.6 * q.SceneryDensity ? 1 + (rng.NextDouble() < 0.35 ? 1 : 0) : 0;
        for (int b = 0; b < beams; b++)
        {
            float s = R(seg.Length * 0.15f, seg.Length * 0.85f);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            if (sign == innerSign) sign = -sign;
            float latAbs = R(70f, 200f);
            var w = seg.Pos[i] + seg.Right(i) * sign * latAbs;
            float ground = TerrainHeight(seg.Pos[i].Y, latAbs, w.X, w.Z, AmpAt(seg, s), seed) - 6f;
            float beamH = R(220f, 360f);
            float width = R(1.6f, 2.6f);
            g.Beams.Add((new Transform3D(Basis.FromScale(new Vector3(width, beamH, width)), new Vector3(w.X, ground + beamH * 0.5f, w.Z)), rng.Next(2) == 0 ? seg.Primary : seg.Secondary));
        }

        // Synthwave palms lining the flatter sectors.
        float palmRate = seg.Kind switch { SectorKind.Flat => 1f, SectorKind.Start => 0.8f, SectorKind.Rolling => 0.6f, SectorKind.Descent => 0.5f, _ => 0.2f };
        int palmCount = (int)(seg.Length / 45f * palmRate * q.SceneryDensity);
        for (int c = 0; c < palmCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(9f, 55f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 10f) continue;
            var w = seg.Pos[i] + seg.Right(i) * sign * latAbs;
            float y = TerrainHeight(seg.Pos[i].Y, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
            float size = R(7f, 13f);
            var basis = Basis.FromEuler(new Vector3(R(-0.06f, 0.06f), R(0, MathF.Tau), R(-0.06f, 0.06f))) * Basis.FromScale(Vector3.One * size);
            g.Palms.Add((new Transform3D(basis, new Vector3(w.X, y - 0.3f, w.Z)), rng.Next(3) == 0 ? seg.Primary : seg.Secondary));
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
        int floatCount = (int)(seg.Length / 320f * q.SceneryDensity);
        for (int c = 0; c < floatCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(35f, 190f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 25f) continue;
            var w = seg.Pos[i] + seg.Right(i) * sign * latAbs;
            float y = TerrainHeight(seg.Pos[i].Y, latAbs, w.X, w.Z, AmpAt(seg, s), seed) + R(25f, 75f);
            var color = rng.Next(2) == 0 ? seg.Primary : seg.Secondary;
            if (rng.NextDouble() < 0.3)
                g.Floaters.Add((new Transform3D(Basis.FromScale(new Vector3(1, 1.4f, 1) * R(5f, 12f)), new Vector3(w.X, y, w.Z)), color));
            else
                g.FloatRings.Add((new Transform3D(Basis.FromEuler(new Vector3(R(-1.2f, 1.2f), R(0, MathF.Tau), R(-0.4f, 0.4f))) * Basis.FromScale(Vector3.One * R(8f, 16f)), new Vector3(w.X, y, w.Z)), color));
        }

        // Distant neon megacity on the horizon in some sectors.
        if (seg.Kind is SectorKind.Flat or SectorKind.Descent or SectorKind.Rolling or SectorKind.Start && rng.NextDouble() < 0.55)
        {
            int sideSign = rng.Next(2) == 0 ? -1 : 1;
            if (sideSign == innerSign) sideSign = -sideSign;
            float centre = R(seg.Length * 0.3f, seg.Length * 0.8f);
            int blocks = (int)(R(12, 28) * Math.Max(0.4f, q.SceneryDensity));
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
        int stackCount = (int)(seg.Length / 140f * q.SceneryDensity);
        for (int c = 0; c < stackCount; c++)
        {
            float s = R(0, seg.Length);
            int i = Math.Clamp((int)(s / Segment.Ds), 0, seg.Count - 1);
            int sign = rng.Next(2) == 0 ? -1 : 1;
            float latAbs = R(45f, 200f);
            if (sign == innerSign && latAbs > SepHalfAt(i) - 20f) continue;
            var w = seg.Pos[i] + seg.Right(i) * sign * latAbs;
            float y = TerrainHeight(seg.Pos[i].Y, latAbs, w.X, w.Z, AmpAt(seg, s), seed);
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
