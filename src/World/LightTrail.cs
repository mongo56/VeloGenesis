using System.Collections.Generic;
using Godot;

namespace VeloGenesis.World;

/// <summary>Light-cycle style glowing ribbon left behind the rider.</summary>
public partial class LightTrail : MeshInstance3D
{
    const float MaxLength = 45f;
    const float Height = 0.45f;
    readonly List<Vector3> _pts = new();
    ImmediateMesh _mesh;
    ShaderMaterial _mat;

    public override void _Ready()
    {
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        _mat = new ShaderMaterial { Shader = Shaders.Trail };
        CastShadow = ShadowCastingSetting.Off;
    }

    public void SetColor(Color c) => _mat.SetShaderParameter("color", c);

    public void SetBoost(float b) => _mat.SetShaderParameter("boost", b);

    public void Reset() => _pts.Clear();

    public void Push(Vector3 p)
    {
        if (_pts.Count == 0 || _pts[^1].DistanceTo(p) > 0.8f) _pts.Add(p);
        // Trim from the tail so the ribbon length stays bounded.
        float len = 0;
        for (int i = _pts.Count - 1; i > 0; i--)
        {
            len += _pts[i].DistanceTo(_pts[i - 1]);
            if (len > MaxLength) { _pts.RemoveRange(0, i - 1); break; }
        }
        Rebuild(p);
    }

    void Rebuild(Vector3 head)
    {
        _mesh.ClearSurfaces();
        if (_pts.Count < 2) return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _mat);
        float acc = 0;
        var prev = head;
        float prevAge = 0;
        for (int i = _pts.Count - 1; i >= 0; i--)
        {
            var p = _pts[i];
            acc += prev.DistanceTo(p);
            float age = Mathf.Min(acc / MaxLength, 1f);
            if (acc > 0.01f)
            {
                Quad(prev, p, prevAge, age);
            }
            prev = p;
            prevAge = age;
        }
        _mesh.SurfaceEnd();
    }

    void Quad(Vector3 a, Vector3 b, float ua, float ub)
    {
        var up = Vector3.Up * Height;
        V(a, ua, 0); V(a + up, ua, 1); V(b, ub, 0);
        V(b, ub, 0); V(a + up, ua, 1); V(b + up, ub, 1);
    }

    void V(Vector3 p, float u, float v)
    {
        _mesh.SurfaceSetUV(new Vector2(u, v));
        _mesh.SurfaceAddVertex(p);
    }
}
