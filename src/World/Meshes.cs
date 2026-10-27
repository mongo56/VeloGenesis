using Godot;

namespace VeloGenesis.World;

/// <summary>Small procedural meshes shared by all segments.</summary>
public static class Meshes
{
    static ArrayMesh _octa, _crystal;

    /// <summary>Flat-shaded octahedron, unit height centred on the origin.</summary>
    public static ArrayMesh Octahedron => _octa ??= BuildBipyramid(4, 0.5f, 0f, 0.5f, -0.5f);

    /// <summary>Six-sided crystal: foot at y = 0, tip at y = 1.</summary>
    public static ArrayMesh Crystal => _crystal ??= BuildBipyramid(6, 0.5f, 0.18f, 1f, 0f);

    static ArrayMesh _rock;

    /// <summary>Faceted, irregular boulder (displaced icosphere, flat shaded), roughly unit radius.</summary>
    public static ArrayMesh Rock => _rock ??= BuildRock();

    static ArrayMesh BuildRock()
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var verts = new System.Collections.Generic.List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var faces = new System.Collections.Generic.List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].Normalized();

        // One subdivision for a less regular silhouette.
        var cache = new System.Collections.Generic.Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int m)) return m;
            verts.Add(((verts[a] + verts[b]) * 0.5f).Normalized());
            return cache[key] = verts.Count - 1;
        }
        var sub = new System.Collections.Generic.List<(int, int, int)>();
        foreach (var (a, b, c) in faces)
        {
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            sub.Add((a, ab, ca)); sub.Add((b, bc, ab)); sub.Add((c, ca, bc)); sub.Add((ab, bc, ca));
        }

        // Lumpy displacement; flattened base so rocks sit in the ground.
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            float r = 1f + 0.22f * Mathf.Sin(v.X * 5.1f + 1.3f) * Mathf.Sin(v.Y * 4.3f + 0.7f) * Mathf.Sin(v.Z * 6.2f + 2.1f)
                        + 0.12f * Mathf.Sin(v.X * 11.7f - v.Z * 9.3f);
            v *= r;
            v.Y = Mathf.Max(v.Y, -0.35f);
            verts[i] = v;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (a, b, c) in sub)
        {
            var pa = verts[a]; var pb = verts[b]; var pc = verts[c];
            var n = (pb - pa).Cross(pc - pa).Normalized();
            if (n.Dot(pa + pb + pc) < 0) n = -n;
            TriN(st, pa, pb, pc, n);
        }
        return st.Commit();
    }

    // ------------------------------------------------------------------ organic shapes

    /// <summary>
    /// Smooth surface of revolution along +Y with an elliptical cross-section: half-width rx(y) along
    /// X and half-depth rz(y) along Z. Radii should reach 0 at y0 / y1 for closed ends.
    /// </summary>
    public static ArrayMesh Lathe(System.Func<float, float> rx, System.Func<float, float> rz, float y0, float y1, int rings = 24, int segs = 18)
    {
        int cols = segs + 1;
        var v = new Vector3[(rings + 1) * cols];
        var n = new Vector3[v.Length];
        var uv = new Vector2[v.Length];
        for (int i = 0; i <= rings; i++)
        {
            float t = (float)i / rings;
            // Denser rings near the ends so the rounded caps stay smooth.
            float ts = 0.5f - 0.5f * Mathf.Cos(t * Mathf.Pi);
            float y = Mathf.Lerp(y0, y1, ts);
            float a = rx(y), b = rz(y);
            for (int j = 0; j <= segs; j++)
            {
                float th = Mathf.Tau * j / segs;
                v[i * cols + j] = new Vector3(Mathf.Cos(th) * a, y, Mathf.Sin(th) * b);
                uv[i * cols + j] = new Vector2((float)j / segs, t);
            }
        }
        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= segs; j++)
            {
                int jm = (j + segs - 1) % segs, jp = (j + 1) % segs;
                var dTh = v[i * cols + jp] - v[i * cols + jm];
                var dY = v[Mathf.Min(i + 1, rings) * cols + j] - v[Mathf.Max(i - 1, 0) * cols + j];
                var nn = dTh.Cross(dY);
                var p = v[i * cols + j];
                if (nn.Dot(new Vector3(p.X, 0, p.Z)) < 0) nn = -nn;
                if (nn.LengthSquared() < 1e-12f || new Vector2(p.X, p.Z).Length() < 1e-4f) nn = i == 0 ? Vector3.Down : Vector3.Up;
                n[i * cols + j] = nn.Normalized();
            }

        // Pick the winding that faces outward (Godot front faces are clockwise).
        var a0 = v[(rings / 2) * cols]; var b0 = v[(rings / 2) * cols + 1]; var c0 = v[(rings / 2 + 1) * cols];
        bool flip = (b0 - a0).Cross(c0 - a0).Dot(n[(rings / 2) * cols]) > 0;
        var idx = new int[rings * segs * 6];
        int k = 0;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int q = i * cols + j, r = q + 1, s = q + cols, u = s + 1;
                if (flip) { idx[k++] = q; idx[k++] = s; idx[k++] = r; idx[k++] = r; idx[k++] = s; idx[k++] = u; }
                else { idx[k++] = q; idx[k++] = r; idx[k++] = s; idx[k++] = r; idx[k++] = u; idx[k++] = s; }
            }
        return new MeshData { V = v, N = n, UV = uv, I = idx }.ToMesh();
    }

    /// <summary>Piecewise-smooth profile through (y, r) control points; zero outside the range.</summary>
    public static System.Func<float, float> Profile(params float[] yr)
    {
        return y =>
        {
            if (y <= yr[0] || y >= yr[^2]) return 0f;
            for (int i = 0; i < yr.Length - 2; i += 2)
            {
                float ya = yr[i], yb = yr[i + 2];
                if (y > yb) continue;
                float t = (y - ya) / (yb - ya);
                float ra = yr[i + 1], rb = yr[i + 3];
                // Ends behave like round caps; the middle uses smoothstep.
                if (i == 0) return rb * Mathf.Sqrt(Mathf.Max(0, 1 - (1 - t) * (1 - t)));
                if (i + 2 == yr.Length - 2) return ra * Mathf.Sqrt(Mathf.Max(0, 1 - t * t));
                return Mathf.Lerp(ra, rb, t * t * (3 - 2 * t));
            }
            return 0f;
        };
    }

    /// <summary>Tapered limb from y = 0 to len with round ends and an optional muscle bulge.</summary>
    public static ArrayMesh Limb(float len, float rStart, float rEnd, float bulgeAt = 0.35f, float bulge = 0f, float depth = 1f)
    {
        float R(float y)
        {
            if (y < 0) return Mathf.Sqrt(Mathf.Max(0, rStart * rStart - y * y));
            if (y > len) return Mathf.Sqrt(Mathf.Max(0, rEnd * rEnd - (y - len) * (y - len)));
            float t = y / len;
            float d = (t - bulgeAt) / 0.24f;
            return Mathf.Lerp(rStart, rEnd, t) + bulge * Mathf.Exp(-d * d);
        }
        return Lathe(R, y => R(y) * depth, -rStart, len + rEnd, 22, 16);
    }

    /// <summary>Synthwave palm: curved segmented trunk and drooping fronds. Base at origin, ~1 unit tall.</summary>
    public static ArrayMesh Palm(Material trunkMat, Material frondMat)
    {
        var mesh = new ArrayMesh();

        // Trunk: rings along a gentle curve.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int rings = 14, segs = 8;
        Vector3 Spine(float t) => new(0.12f * t * t, t, 0);
        for (int i = 0; i < rings; i++)
        {
            float t0 = (float)i / rings, t1 = (float)(i + 1) / rings;
            float r0 = Mathf.Lerp(0.035f, 0.018f, t0), r1 = Mathf.Lerp(0.035f, 0.018f, t1);
            // Slight bulge at each segment joint gives the classic ringed trunk.
            float b0 = r0 * 1.15f, b1 = r1;
            for (int j = 0; j < segs; j++)
            {
                float a0 = Mathf.Tau * j / segs, a1 = Mathf.Tau * (j + 1) / segs;
                Vector3 P(float t, float r, float a) => Spine(t) + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                var n0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var n1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var p00 = P(t0, b0, a0); var p01 = P(t0, b0, a1); var p10 = P(t1, b1, a0); var p11 = P(t1, b1, a1);
                Vtx(st, p00, n0, new Vector2(0, t0)); Vtx(st, p10, n0, new Vector2(0, t1)); Vtx(st, p01, n1, new Vector2(1, t0));
                Vtx(st, p01, n1, new Vector2(1, t0)); Vtx(st, p10, n0, new Vector2(0, t1)); Vtx(st, p11, n1, new Vector2(1, t1));
            }
        }
        st.SetMaterial(trunkMat);
        st.Commit(mesh);

        // Fronds: curved, tapered strips radiating from the crown (double-sided material).
        st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var crown = Spine(1f);
        const int fronds = 8, steps = 8;
        for (int f = 0; f < fronds; f++)
        {
            float ang = Mathf.Tau * f / fronds + (f % 2) * 0.2f;
            var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            var side = new Vector3(-dir.Z, 0, dir.X);
            float length = 0.45f + 0.08f * (f % 3);
            for (int s = 0; s < steps; s++)
            {
                float u0 = (float)s / steps, u1 = (float)(s + 1) / steps;
                Vector3 C(float u) => crown + dir * (u * length) + Vector3.Up * (0.12f * Mathf.Sin(u * Mathf.Pi * 0.8f) - 0.3f * u * u);
                float w0 = 0.07f * Mathf.Sin(Mathf.Pi * Mathf.Min(u0 + 0.08f, 1f)), w1 = 0.07f * Mathf.Sin(Mathf.Pi * Mathf.Min(u1 + 0.08f, 1f));
                var c0 = C(u0); var c1 = C(u1);
                var l0 = c0 - side * w0 - Vector3.Up * w0 * 0.3f; var r0 = c0 + side * w0 - Vector3.Up * w0 * 0.3f;
                var l1 = c1 - side * w1 - Vector3.Up * w1 * 0.3f; var r1 = c1 + side * w1 - Vector3.Up * w1 * 0.3f;
                var nrm = (c1 - c0).Cross(side).Normalized();
                if (nrm.Y < 0) nrm = -nrm;
                Vtx(st, l0, nrm, new Vector2(0, u0)); Vtx(st, l1, nrm, new Vector2(0, u1)); Vtx(st, c0, nrm, new Vector2(0.5f, u0));
                Vtx(st, c0, nrm, new Vector2(0.5f, u0)); Vtx(st, l1, nrm, new Vector2(0, u1)); Vtx(st, c1, nrm, new Vector2(0.5f, u1));
                Vtx(st, c0, nrm, new Vector2(0.5f, u0)); Vtx(st, c1, nrm, new Vector2(0.5f, u1)); Vtx(st, r0, nrm, new Vector2(1, u0));
                Vtx(st, r0, nrm, new Vector2(1, u0)); Vtx(st, c1, nrm, new Vector2(0.5f, u1)); Vtx(st, r1, nrm, new Vector2(1, u1));
            }
        }
        st.SetMaterial(frondMat);
        st.Commit(mesh);
        return mesh;
    }

    static void Vtx(SurfaceTool st, Vector3 p, Vector3 n, Vector2 uv)
    {
        st.SetNormal(n);
        st.SetUV(uv);
        st.AddVertex(p);
    }

    static ArrayMesh BuildBipyramid(int sides, float radius, float ringY, float apexY, float footY)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var apex = new Vector3(0, apexY, 0);
        var foot = new Vector3(0, footY, 0);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            var p0 = new Vector3(Mathf.Cos(a0) * radius, ringY, Mathf.Sin(a0) * radius);
            var p1 = new Vector3(Mathf.Cos(a1) * radius, ringY, Mathf.Sin(a1) * radius);
            Tri(st, apex, p1, p0);
            Tri(st, foot, p0, p1);
        }
        return st.Commit();
    }

    /// <summary>a, b, c counter-clockwise seen from outside; emitted clockwise as Godot expects.</summary>
    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = (b - a).Cross(c - a).Normalized();
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(c);
        st.SetNormal(n); st.AddVertex(b);
    }

    /// <summary>
    /// Deep-section wheel rim: a flat ring in the YZ plane (axle along X) with both side walls and
    /// the inner wall, <paramref name="width"/> wide across X.
    /// </summary>
    public static ArrayMesh Rim(float innerR, float outerR, float width, int segments = 64)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float hx = width * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments, a1 = Mathf.Tau * (i + 1) / segments;
            Vector3 P(float r, float a, float x) => new(x, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            foreach (float x in new[] { -hx, hx })
            {
                // Side wall
                var n = new Vector3(Mathf.Sign(x), 0, 0);
                if (x > 0) Quad(st, P(innerR, a0, x), P(outerR, a0, x), P(outerR, a1, x), P(innerR, a1, x), n);
                else Quad(st, P(innerR, a1, x), P(outerR, a1, x), P(outerR, a0, x), P(innerR, a0, x), n);
            }
            // Inner wall (spoke bed), facing the hub
            var nIn = -new Vector3(0, Mathf.Sin((a0 + a1) / 2), Mathf.Cos((a0 + a1) / 2));
            Quad(st, P(innerR, a0, -hx), P(innerR, a0, hx), P(innerR, a1, hx), P(innerR, a1, -hx), nIn);
        }
        return st.Commit();
    }

    /// <summary>a→b→c→d counter-clockwise seen from the normal side.</summary>
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        TriN(st, a, b, c, n);
        TriN(st, a, c, d, n);
    }

    static void TriN(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        // Emit in whichever order faces along n (Godot front faces are clockwise).
        bool ccw = (b - a).Cross(c - a).Dot(n) > 0;
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(ccw ? c : b);
        st.SetNormal(n); st.AddVertex(ccw ? b : c);
    }

    public static StandardMaterial3D Glow(Color c, float energy = 3f) => new()
    {
        AlbedoColor = new Color(0.01f, 0.01f, 0.02f),
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        Roughness = 0.4f,
    };
}
