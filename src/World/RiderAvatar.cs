using System;
using Godot;

namespace VeloGenesis.World;

/// <summary>
/// Gravel bike and rider with generated organic body shapes. Legs and arms use two-bone IK, cranks turn with
/// cadence, wheels spin with speed and the upper body sways slightly with each pedal stroke.
/// Local space: -Z forward, +X right, +Y up, origin on the ground under the bottom bracket.
/// </summary>
public partial class RiderAvatar : Node3D
{
    // Modern gravel race geometry (≈56 cm, 700x45 tan-wall tyres): steeply sloping top tube,
    // dropped seat stays, long exposed seatpost, 415 mm stays, ≈1055 mm wheelbase.
    const float WheelR = 0.36f, TyreW = 0.045f, RimDepth = 0.05f, Crank = 0.1725f;
    /// <summary>Stem length (steerer to bar centre), metres.</summary>
    const float Stem = 0.08f;
    static readonly Vector3 BB = new(0, 0.29f, 0);
    static readonly Vector3 SeatTubeTop = new(0, 0.81f, 0.117f);
    static readonly Vector3 SeatCluster = new(0, 0.77f, 0.108f);
    static readonly Vector3 SeatStayJoin = new(0, 0.645f, 0.08f);
    static readonly Vector3 HeadTop = new(0, 0.883f, -0.425f);
    static readonly Vector3 HeadBottom = new(0, 0.682f, -0.507f);
    static readonly Vector3 RearHub = new(0, WheelR, 0.415f);
    static readonly Vector3 FrontHub = new(0, WheelR, -0.64f);
    static readonly Vector3 SteererTop = new(0, 0.91f, -0.414f);
    static readonly Vector3 BarClamp = SteererTop + new Vector3(0, Stem * 0.17f, -Stem * 0.985f); // -10° stem
    static readonly Vector3 HoodOffset = new(0.207f, 0.022f, -0.068f);

    // Rider
    const float Thigh = 0.47f, Shin = 0.46f, UpperArm = 0.31f, Forearm = 0.3f, TorsoLen = 0.44f;
    static readonly Vector3 Pelvis = new(0, 1.05f, 0.17f);
    static readonly Vector3 Chest = new(0, 1.33f, -0.17f);
    static readonly Vector3 HeadPos = new(0, 1.5f, -0.33f);

    static readonly Color Skin = new(0.56f, 0.37f, 0.26f);
    static readonly Color Tee = new(0.8f, 0.58f, 0.07f);
    static readonly Color Shorts = new(0.03f, 0.03f, 0.035f);
    static readonly Color White = new(0.86f, 0.87f, 0.89f);

    StandardMaterial3D _decalMat, _tailMat, _carbonMat, _steelMat, _spokeMat, _helmetMat, _lensMat, _shoeMat, _ventMat;
    ShaderMaterial _frameMat, _rubberMat, _tyreMat, _skinMat, _teeMat, _shortsMat, _torsoMat, _shinMat;
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
        _tube ??= new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 12, Rings = 1 };
        _box ??= new BoxMesh { Size = Vector3.One };
        _rig = new Node3D();
        AddChild(_rig);

        // Bike: matte sage frame with a thin route-coloured pinstripe, black finishing kit,
        // deep carbon rims, tan-wall gravel tyres.
        _frameMat = new ShaderMaterial { Shader = Shaders.Paint };
        _frameMat.SetShaderParameter("color", new Color(0.1f, 0.9f, 1f));
        _tailMat = Meshes.Glow(new Color(1f, 0.08f, 0.12f), 5f);
        _rubberMat = new ShaderMaterial { Shader = Shaders.Rim };
        _rubberMat.SetShaderParameter("rim_color", new Color(0.35f, 0.38f, 0.5f));
        _rubberMat.SetShaderParameter("rim", 0.6f);
        _rubberMat.SetShaderParameter("body_color", new Color(0.015f, 0.015f, 0.018f));
        _tyreMat = new ShaderMaterial { Shader = Shaders.Tyre };
        _carbonMat = new StandardMaterial3D { AlbedoColor = new Color(0.025f, 0.025f, 0.03f), Metallic = 0.3f, Roughness = 0.22f, ClearcoatEnabled = true, Clearcoat = 1f };
        _steelMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.74f), Metallic = 1f, Roughness = 0.25f };
        _decalMat = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.79f, 0.8f), Roughness = 0.45f, EmissionEnabled = true, Emission = new Color(0.8f, 0.82f, 0.85f), EmissionEnergyMultiplier = 0.12f };
        _spokeMat = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Metallic = 0.8f, Roughness = 0.35f };

        // Rider: white vented helmet, mirrored gold shades, loose yellow tee, black baggy shorts,
        // tall white socks and white shoes.
        _skinMat = Kit(Skin, Skin, 100f, 0.55f, 0.55f);
        _teeMat = Kit(Tee, Tee, 100f, 0.85f, 0.85f);
        _shortsMat = Kit(Shorts, Shorts, 100f, 0.8f, 0.8f);
        // Torso: shorts waistband below the tee hem.
        _torsoMat = Kit(Shorts, Tee, 0.06f, 0.8f, 0.85f);
        // Shin: skin from the knee, then the sock.
        _shinMat = Kit(Skin, White, 0.2f, 0.55f, 0.75f);
        _helmetMat = new StandardMaterial3D { AlbedoColor = White, Roughness = 0.28f, ClearcoatEnabled = true, Clearcoat = 0.8f };
        _ventMat = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.025f), Roughness = 0.6f };
        _lensMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.62f, 0.18f), Metallic = 1f, Roughness = 0.06f,
            EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 0.5f,
        };
        _shoeMat = new StandardMaterial3D { AlbedoColor = White, Roughness = 0.45f };

        BuildBike();
        BuildRider();
        AddFillLights();
        Pose(0);
    }

    /// <summary>Render layer used only by the avatar, so its fill lights don't light the world.</summary>
    const uint AvatarLayer = 1u << 9;
    readonly System.Collections.Generic.List<OmniLight3D> _fill = new();

    /// <summary>
    /// Soft neutral key and fill that only reach the rider and bike, so the kit keeps its real
    /// colours (yellow tee, white helmet, sage frame) under the magenta sunset.
    /// </summary>
    void AddFillLights()
    {
        void Layer(Node n)
        {
            if (n is GeometryInstance3D g) g.Layers |= AvatarLayer;
            foreach (var c in n.GetChildren()) Layer(c);
        }
        Layer(this);
        foreach (var (pos, energy) in new[] { (new Vector3(-0.9f, 2.4f, 1.8f), 1.5f), (new Vector3(1.2f, 1.8f, -1.6f), 0.8f) })
        {
            var l = new OmniLight3D
            {
                Position = pos,
                LightColor = new Color(1f, 0.95f, 0.88f),
                LightEnergy = energy,
                LightSpecular = 0.4f,
                LightVolumetricFogEnergy = 0f,
                OmniRange = 4.5f,
                OmniAttenuation = 0.6f,
                LightCullMask = AvatarLayer,
                ShadowEnabled = false,
            };
            AddChild(l);
            _fill.Add(l);
        }
    }

    static ShaderMaterial Kit(Color a, Color b, float split, float roughA, float roughB)
    {
        var m = new ShaderMaterial { Shader = Shaders.Kit };
        m.SetShaderParameter("color_a", a);
        m.SetShaderParameter("color_b", b);
        m.SetShaderParameter("split", split);
        m.SetShaderParameter("rough_a", roughA);
        m.SetShaderParameter("rough_b", roughB);
        return m;
    }

    /// <summary>Turns the whole bike and rider into a translucent hologram (ghost racer).</summary>
    public void MakeGhost(Color color)
    {
        var mat = new ShaderMaterial { Shader = Shaders.Ghost };
        mat.SetShaderParameter("color", color);
        foreach (var l in _fill) l.QueueFree();
        _fill.Clear();
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
        _frameMat.SetShaderParameter("color", primary);
        foreach (var m in new[] { _skinMat, _teeMat, _shortsMat, _torsoMat, _shinMat })
            m.SetShaderParameter("rim_color", secondary);
    }

    // ---------------------------------------------------------------- bike

    void BuildBike()
    {
        _rear = Wheel(RearHub, true);
        _front = Wheel(FrontHub, false);

        // Main triangle: fat down tube, slim top tube sloping down to a low seat cluster.
        Tube(BB, HeadBottom + new Vector3(0, 0.01f, 0.005f), 0.03f, _frameMat);
        Tube(BB + new Vector3(0, -0.01f, 0), SeatTubeTop, 0.018f, _frameMat);
        Tube(SeatCluster, HeadTop + new Vector3(0, -0.025f, 0.012f), 0.017f, _frameMat);
        Tube(HeadBottom + new Vector3(0, -0.025f, -0.01f), HeadTop + new Vector3(0, 0.025f, 0.01f), 0.026f, _frameMat);
        // Bottom-bracket shell.
        Part(_tube, _frameMat).Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2) * Basis.FromScale(new Vector3(0.026f, 0.085f, 0.026f)), BB);
        foreach (int s in new[] { -1, 1 })
        {
            Tube(BB + new Vector3(s * 0.035f, 0, 0.02f), RearHub + new Vector3(s * 0.066f, 0, 0), 0.012f, _frameMat);
            // Dropped seat stays meet the seat tube well below the top tube.
            Tube(SeatStayJoin + new Vector3(s * 0.016f, 0, 0.012f), RearHub + new Vector3(s * 0.064f, 0.012f, 0), 0.009f, _frameMat);
            // Carbon fork legs, raked forward to the hub.
            var crown = HeadBottom + new Vector3(s * 0.048f, -0.04f, 0.005f);
            var mid = crown.Lerp(FrontHub + new Vector3(s * 0.062f, 0, 0), 0.55f) + new Vector3(0, 0, -0.012f);
            Tube(crown, mid, 0.016f, _frameMat);
            Tube(mid, FrontHub + new Vector3(s * 0.062f, 0.015f, 0), 0.013f, _frameMat);
        }
        Tube(HeadBottom + new Vector3(-0.058f, -0.035f, 0), HeadBottom + new Vector3(0.058f, -0.035f, 0), 0.022f, _frameMat);

        // Cockpit: spacers, stem and flared drop bars with black tape and hoods.
        Tube(HeadTop, SteererTop, 0.017f, _rubberMat);
        Tube(SteererTop + new Vector3(0, 0, 0.012f), BarClamp, 0.016f, _rubberMat);
        foreach (int s in new[] { -1, 1 })
        {
            // Compact gravel drop: ~70 mm reach, ~120 mm drop, flared ends. Offsets from the bar clamp.
            Vector3 B(float x, float y, float z) => BarClamp + new Vector3(s * x, y, z);
            var pts = new[] { BarClamp, B(0.2f, 0, 0), B(0.205f, 0.004f, -0.05f), B(0.212f, -0.03f, -0.085f), B(0.226f, -0.095f, -0.075f), B(0.236f, -0.13f, -0.03f), B(0.245f, -0.125f, 0.035f) };
            for (int i = 0; i < pts.Length - 1; i++) Tube(pts[i], pts[i + 1], 0.0125f, _rubberMat);
            Box(B(0.206f, 0.01f, -0.035f), B(0.209f, 0.002f, -0.095f), new Vector3(0.032f, 0, 0.045f), _rubberMat); // hood
            Tube(B(0.209f, -0.01f, -0.097f), B(0.22f, -0.12f, -0.112f), 0.007f, _rubberMat);                       // lever
        }

        // Long exposed seatpost, slim saddle, small blinking tail light on the post.
        var clamp = SeatTubeTop + (SeatTubeTop - BB).Normalized() * 0.18f;
        Tube(SeatTubeTop + new Vector3(0, -0.02f, 0), clamp, 0.0135f, _rubberMat);
        Box(new Vector3(0, clamp.Y + 0.02f, clamp.Z + 0.11f), new Vector3(0, clamp.Y + 0.015f, clamp.Z - 0.15f), new Vector3(0.13f, 0, 0.03f), _rubberMat);
        var tail = SeatTubeTop + (clamp - SeatTubeTop) * 0.5f + new Vector3(0, 0, 0.018f);
        Box(tail + new Vector3(0, -0.03f, 0), tail + new Vector3(0, 0.03f, 0), new Vector3(0.025f, 0, 0.012f), _tailMat);

        // Drivetrain (1x): chainring + spider rotate with the cranks; chain to the cassette.
        _spider = new Node3D { Position = BB + new Vector3(0.068f, 0, 0) };
        _rig.AddChild(_spider);
        _spider.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.088f, OuterRadius = 0.104f, Rings = 40, RingSegments = 6 }, MaterialOverride = _steelMat, Rotation = new Vector3(0, 0, Mathf.Pi / 2) });
        for (int i = 0; i < 4; i++)
            _spider.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.008f, 0.17f, 0.018f) }, MaterialOverride = _rubberMat, Rotation = new Vector3(i * Mathf.Pi / 4, 0, 0) });
        var cog = new Vector3(0.068f, 0, 0);
        Tube(BB + cog + new Vector3(0, 0.096f, 0), RearHub + cog + new Vector3(0, 0.05f, 0), 0.004f, _steelMat);
        Tube(BB + cog + new Vector3(0, -0.096f, 0), RearHub + cog + new Vector3(0, -0.05f, 0), 0.004f, _steelMat);
        var cass = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.026f, BottomRadius = 0.056f, Height = 0.04f }, MaterialOverride = _steelMat, Position = RearHub + cog, Rotation = new Vector3(0, 0, Mathf.Pi / 2) };
        _rig.AddChild(cass);
        Box(RearHub + new Vector3(0.078f, -0.02f, 0.02f), RearHub + new Vector3(0.078f, -0.12f, -0.005f), new Vector3(0.016f, 0, 0.035f), _rubberMat); // derailleur

        for (int s = 0; s < 2; s++)
        {
            _crankArm[s] = Tube(Vector3.Zero, Vector3.Up, 0.012f, _rubberMat);
            _pedal[s] = Part(_box, _rubberMat);
        }
    }

    Node3D Wheel(Vector3 hub, bool rear)
    {
        var w = new Node3D { Position = hub };
        _rig.AddChild(w);
        var side = new Vector3(0, 0, Mathf.Pi / 2);
        w.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = WheelR - TyreW, OuterRadius = WheelR, Rings = 64, RingSegments = 12 }, MaterialOverride = _tyreMat, Rotation = side });
        // 50 mm deep carbon rim with a light decal band on both side walls.
        float rimOuter = WheelR - TyreW + 0.004f, rimInner = rimOuter - RimDepth;
        _rimMesh ??= Meshes.Rim(rimInner, rimOuter, 0.028f);
        _rimDecal ??= Meshes.Rim(rimInner + 0.022f, rimInner + 0.028f, 0.0295f);
        w.AddChild(new MeshInstance3D { Mesh = _rimMesh, MaterialOverride = _carbonMat });
        w.AddChild(new MeshInstance3D { Mesh = _rimDecal, MaterialOverride = _decalMat });
        for (int i = 0; i < 12; i++)
            w.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.0025f, rimInner * 2f, 0.0025f) }, MaterialOverride = _spokeMat, Rotation = new Vector3(i * Mathf.Pi / 12f, 0, 0) });
        w.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.11f }, MaterialOverride = _rubberMat, Rotation = side });
        // Disc rotor on the left.
        w.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = rear ? 0.07f : 0.08f, BottomRadius = rear ? 0.07f : 0.08f, Height = 0.003f, RadialSegments = 32 }, MaterialOverride = _steelMat, Rotation = side, Position = new Vector3(-0.055f, 0, 0) });
        return w;
    }

    // ---------------------------------------------------------------- rider

    void BuildRider()
    {
        // All body parts are smooth generated surfaces (see Meshes.Lathe / Limb) with kit details.
        _bodyMeshes ??= new BodyMeshes();
        var bm = _bodyMeshes;

        // Torso: one contoured shape from the seat to the base of the neck (hips → waist → chest →
        // shoulders), cut a little loose for the tee. Local Y runs pelvis → chest.
        _torso = Part(bm.Torso, _torsoMat);
        Child(_torso, new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.082f, Rings = 24, RingSegments = 6 }, _teeMat, new Vector3(0, 0.548f, 0), new Vector3(0.95f, 1, 0.8f)); // crew neck

        // Head group rides on the chest.
        _upper = new Node3D();
        _rig.AddChild(_upper);
        var neck = new MeshInstance3D { Mesh = bm.Neck, MaterialOverride = _skinMat };
        _upper.AddChild(neck);
        OrientFrom(neck, Chest + new Vector3(0, 0.05f, -0.03f), HeadPos + new Vector3(0, -0.07f, 0.03f));
        var headTilt = new Basis(Vector3.Right, 0.25f);
        _upper.AddChild(new MeshInstance3D { Mesh = bm.Head, MaterialOverride = _skinMat, Transform = new Transform3D(headTilt, HeadPos) });

        // Road / gravel helmet: rounded white shell sitting high over the brow, longer at the back,
        // with dark vent channels running front to back.
        var helmetCentre = HeadPos + new Vector3(0, 0.055f, 0.012f);
        var helmetTilt = new Basis(Vector3.Right, 0.32f);
        _upper.AddChild(new MeshInstance3D { Mesh = bm.Helmet, MaterialOverride = _helmetMat, Transform = new Transform3D(helmetTilt, helmetCentre) });
        foreach (float dx in new[] { -0.07f, -0.035f, 0f, 0.035f, 0.07f })
        {
            var vent = new MeshInstance3D { Mesh = _box, MaterialOverride = _ventMat };
            _upper.AddChild(vent);
            float top = 0.168f - dx * dx * 6f;
            Orient(vent, HeadPos + new Vector3(dx, top - 0.012f, -0.1f), HeadPos + new Vector3(dx, top + 0.012f, 0.05f), new Vector3(0.012f, 1, 0.01f));
        }
        // Dark lower edge of the shell and two rear vents.
        _upper.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.96f, OuterRadius = 1.0f, Rings = 40, RingSegments = 4 },
            MaterialOverride = _ventMat,
            Transform = new Transform3D(helmetTilt * Basis.FromScale(new Vector3(0.114f, 0.5f, 0.142f)), helmetCentre + helmetTilt * new Vector3(0, -0.05f, 0)),
        });
        foreach (float dx in new[] { -0.04f, 0.04f })
        {
            var vent = new MeshInstance3D { Mesh = _box, MaterialOverride = _ventMat };
            _upper.AddChild(vent);
            Orient(vent, helmetCentre + helmetTilt * new Vector3(dx, 0.05f, 0.128f), helmetCentre + helmetTilt * new Vector3(dx, 0.0f, 0.142f), new Vector3(0.022f, 1, 0.012f));
        }
        // Mirrored wraparound sport glasses at eye level, below the helmet's brow: lens, black
        // top frame, and arms running back to the ears.
        var eyes = new Transform3D(headTilt, HeadPos + headTilt * new Vector3(0, -0.01f, 0));
        _upper.AddChild(new MeshInstance3D { Mesh = bm.Lens, MaterialOverride = _lensMat, Transform = eyes });
        _upper.AddChild(new MeshInstance3D { Mesh = bm.LensFrame, MaterialOverride = _rubberMat, Transform = eyes });
        foreach (int sd in new[] { -1, 1 })
        {
            var arm = new MeshInstance3D { Mesh = _box, MaterialOverride = _rubberMat };
            _upper.AddChild(arm);
            var hinge = eyes * BodyMeshes.LensPoint(sd * BodyMeshes.LensArc, 0.016f);
            var ear = eyes * new Vector3(sd * 0.09f, -0.008f, 0.04f);
            Orient(arm, hinge, ear, new Vector3(0.007f, 1, 0.014f));
        }

        for (int s = 0; s < 2; s++)
        {
            _deltoid[s] = Part(bm.Deltoid, _teeMat);
            // Bare legs: baggy shorts over the thigh, white sock on the shin.
            _thigh[s] = Part(bm.Thigh, _skinMat);
            Child(_thigh[s], bm.ShortsLeg, _shortsMat, Vector3.Zero, Vector3.One);
            _shin[s] = Part(bm.Shin, _shinMat);
            _shoe[s] = Part(bm.Shoe, _shoeMat);
            Child(_shoe[s], _box, _rubberMat, new Vector3(0, 0.14f, -0.04f), new Vector3(0.072f, 0.25f, 0.01f));   // sole
            Child(_shoe[s], _box, _rubberMat, new Vector3(0.03f, 0.12f, 0.035f), new Vector3(0.02f, 0.02f, 0.012f)); // dial
            // Bare arms with short tee sleeves; black short-finger gloves.
            _upperArm[s] = Part(bm.UpperArm, _skinMat);
            Child(_upperArm[s], bm.Sleeve, _teeMat, Vector3.Zero, Vector3.One);
            _forearm[s] = Part(bm.Forearm, _skinMat);
            _glove[s] = Part(bm.Glove, _rubberMat);
        }
    }

    /// <summary>Generated body meshes, shared by all avatars.</summary>
    sealed class BodyMeshes
    {
        public readonly Func<float, float> TorsoWidth = Meshes.Profile(-0.12f, 0, 0f, 0.175f, 0.17f, 0.142f, 0.32f, 0.17f, 0.41f, 0.185f, 0.48f, 0.14f, 0.54f, 0.08f, 0.59f, 0);
        public readonly Func<float, float> TorsoDepth = Meshes.Profile(-0.12f, 0, 0f, 0.125f, 0.17f, 0.11f, 0.32f, 0.135f, 0.41f, 0.12f, 0.48f, 0.095f, 0.54f, 0.068f, 0.59f, 0);
        public readonly ArrayMesh Torso, Neck, Head, Helmet, Lens, LensFrame, Thigh, ShortsLeg, Shin, UpperArm, Sleeve, Forearm, Glove, Shoe, Deltoid;

        public BodyMeshes()
        {
            Torso = Meshes.Lathe(TorsoWidth, TorsoDepth, -0.12f, 0.59f, 34, 20);
            Neck = Meshes.Limb(0.11f, 0.05f, 0.046f);
            Head = Meshes.Lathe(Meshes.Profile(-0.11f, 0, -0.06f, 0.07f, 0f, 0.083f, 0.06f, 0.078f, 0.115f, 0),
                                Meshes.Profile(-0.11f, 0, -0.06f, 0.085f, 0f, 0.1f, 0.06f, 0.092f, 0.115f, 0), -0.11f, 0.115f, 18, 18);
            // Rounded road-helmet shell (vertical axis): width rx, front-back depth rz, a touch
            // longer than the head so it wraps the back.
            Helmet = Meshes.Lathe(Meshes.Profile(-0.07f, 0, -0.055f, 0.112f, 0f, 0.124f, 0.06f, 0.112f, 0.105f, 0.075f, 0.135f, 0),
                                  Meshes.Profile(-0.07f, 0, -0.055f, 0.14f, 0f, 0.152f, 0.06f, 0.138f, 0.105f, 0.09f, 0.135f, 0), -0.07f, 0.135f, 24, 28);
            Lens = BuildLens(-0.024f, 0.02f, true);
            LensFrame = BuildLens(0.016f, 0.024f, false, 0.003f);
            Thigh = Meshes.Limb(RiderAvatar.Thigh, 0.086f, 0.062f, 0.35f, 0.01f, 1.06f);
            // Baggy shorts leg ending a hand's width above the knee.
            ShortsLeg = Meshes.Limb(0.33f, 0.104f, 0.088f, 0.4f, 0.006f, 1.05f);
            Shin = Meshes.Limb(RiderAvatar.Shin, 0.056f, 0.036f, 0.28f, 0.013f, 1.12f);
            UpperArm = Meshes.Limb(RiderAvatar.UpperArm, 0.054f, 0.042f, 0.45f, 0.006f);
            Sleeve = Meshes.Limb(0.14f, 0.07f, 0.064f, 0.5f, 0.002f);
            Forearm = Meshes.Limb(RiderAvatar.Forearm, 0.044f, 0.03f, 0.25f, 0.007f);
            // Shoulder cap that bridges torso and upper arm.
            Deltoid = Meshes.Limb(0.12f, 0.068f, 0.06f, 0.3f, 0.004f, 1.1f);
            Glove = Meshes.Limb(0.06f, 0.034f, 0.03f, 0.5f, 0.004f, 0.75f);
            Shoe = Meshes.Lathe(Meshes.Profile(0f, 0, 0.03f, 0.04f, 0.15f, 0.048f, 0.24f, 0.036f, 0.28f, 0),
                                Meshes.Profile(0f, 0, 0.03f, 0.045f, 0.12f, 0.04f, 0.24f, 0.025f, 0.28f, 0), 0f, 0.28f, 16, 14);
        }

        public const float LensArc = 1.38f;
        const float LensRx = 0.094f, LensRz = 0.11f;

        /// <summary>Point on the lens ellipse around the front of the face (−Z), head-local.</summary>
        public static Vector3 LensPoint(float a, float y, float grow = 0f) => new(MathF.Sin(a) * (LensRx + grow), y, -MathF.Cos(a) * (LensRz + grow));

        /// <summary>
        /// Wraparound band between heights y0 and y1 around the front of the face. With
        /// <paramref name="taper"/> the bottom edge rises towards the temples like a sport lens.
        /// </summary>
        static ArrayMesh BuildLens(float y0, float y1, bool taper, float grow = 0f)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            const int segs = 24;
            for (int i = 0; i < segs; i++)
            {
                float a0 = -LensArc + 2 * LensArc * i / segs, a1 = -LensArc + 2 * LensArc * (i + 1) / segs;
                Vector3 N(float a) => new Vector3(MathF.Sin(a) / LensRx, 0, -MathF.Cos(a) / LensRz).Normalized();
                float B(float a) => taper ? y0 + (y1 - y0) * 0.35f * MathF.Pow(MathF.Abs(a) / LensArc, 2f) : y0;
                Meshes.Quad(st, LensPoint(a0, B(a0), grow), LensPoint(a1, B(a1), grow), LensPoint(a1, y1, grow), LensPoint(a0, y1, grow), (N(a0) + N(a1)).Normalized());
            }
            return st.Commit();
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

            // Ankling: the foot pivots on the pedal spindle (ball of the foot) instead of staying
            // flat. Toe-down angle ≈ 5° pushing over 3 o'clock, 20° at the bottom, 28° pulling
            // back through 9 o'clock and 13° coming over the top; heels drop a bit when standing.
            float toeDown = Mathf.DegToRad(16.5f - 11.5f * MathF.Cos(ca) + 3.5f * MathF.Sin(ca) - 5f * _stand);
            var pitch = new Basis(Vector3.Right, -toeDown);
            _pedal[i].Transform = new Transform3D(pitch * Basis.FromScale(new Vector3(0.085f, 0.018f, 0.075f)), axle + new Vector3(side * 0.045f, 0, 0));

            // Shoe clipped to the pedal, ankle behind and above the axle.
            var foot = axle + new Vector3(side * 0.04f, 0, 0);
            var ankle = foot + pitch * new Vector3(0, 0.075f, 0.055f);
            var toe = foot + pitch * new Vector3(0, 0.025f, -0.12f);
            var heel = ankle + pitch * new Vector3(0, -0.035f, 0.05f);
            OrientFrom(_shoe[i], heel + pitch * new Vector3(0, -0.005f, 0.02f), toe, (toe - heel).Length() / 0.26f);

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
