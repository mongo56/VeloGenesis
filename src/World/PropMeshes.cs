using Godot;

namespace VeloGenesis.World;

/// <summary>Procedural roadside furniture and architecture built from boxes.</summary>
public static class PropMeshes
{
    static ArrayMesh _lamp, _gantry, _tower;
    static StandardMaterial3D _metal, _chrome;
    static ShaderMaterial _trim;

    public const float GantryHeight = 7f;
    public static readonly float GantryHalfSpan = WorldBuilder.RoadHalfWidth + 1.6f;

    /// <summary>Dark gunmetal for structures.</summary>
    public static StandardMaterial3D Metal => _metal ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.07f, 0.06f, 0.09f),
        Metallic = 0.85f,
        Roughness = 0.32f,
    };

    /// <summary>Polished chrome (reflects the sunset sky and the neon).</summary>
    public static StandardMaterial3D Chrome => _chrome ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.92f, 0.86f, 0.96f),
        Metallic = 1f,
        Roughness = 0.1f,
    };

    /// <summary>Glowing trim tinted by each instance's colour.</summary>
    public static ShaderMaterial Trim => _trim ??= new ShaderMaterial { Shader = Shaders.InstanceGlow };

    /// <summary>
    /// Street lamp at the road edge: plinth, square pole with a glowing strip and an arm reaching
    /// over the road (+X) ending in a light bar. Origin at ground level.
    /// </summary>
    public static ArrayMesh LampPost => _lamp ??= BuildLamp();

    /// <summary>Where the lamp's light bar sits, in the lamp's local space.</summary>
    public static readonly Vector3 LampHead = new(0.75f, 2.42f, 0);

    static ArrayMesh BuildLamp()
    {
        var metal = Begin();
        var glow = Begin();
        AddBox(metal, new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.2f, 0.3f));
        AddBox(metal, new Vector3(0, 1.35f, 0), new Vector3(0.09f, 2.5f, 0.09f));
        AddBox(glow, new Vector3(0.05f, 1.3f, 0), new Vector3(0.014f, 2.0f, 0.03f));
        AddBox(metal, new Vector3(0.36f, 2.56f, 0), new Vector3(0.8f, 0.07f, 0.09f));
        AddBox(metal, new Vector3(0.75f, 2.5f, 0), new Vector3(0.52f, 0.07f, 0.17f));
        AddBox(glow, new Vector3(0.75f, 2.45f, 0), new Vector3(0.48f, 0.05f, 0.19f));
        return Commit(metal, Metal, glow, Trim);
    }

    /// <summary>
    /// Box-truss gantry spanning the road (along X), with glowing strips on the legs and under the
    /// truss. Origin at road centre, ground level.
    /// </summary>
    public static ArrayMesh Gantry => _gantry ??= BuildGantry();

    static ArrayMesh BuildGantry()
    {
        var metal = Begin();
        var glow = Begin();
        float w = GantryHalfSpan, h = GantryHeight;
        foreach (int s in new[] { -1, 1 })
        {
            AddBox(metal, new Vector3(s * w, h * 0.5f - 0.6f, 0), new Vector3(0.4f, h + 1.2f, 0.4f));
            AddBox(metal, new Vector3(s * w, 0.15f, 0), new Vector3(0.8f, 0.3f, 0.8f));
            AddBox(glow, new Vector3(s * (w - 0.21f), h * 0.45f, 0), new Vector3(0.014f, h * 0.7f, 0.1f));
        }
        float len = w * 2 + 0.4f, top = h, bottom = h - 1.1f;
        foreach (float z in new[] { -0.35f, 0.35f })
        {
            AddBox(metal, new Vector3(0, top, z), new Vector3(len, 0.14f, 0.14f));
            AddBox(metal, new Vector3(0, bottom, z), new Vector3(len, 0.14f, 0.14f));
            // Verticals and alternating diagonals (a Warren truss) in the front and back planes.
            int bays = 9;
            float bay = (w * 2) / bays;
            for (int b = 0; b <= bays; b++)
            {
                float x = -w + b * bay;
                AddBox(metal, new Vector3(x, (top + bottom) * 0.5f, z), new Vector3(0.08f, top - bottom, 0.08f));
                if (b == bays) continue;
                float dx = bay, dy = (top - bottom) * (b % 2 == 0 ? 1 : -1);
                float ang = Mathf.Atan2(dy, dx);
                AddBox(metal, new Vector3(x + bay * 0.5f, (top + bottom) * 0.5f, z), new Vector3(Mathf.Sqrt(dx * dx + dy * dy), 0.06f, 0.06f), new Basis(Vector3.Back, ang));
            }
        }
        // Cross members joining front and back.
        for (int b = 0; b <= 3; b++)
            AddBox(metal, new Vector3(-w + b * (w * 2 / 3), bottom, 0), new Vector3(0.08f, 0.08f, 0.7f));
        AddBox(glow, new Vector3(0, bottom - 0.09f, 0), new Vector3(w * 2 - 0.6f, 0.02f, 0.3f));
        AddBox(glow, new Vector3(0, top + 0.08f, 0.35f), new Vector3(len, 0.02f, 0.04f));
        AddBox(glow, new Vector3(0, top + 0.08f, -0.35f), new Vector3(len, 0.02f, 0.04f));
        return Commit(metal, Metal, glow, Trim);
    }

    /// <summary>
    /// Stepped skyscraper in a unit box (x/z ±0.5, y −0.5..0.5) with setbacks and a spire above
    /// the box. Single surface for the window shader.
    /// </summary>
    public static ArrayMesh Tower => _tower ??= BuildTower();

    static ArrayMesh BuildTower()
    {
        var st = Begin();
        AddBox(st, new Vector3(0, -0.2f, 0), new Vector3(1f, 0.6f, 1f));
        AddBox(st, new Vector3(0, 0.15f, 0), new Vector3(0.8f, 0.1f, 0.8f));
        AddBox(st, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.62f, 0.2f, 0.62f));
        AddBox(st, new Vector3(0.05f, 0.45f, -0.05f), new Vector3(0.4f, 0.1f, 0.4f));
        AddBox(st, new Vector3(0.05f, 0.56f, -0.05f), new Vector3(0.025f, 0.12f, 0.025f));
        return st.Commit();
    }

    // ------------------------------------------------------------------ helpers

    static SurfaceTool Begin()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        return st;
    }

    static ArrayMesh Commit(SurfaceTool a, Material ma, SurfaceTool b, Material mb)
    {
        var mesh = new ArrayMesh();
        a.SetMaterial(ma);
        a.Commit(mesh);
        b.SetMaterial(mb);
        b.Commit(mesh);
        return mesh;
    }

    /// <summary>Adds a box (optionally rotated about its centre) with flat normals.</summary>
    public static void AddBox(SurfaceTool st, Vector3 center, Vector3 size, Basis? rot = null)
    {
        var r = rot ?? Basis.Identity;
        var h = size * 0.5f;
        Vector3 P(float x, float y, float z) => center + r * new Vector3(x * h.X, y * h.Y, z * h.Z);
        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Meshes.Quad(st, a, b, c, d, r * n);
        Face(Vector3.Right, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
        Face(Vector3.Left, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1));
        Face(Vector3.Up, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1));
        Face(Vector3.Down, P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1));
        Face(Vector3.Back, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Face(Vector3.Forward, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
    }
}
