using System;
using Godot;

namespace VeloGenesis.World;

/// <summary>
/// Neon gravel bike and rider with generated organic body shapes. Legs and arms use two-bone IK, cranks turn with
/// cadence, wheels spin with speed and the upper body sways slightly with each pedal stroke.
/// Local space: -Z forward, +X right, +Y up, origin on the ground under the bottom bracket.
/// </summary>
public partial class RiderAvatar : Node3D
{
    // Gravel bike geometry (≈56 cm frame, 700x42 tyres)
    const float WheelR = 0.355f, TyreW = 0.042f, RimDepth = 0.05f, Crank = 0.1725f;
    /// <summary>Stem length (steerer to bar centre), metres.</summary>
    const float Stem = 0.08f;
    static readonly Vector3 BB = new(0, 0.28f, 0);
    static readonly Vector3 SeatCluster = new(0, 0.8f, 0.155f);
    static readonly Vector3 HeadTop = new(0, 0.885f, -0.47f);
    static readonly Vector3 HeadBottom = new(0, 0.72f, -0.515f);
    static readonly Vector3 RearHub = new(0, WheelR, 0.43f);
    static readonly Vector3 FrontHub = new(0, WheelR, -0.605f);
    static readonly Vector3 SteererTop = new(0, 0.905f, -0.475f);
    static readonly Vector3 BarClamp = SteererTop + new Vector3(0, Stem * 0.17f, -Stem * 0.985f); // -10° stem
    static readonly Vector3 HoodOffset = new(0.207f, 0.022f, -0.068f);

    // Rider
    const float Thigh = 0.47f, Shin = 0.46f, UpperArm = 0.31f, Forearm = 0.3f, TorsoLen = 0.44f;
    static readonly Vector3 Pelvis = new(0, 1.05f, 0.17f);
    static readonly Vector3 Chest = new(0, 1.33f, -0.17f);
    static readonly Vector3 HeadPos = new(0, 1.5f, -0.33f);

    StandardMaterial3D _frameMat, _accentMat, _dimMat, _tailMat;
    ShaderMaterial _suitMat, _rubberMat;
    Node3D _rig, _front, _rear, _spider, _upper;
    MeshInstance3D _torso;
    readonly MeshInstance3D[] _deltoid = new MeshInstance3D[2];
    float _stand, _sprint;
    readonly MeshInstance3D[] _thigh = new MeshInstance3D[2], _shin = new MeshInstance3D[2], _shoe = new MeshInstance3D[2],
        _crankArm = new MeshInstance3D[2], _pedal = new MeshInstance3D[2],
        _upperArm = new MeshInstance3D[2], _forearm = new MeshInstance3D[2], _glove = new MeshInstance3D[2];
    float _crank, _time;

    static CylinderMesh _tube;
    static BoxMesh _box;
    static ArrayMesh _rimMesh, _rimDecal;

    public override void _Ready()
    {
        _tube ??= new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 10, Rings = 1 };
        _box ??= new BoxMesh { Size = Vector3.One };
        _rig = new Node3D();
        AddChild(_rig);

        _frameMat = Meshes.Glow(new Color(0.1f, 0.9f, 1f), 2.2f);
        _accentMat = Meshes.Glow(new Color(0.1f, 0.9f, 1f), 3.5f);
        _dimMat = Meshes.Glow(new Color(0.1f, 0.9f, 1f), 0.7f);
        _tailMat = Meshes.Glow(new Color(1f, 0.08f, 0.12f), 5f);
        _suitMat = new ShaderMaterial { Shader = Shaders.Rim };
        // Matte fabric kit with a soft coloured rim light.
        _suitMat.SetShaderParameter("rim", 1.1f);
        _suitMat.SetShaderParameter("body_color", new Color(0.06f, 0.045f, 0.09f));
        _suitMat.SetShaderParameter("metallic", 0.05f);
        _suitMat.SetShaderParameter("roughness", 0.65f);
        _rubberMat = new ShaderMaterial { Shader = Shaders.Rim };
        _rubberMat.SetShaderParameter("rim_color", new Color(0.35f, 0.38f, 0.5f));
        _rubberMat.SetShaderParameter("rim", 0.9f);
        _rubberMat.SetShaderParameter("body_color", new Color(0.012f, 0.012f, 0.016f));

        BuildBike();
        BuildRider();
        Pose(0);
    }

    /// <summary>Turns the whole bike and rider into a translucent hologram (ghost racer).</summary>
    public void MakeGhost(Color color)
    {
        var mat = new ShaderMaterial { Shader = Shaders.Ghost };
        mat.SetShaderParameter("color", color);
        void Apply(Node n)
        {
            if (n is GeometryInstance3D g)
            {
                g.MaterialOverride = mat;
                g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
            foreach (var c in n.GetChildren()) Apply(c);
        }
        Apply(this);
    }

    public void SetColors(Color primary, Color secondary)
    {
        _frameMat.Emission = primary;
        _accentMat.Emission = primary;
        _dimMat.Emission = primary;
        _suitMat.SetShaderParameter("rim_color", secondary);
    }

    // ---------------------------------------------------------------- bike

    void BuildBike()
    {
        _rear = Wheel(RearHub, true);
        _front = Wheel(FrontHub, false);

        // Main triangle: oversized down tube, sloping top tube.
        Tube(BB, HeadBottom, 0.024f, _frameMat);
        Tube(BB, SeatCluster, 0.017f, _frameMat);
        Tube(SeatCluster, HeadTop, 0.016f, _frameMat);
        Tube(HeadBottom + new Vector3(0, -0.02f, 0.005f), HeadTop + new Vector3(0, 0.03f, -0.01f), 0.022f, _frameMat);
        foreach (int s in new[] { -1, 1 })
        {
            Tube(BB + new Vector3(s * 0.04f, 0, 0.03f), RearHub + new Vector3(s * 0.065f, 0, 0), 0.011f, _frameMat);
            Tube(SeatCluster + new Vector3(s * 0.018f, -0.02f, 0.01f), RearHub + new Vector3(s * 0.065f, 0, 0), 0.009f, _frameMat);
            // Fork legs with a little rake
            Tube(HeadBottom + new Vector3(s * 0.05f, -0.03f, 0), FrontHub + new Vector3(s * 0.06f, 0.02f, -0.02f), 0.013f, _frameMat);
            Tube(FrontHub + new Vector3(s * 0.06f, 0.02f, -0.02f), FrontHub + new Vector3(s * 0.06f, 0, 0), 0.011f, _frameMat);
        }
        Tube(HeadBottom + new Vector3(-0.055f, -0.03f, 0), HeadBottom + new Vector3(0.055f, -0.03f, 0), 0.017f, _frameMat);

        // Cockpit: stem and flared drop bars with hoods.
        Tube(HeadTop, SteererTop, 0.018f, _rubberMat);            // spacers
        Tube(SteererTop + new Vector3(0, 0, 0.012f), BarClamp, 0.016f, _rubberMat);
        foreach (int s in new[] { -1, 1 })
        {
            // Compact gravel drop: ~70 mm reach, ~120 mm drop, flared ends. Offsets from the bar clamp.
            Vector3 B(float x, float y, float z) => BarClamp + new Vector3(s * x, y, z);
            var pts = new[] { BarClamp, B(0.2f, 0, 0), B(0.205f, 0.004f, -0.05f), B(0.212f, -0.03f, -0.085f), B(0.226f, -0.095f, -0.075f), B(0.236f, -0.13f, -0.03f), B(0.245f, -0.125f, 0.035f) };
            for (int i = 0; i < pts.Length - 1; i++) Tube(pts[i], pts[i + 1], 0.0125f, _rubberMat);
            Box(B(0.206f, 0.01f, -0.035f), B(0.209f, 0.002f, -0.095f), new Vector3(0.032f, 0, 0.045f), _rubberMat); // hood
            Tube(B(0.209f, -0.01f, -0.097f), B(0.22f, -0.12f, -0.112f), 0.007f, _dimMat);                          // lever
        }

        // Seatpost, saddle, saddle bag + blinking tail light.
        var clamp = new Vector3(0, 0.975f, 0.205f);
        Tube(SeatCluster, clamp, 0.0135f, _rubberMat);
        Box(new Vector3(0, 0.995f, 0.29f), new Vector3(0, 0.99f, 0.03f), new Vector3(0.13f, 0, 0.035f), _rubberMat);
        Box(new Vector3(0, 0.995f, 0.29f), new Vector3(0, 0.995f, 0.1f), new Vector3(0.012f, 0, 0.038f), _dimMat);
        Box(new Vector3(0, 0.93f, 0.36f), new Vector3(0, 0.935f, 0.23f), new Vector3(0.085f, 0, 0.075f), _rubberMat);
        Box(new Vector3(0, 0.93f, 0.367f), new Vector3(0, 0.93f, 0.37f), new Vector3(0.045f, 0, 0.02f), _tailMat);

        // Frame bag (it's a gravel bike) with a glowing zip.
        var bagA = SeatCluster + new Vector3(0, -0.075f, -0.06f);
        var bagB = HeadTop + new Vector3(0, -0.1f, 0.11f);
        Box(bagA, bagB, new Vector3(0.055f, 0, 0.11f), _rubberMat);
        Box(bagA + new Vector3(0.029f, 0.035f, 0), bagB + new Vector3(0.029f, 0.035f, 0), new Vector3(0.004f, 0, 0.006f), _accentMat);

        // Drivetrain: chainring + spider rotate with the cranks; chain to the cassette.
        _spider = new Node3D { Position = BB + new Vector3(0.068f, 0, 0) };
        _rig.AddChild(_spider);
        _spider.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.088f, OuterRadius = 0.104f, Rings = 32, RingSegments = 6 }, MaterialOverride = _accentMat, Rotation = new Vector3(0, 0, Mathf.Pi / 2) });
        for (int i = 0; i < 4; i++)
            _spider.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.006f, 0.17f, 0.016f) }, MaterialOverride = _rubberMat, Rotation = new Vector3(i * Mathf.Pi / 4, 0, 0) });
        var cog = new Vector3(0.068f, 0, 0);
        Tube(BB + cog + new Vector3(0, 0.096f, 0), RearHub + cog + new Vector3(0, 0.045f, 0), 0.004f, _dimMat);
        Tube(BB + cog + new Vector3(0, -0.096f, 0), RearHub + cog + new Vector3(0, -0.045f, 0), 0.004f, _dimMat);
        var cass = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.03f }, MaterialOverride = _rubberMat, Position = RearHub + cog, Rotation = new Vector3(0, 0, Mathf.Pi / 2) };
        _rig.AddChild(cass);
        Box(RearHub + new Vector3(0.075f, -0.03f, 0.02f), RearHub + new Vector3(0.075f, -0.11f, 0.0f), new Vector3(0.012f, 0, 0.03f), _dimMat); // derailleur

        for (int s = 0; s < 2; s++)
        {
            _crankArm[s] = Tube(Vector3.Zero, Vector3.Up, 0.012f, _rubberMat);
            _pedal[s] = Part(_box, _dimMat);
        }
    }

    Node3D Wheel(Vector3 hub, bool rear)
    {
        var w = new Node3D { Position = hub };
        _rig.AddChild(w);
        var side = new Vector3(0, 0, Mathf.Pi / 2);
        w.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = WheelR - TyreW, OuterRadius = WheelR, Rings = 48, RingSegments = 10 }, MaterialOverride = _rubberMat, Rotation = side });
        // 50 mm deep carbon rim with a glowing decal band on both side walls.
        float rimOuter = WheelR - TyreW + 0.004f, rimInner = rimOuter - RimDepth;
        _rimMesh ??= Meshes.Rim(rimInner, rimOuter, 0.026f);
        _rimDecal ??= Meshes.Rim(rimInner + 0.02f, rimInner + 0.03f, 0.0275f);
        w.AddChild(new MeshInstance3D { Mesh = _rimMesh, MaterialOverride = _rubberMat });
        w.AddChild(new MeshInstance3D { Mesh = _rimDecal, MaterialOverride = _accentMat });
        for (int i = 0; i < 12; i++)
            w.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.003f, rimInner * 2f, 0.003f) }, MaterialOverride = _dimMat, Rotation = new Vector3(i * Mathf.Pi / 12f, 0, 0) });
        w.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.11f }, MaterialOverride = _rubberMat, Rotation = side });
        w.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = rear ? 0.07f : 0.08f, BottomRadius = rear ? 0.07f : 0.08f, Height = 0.004f, RadialSegments = 24 }, MaterialOverride = _dimMat, Rotation = side, Position = new Vector3(-0.055f, 0, 0) });
        return w;
    }

    // ---------------------------------------------------------------- rider

    void BuildRider()
    {
        // All body parts are smooth generated surfaces (see Meshes.Lathe / Limb) with kit details.
        _bodyMeshes ??= new BodyMeshes();
        var bm = _bodyMeshes;

        // Torso: one contoured shape from the seat to the base of the neck (hips → waist → chest →
        // shoulders). Local Y runs pelvis → chest, local Z points out of the rider's back.
        _torso = Part(bm.Torso, _suitMat);
        for (int i = 0; i < 6; i++)
        {
            float y0 = 0.05f + i * 0.065f, y1 = y0 + 0.05f;
            float zz = Math.Max(bm.TorsoDepth((y0 + y1) / 2), 0.01f);
            Child(_torso, _box, _accentMat, new Vector3(0, (y0 + y1) / 2, zz + 0.002f), new Vector3(0.016f, y1 - y0, 0.006f)); // spine stripe
            foreach (int s in new[] { -1, 1 })
            {
                float xx = bm.TorsoWidth((y0 + y1) / 2);
                Child(_torso, _box, _dimMat, new Vector3(s * (xx + 0.002f), (y0 + y1) / 2, 0), new Vector3(0.006f, y1 - y0, 0.02f)); // side panels
            }
        }
        var collar = Child(_torso, new TorusMesh { InnerRadius = 0.088f, OuterRadius = 0.102f, Rings = 24, RingSegments = 6 }, _accentMat, new Vector3(0, 0.535f, 0), new Vector3(0.95f, 1, 0.78f));

        // Head group rides on the chest.
        _upper = new Node3D();
        _rig.AddChild(_upper);
        var neck = new MeshInstance3D { Mesh = bm.Neck, MaterialOverride = _suitMat };
        _upper.AddChild(neck);
        OrientFrom(neck, Chest + new Vector3(0, 0.05f, -0.03f), HeadPos + new Vector3(0, -0.07f, 0.03f));
        _upper.AddChild(new MeshInstance3D { Mesh = bm.Head, MaterialOverride = _suitMat, Transform = new Transform3D(new Basis(Vector3.Right, 0.25f), HeadPos) });
        // Gravel / enduro helmet: deep shell tilted so it sits high over the brow and wraps low
        // around the back of the head, plus a peak and big glowing vents.
        var helmetCentre = HeadPos + new Vector3(0, 0.035f, 0.025f);
        var helmetTilt = new Basis(Vector3.Right, 0.4f);
        _upper.AddChild(new MeshInstance3D { Mesh = bm.Helmet, MaterialOverride = _rubberMat, Transform = new Transform3D(helmetTilt, helmetCentre) });
        // Glowing rim line just above the lower edge.
        _upper.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.97f, OuterRadius = 1.0f, Rings = 40, RingSegments = 4 },
            MaterialOverride = _accentMat,
            Transform = new Transform3D(helmetTilt * Basis.FromScale(new Vector3(0.124f, 0.6f, 0.147f)), helmetCentre + helmetTilt * new Vector3(0, -0.058f, 0)),
        });
        // Peak: a thin rounded plate over the brow, angled slightly down.
        var peakBasis = new Basis(Vector3.Right, -0.22f);
        _upper.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.105f, BottomRadius = 0.105f, Height = 0.01f, RadialSegments = 32 },
            MaterialOverride = _rubberMat,
            Transform = new Transform3D(peakBasis * Basis.FromScale(new Vector3(1f, 1f, 0.62f)), HeadPos + new Vector3(0, 0.07f, -0.125f)),
        });
        _upper.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.1f, OuterRadius = 0.108f, Rings = 32, RingSegments = 4 },
            MaterialOverride = _accentMat,
            Transform = new Transform3D(peakBasis * Basis.FromScale(new Vector3(1f, 0.6f, 0.62f)), HeadPos + new Vector3(0, 0.07f, -0.125f)),
        });
        foreach (float dx in new[] { -0.06f, -0.02f, 0.02f, 0.06f })
        {
            var vent = new MeshInstance3D { Mesh = _box, MaterialOverride = _accentMat };
            _upper.AddChild(vent);
            float top = 0.172f - dx * dx * 7f;
            Orient(vent, HeadPos + new Vector3(dx, top + 0.005f, -0.06f), HeadPos + new Vector3(dx, top - 0.01f, 0.1f), new Vector3(0.016f, 1, 0.012f));
        }
        // Wraparound visor band.
        _upper.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.083f, OuterRadius = 0.097f, Rings = 32, RingSegments = 6 },
            MaterialOverride = _accentMat,
            Transform = new Transform3D(new Basis(Vector3.Right, 0.25f) * Basis.FromScale(new Vector3(1f, 1.4f, 1.15f)), HeadPos + new Vector3(0, 0.005f, -0.01f)),
        });

        for (int s = 0; s < 2; s++)
        {
            _deltoid[s] = Part(bm.Deltoid, _suitMat);
            _thigh[s] = Part(bm.Thigh, _suitMat);
            Child(_thigh[s], bm.Band(0.066f), _accentMat, new Vector3(0, Thigh * 0.8f, 0), Vector3.One);      // shorts gripper
            _shin[s] = Part(bm.Shin, _suitMat);
            Child(_shin[s], bm.Band(0.042f), _dimMat, new Vector3(0, Shin * 0.82f, 0), Vector3.One);         // sock top
            _shoe[s] = Part(bm.Shoe, _rubberMat);
            Child(_shoe[s], _box, _accentMat, new Vector3(0, 0.14f, -0.042f), new Vector3(0.07f, 0.24f, 0.008f)); // glowing sole
            Child(_shoe[s], _box, _dimMat, new Vector3(0, 0.12f, 0.04f), new Vector3(0.075f, 0.018f, 0.006f));    // strap
            _upperArm[s] = Part(bm.UpperArm, _suitMat);
            Child(_upperArm[s], bm.Band(0.054f), _accentMat, new Vector3(0, UpperArm * 0.55f, 0), Vector3.One); // sleeve cuff
            _forearm[s] = Part(bm.Forearm, _suitMat);
            _glove[s] = Part(bm.Glove, _dimMat);
        }
    }

    /// <summary>Generated body meshes, shared by all avatars.</summary>
    sealed class BodyMeshes
    {
        public readonly Func<float, float> TorsoWidth = Meshes.Profile(-0.12f, 0, 0f, 0.168f, 0.17f, 0.13f, 0.32f, 0.16f, 0.41f, 0.175f, 0.48f, 0.13f, 0.54f, 0.075f, 0.59f, 0);
        public readonly Func<float, float> TorsoDepth = Meshes.Profile(-0.12f, 0, 0f, 0.12f, 0.17f, 0.1f, 0.32f, 0.125f, 0.41f, 0.11f, 0.48f, 0.09f, 0.54f, 0.065f, 0.59f, 0);
        public readonly ArrayMesh Torso, Neck, Head, Helmet, Thigh, Shin, UpperArm, Forearm, Glove, Shoe, Deltoid;
        readonly System.Collections.Generic.Dictionary<float, Mesh> _bands = new();

        public BodyMeshes()
        {
            Torso = Meshes.Lathe(TorsoWidth, TorsoDepth, -0.12f, 0.59f, 34, 20);
            Neck = Meshes.Limb(0.11f, 0.05f, 0.046f);
            Head = Meshes.Lathe(Meshes.Profile(-0.11f, 0, -0.06f, 0.07f, 0f, 0.083f, 0.06f, 0.078f, 0.115f, 0),
                                Meshes.Profile(-0.11f, 0, -0.06f, 0.085f, 0f, 0.1f, 0.06f, 0.092f, 0.115f, 0), -0.11f, 0.115f, 18, 18);
            // Domed enduro shell (vertical axis): width rx, front-back depth rz.
            Helmet = Meshes.Lathe(Meshes.Profile(-0.09f, 0, -0.07f, 0.118f, 0f, 0.13f, 0.07f, 0.118f, 0.12f, 0.075f, 0.145f, 0),
                                  Meshes.Profile(-0.09f, 0, -0.07f, 0.142f, 0f, 0.153f, 0.07f, 0.135f, 0.12f, 0.085f, 0.145f, 0), -0.09f, 0.145f, 22, 24);
            Thigh = Meshes.Limb(RiderAvatar.Thigh, 0.086f, 0.062f, 0.35f, 0.01f, 1.06f);
            Shin = Meshes.Limb(RiderAvatar.Shin, 0.056f, 0.036f, 0.28f, 0.013f, 1.12f);
            UpperArm = Meshes.Limb(RiderAvatar.UpperArm, 0.058f, 0.044f, 0.45f, 0.006f);
            Forearm = Meshes.Limb(RiderAvatar.Forearm, 0.046f, 0.031f, 0.25f, 0.007f);
            // Shoulder cap that bridges torso and upper arm.
            Deltoid = Meshes.Limb(0.12f, 0.063f, 0.051f, 0.3f, 0.004f, 1.1f);
            Glove = Meshes.Limb(0.06f, 0.034f, 0.03f, 0.5f, 0.004f, 0.75f);
            Shoe = Meshes.Lathe(Meshes.Profile(0f, 0, 0.03f, 0.04f, 0.15f, 0.048f, 0.24f, 0.036f, 0.28f, 0),
                                Meshes.Profile(0f, 0, 0.03f, 0.045f, 0.12f, 0.04f, 0.24f, 0.025f, 0.28f, 0), 0f, 0.28f, 16, 14);
        }

        /// <summary>Thin glowing ring that wraps a limb of the given radius (cuffs, grippers).</summary>
        public Mesh Band(float radius)
        {
            if (!_bands.TryGetValue(radius, out var m))
                _bands[radius] = m = new TorusMesh { InnerRadius = radius - 0.004f, OuterRadius = radius + 0.006f, Rings = 24, RingSegments = 6 };
            return m;
        }
    }

    static BodyMeshes _bodyMeshes;

    static MeshInstance3D Child(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Transform = new Transform3D(Basis.FromScale(scale), pos) };
        parent.AddChild(m);
        return m;
    }

    // Shoulder joint sits inside the torso's shoulder, so the deltoid blend can cover the seam.
    static Vector3 ShoulderPos(int side) => Chest + new Vector3(side * 0.165f, -0.005f, -0.015f);

    // ---------------------------------------------------------------- animation

    /// <summary>0..1 blends: out of the saddle, and sprinting in the drops.</summary>
    public void SetStance(float stand, float sprint)
    {
        _stand = Math.Clamp(stand, 0f, 1f);
        _sprint = Math.Clamp(sprint, 0f, 1f);
    }

    public void Animate(float speed, float cadence, float dt)
    {
        _time += dt;
        float spin = speed / WheelR * dt;
        // Negative rotation about +X moves the top of the wheel forward (-Z).
        _front.Rotate(Vector3.Right, -spin);
        _rear.Rotate(Vector3.Right, -spin);
        _crank += cadence / 60f * Mathf.Tau * dt;
        _tailMat.EmissionEnergyMultiplier = (_time % 0.8f) < 0.12f ? 7f : 1.2f;
        Pose(cadence);
    }

    void Pose(float cadence)
    {
        float a = _crank;
        _spider.Rotation = new Vector3(-a, 0, 0);

        // Seated: small sway with the pedal strokes. Standing: hips up and forward over the cranks,
        // shoulders over the bars, and the bike rocks away from the pushing leg. Sprint: lower, in
        // the drops, rocking harder.
        float effort = Math.Clamp(cadence / 90f, 0f, 1f);
        float seated = 1f - _stand;
        var sway = new Vector3(0.012f * MathF.Sin(a) * effort * seated, 0.006f * MathF.Cos(2 * a) * effort, 0);
        var pelvis = Pelvis + _stand * new Vector3(0, 0.06f, -0.18f) + _sprint * new Vector3(0, -0.02f, -0.03f)
            + new Vector3(0, 0.02f * MathF.Abs(MathF.Cos(a)) * _stand, 0);
        var chest = Chest + _stand * new Vector3(0, 0.05f, -0.1f) + _sprint * new Vector3(0, -0.09f, -0.05f) + sway;
        float rock = _stand * (0.05f + 0.06f * _sprint) * MathF.Sin(a) * effort;
        _rig.Rotation = new Vector3(0, 0, -rock);
        _upper.Rotation = new Vector3(0, 0, rock * 0.6f);
        _upper.Position = chest - Chest;

        // The torso mesh is built for the seated length; squash slightly when standing.
        OrientFrom(_torso, pelvis, chest, (chest - pelvis).Length() / TorsoLen);

        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? -1 : 1;
            // Forward pedalling: from the front (3 o'clock seen from the right) the foot goes DOWN.
            float ca = a + i * Mathf.Pi;
            var crankDir = new Vector3(0, -MathF.Sin(ca), -MathF.Cos(ca));
            var axle = BB + new Vector3(side * 0.085f, 0, 0) + crankDir * Crank;
            PlaceTube(_crankArm[i], BB + new Vector3(side * 0.08f, 0, 0), axle, 0.012f);
            _pedal[i].Transform = new Transform3D(Basis.FromScale(new Vector3(0.085f, 0.018f, 0.075f)), axle + new Vector3(side * 0.045f, 0, 0));

            // Shoe clipped to the pedal, ankle behind and above the axle.
            var foot = axle + new Vector3(side * 0.04f, 0, 0);
            var ankle = foot + new Vector3(0, 0.075f, 0.055f);
            var toe = foot + new Vector3(0, 0.025f, -0.12f);
            var heel = ankle + new Vector3(0, -0.035f, 0.05f);
            OrientFrom(_shoe[i], heel + new Vector3(0, -0.005f, 0.02f), toe, (toe - heel).Length() / 0.26f);

            // Hip joint a few cm above the saddle and forward, roughly where the femur head is.
            var hip = pelvis + new Vector3(side * 0.09f, 0f, -0.03f);
            var knee = Joint(hip, ankle, Thigh, Shin, new Vector3(side * 0.12f, 0.25f, -1f));
            OrientFrom(_thigh[i], hip, knee);
            OrientFrom(_shin[i], knee, ankle);

            // Shoulder in rig space, following the counter-rotated upper body.
            var shoulder = _upper.Transform * ShoulderPos(side);
            var hood = BarClamp + new Vector3(side * HoodOffset.X, HoodOffset.Y, HoodOffset.Z);
            var drop = BarClamp + new Vector3(side * 0.236f, -0.12f, -0.035f);
            var hand = hood.Lerp(drop, _sprint);
            var elbow = Joint(shoulder, hand, UpperArm, Forearm, new Vector3(side * 0.7f, -0.35f, 0.6f));
            OrientFrom(_upperArm[i], shoulder, elbow);
            var armDir = (elbow - shoulder).Normalized();
            OrientFrom(_deltoid[i], shoulder - armDir * 0.035f + new Vector3(-side * 0.02f, -0.005f, 0), shoulder + armDir * 0.08f);
            OrientFrom(_forearm[i], elbow, hand - (hand - elbow).Normalized() * 0.02f);
            var wristDir = (hand - elbow).Normalized();
            OrientFrom(_glove[i], hand - wristDir * 0.03f, hand + wristDir * 0.04f + Vector3.Down * 0.015f);
        }
    }

    /// <summary>Two-bone IK: middle joint position bending towards <paramref name="pole"/>.</summary>
    static Vector3 Joint(Vector3 root, Vector3 target, float a, float b, Vector3 pole)
    {
        var d = target - root;
        float len = Math.Clamp(d.Length(), 0.01f, a + b - 0.002f);
        var dir = d.Normalized();
        float along = (a * a - b * b + len * len) / (2f * len);
        float h = MathF.Sqrt(Math.Max(a * a - along * along, 0f));
        var bend = (pole - dir * pole.Dot(dir)).Normalized();
        return root + dir * along + bend * h;
    }

    // ---------------------------------------------------------------- helpers

    MeshInstance3D Part(Mesh mesh, Material mat)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
        _rig.AddChild(m);
        return m;
    }

    MeshInstance3D Tube(Vector3 a, Vector3 b, float radius, Material mat)
    {
        var m = Part(_tube, mat);
        PlaceTube(m, a, b, radius);
        return m;
    }

    void Box(Vector3 a, Vector3 b, Vector3 widthDepth, Material mat) => Orient(Part(_box, mat), a, b, new Vector3(widthDepth.X, 1, widthDepth.Z));

    static void PlaceTube(Node3D m, Vector3 a, Vector3 b, float radius) => Orient(m, a, b, new Vector3(radius, 1, radius));

    /// <summary>Places a mesh built along +Y from its origin so it starts at a and points at b.</summary>
    static void OrientFrom(Node3D m, Vector3 a, Vector3 b, float yScale = 1f)
    {
        var y = b - a;
        if (y.LengthSquared() < 1e-10f) return;
        y = y.Normalized();
        var x = Vector3.Right - y * y.X;
        if (x.LengthSquared() < 1e-3f) x = Vector3.Forward - y * y.Dot(Vector3.Forward);
        x = x.Normalized();
        m.Transform = new Transform3D(new Basis(x, y * yScale, x.Cross(y)), a);
    }

    /// <summary>
    /// Points the node's Y axis from a to b, centred between them. scale.Y multiplies the length for
    /// unit meshes (pass 1 for meshes already built at the right length, e.g. capsules).
    /// </summary>
    static void Orient(Node3D m, Vector3 a, Vector3 b, Vector3 scale)
    {
        var y = b - a;
        float len = y.Length();
        if (len < 1e-5f) return;
        y /= len;
        // Keep local X close to the bike's X so widths (torso, shoes) stay sideways.
        var x = Vector3.Right - y * y.X;
        if (x.LengthSquared() < 1e-3f) x = Vector3.Forward - y * y.Dot(Vector3.Forward);
        x = x.Normalized();
        var z = x.Cross(y);
        bool unit = m is MeshInstance3D mi && (mi.Mesh == _tube || mi.Mesh == _box);
        float ly = unit ? len * scale.Y : scale.Y;
        m.Transform = new Transform3D(new Basis(x * scale.X, y * ly, z * scale.Z), (a + b) * 0.5f);
    }
}
