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
    const float Thigh = 0.47f, Shin = 0.46f;
    static readonly Vector3 Pelvis = new(0, 1.05f, 0.195f);
    static readonly Vector3 Chest = new(0, 1.33f, -0.17f);
    /// <summary>Ball-of-foot bone above the pedal spindle (pedal, cleat and sole).</summary>
    const float BallAbovePedal = 0.035f;
    /// <summary>Wrist from the middle of the grip on the hoods or drops.</summary>
    static readonly Vector3 WristFromGrip = new(0, 0.04f, 0.06f);


    StandardMaterial3D _decalMat, _tailMat, _carbonMat, _steelMat, _spokeMat;
    ShaderMaterial _frameMat, _rubberMat, _tyreMat;
    Node3D _rig, _front, _rear, _spider;
    float _stand, _sprint;
    readonly MeshInstance3D[] _crankArm = new MeshInstance3D[2], _pedal = new MeshInstance3D[2];
    float _crank, _time;

    /// <summary>Build and kit; set before the avatar enters the tree (null = the player's look).</summary>
    public RiderLook Look;
    /// <summary>Debug (--female): the player rides as a woman.</summary>
    public static bool DebugFemale;

    static CylinderMesh _tube;
    static BoxMesh _box;
    static ArrayMesh _rimMesh, _rimDecal;
    static PackedScene _saddleScene, _handlebarScene;

    public override void _Ready()
    {
        _tube ??= new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 12, Rings = 1 };
        _box ??= new BoxMesh { Size = Vector3.One };
        var look = Look ?? new RiderLook { Female = DebugFemale };
        _rig = new Node3D { Scale = Vector3.One * look.Size };
        AddChild(_rig);

        // Bike: matte sage frame with a thin route-coloured pinstripe, black finishing kit,
        // deep carbon rims, tan-wall gravel tyres.
        _frameMat = new ShaderMaterial { Shader = Shaders.Paint };
        _frameMat.SetShaderParameter("color", look.Frame);
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

        // The rider's hair, shirt, shorts, shoes and glasses are fitted MakeHuman assets tinted per rider;
        // socks and gloves are painted on the body (see BuildBody).

        BuildBike();
        BuildRider(look);
        AddFillLights();
        Pose(0);
    }

    /// <summary>Render layer used only by the avatar, so its fill lights don't light the world.</summary>
    const uint AvatarLayer = 1u << 9;
    readonly System.Collections.Generic.List<OmniLight3D> _fill = new();

    /// <summary>
    /// Soft neutral key and fill that only reach the rider and bike, so the kit keeps its real
    /// colours (magenta tee, white shoes, sage frame) under the magenta sunset.
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
        _bodyMat.SetShaderParameter("rim_color", secondary);
    }

    /// <summary>Changes the tee colour (AI riders wear their club's colour).</summary>
    public void SetJersey(Color color)
    {
        foreach (var m in _shirtMats) m.AlbedoColor = color;
    }

    /// <summary>Bright rear light under the saddle so AI riders read from a long way back.</summary>
    public void AddTailLight(Color color)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.09f, 0.04f) },
            MaterialOverride = Meshes.Glow(color, 8f),
            Position = SeatCluster + new Vector3(0, -0.1f, 0.07f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
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

        // Cockpit: spacers, then the modelled stem, taped compact drop bar, hoods and levers
        // (tools/bike/make_handlebar.py; the bar follows the hand IK's hood and drop positions).
        Tube(HeadTop, SteererTop, 0.017f, _rubberMat);
        _handlebarScene ??= GD.Load<PackedScene>("res://assets/bike/handlebar.glb");
        var cockpit = _handlebarScene.Instantiate<Node3D>();
        cockpit.Position = BarClamp;
        _rig.AddChild(cockpit);
        foreach (var part in cockpit.GetChildren())
            if (part is MeshInstance3D mi)
                mi.MaterialOverride = mi.Name.ToString() is "Bar" or "Hoods" ? _rubberMat : _carbonMat;

        // Long exposed seatpost, slim saddle, small blinking tail light on the post.
        var clamp = SeatTubeTop + (SeatTubeTop - BB).Normalized() * 0.18f;
        Tube(SeatTubeTop + new Vector3(0, -0.02f, 0), clamp, 0.0135f, _rubberMat);
        // Saddle model (tools/bike/make_saddle.py): padded cover with a relief channel on steel rails.
        _saddleScene ??= GD.Load<PackedScene>("res://assets/bike/saddle.glb");
        var saddle = _saddleScene.Instantiate<Node3D>();
        saddle.Position = new Vector3(0, clamp.Y - 0.005f, clamp.Z - 0.02f);
        _rig.AddChild(saddle);
        var saddleMat = new StandardMaterial3D { AlbedoColor = new Color(0.025f, 0.025f, 0.028f), Roughness = 0.5f, Metallic = 0.05f };
        foreach (var part in saddle.GetChildren())
            if (part is MeshInstance3D mi) mi.MaterialOverride = mi.Name == "Rails" ? _steelMat : saddleMat;
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

    /// <summary>The rider: a rigged MakeHuman body with fitted hair, shoes, glasses and helmet (RiderAvatar.Body.cs).</summary>
    void BuildRider(RiderLook look) => BuildBody(look);

    // ---------------------------------------------------------------- animation    // ---------------------------------------------------------------- animation

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

        var balls = new Vector3[2];
        var pitches = new Basis[2];
        var wrists = new Vector3[2];
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
            pitches[i] = new Basis(Vector3.Right, -toeDown);
            _pedal[i].Transform = new Transform3D(pitches[i] * Basis.FromScale(new Vector3(0.085f, 0.018f, 0.075f)), axle + new Vector3(side * 0.045f, 0, 0));
            // Ball of the foot over the spindle, on top of the pedal and the shoe's sole.
            balls[i] = axle + new Vector3(side * 0.04f, 0, 0) + pitches[i] * new Vector3(0, BallAbovePedal, 0);

            var hood = BarClamp + new Vector3(side * HoodOffset.X, HoodOffset.Y, HoodOffset.Z);
            var drop = BarClamp + new Vector3(side * 0.236f, -0.12f, -0.035f);
            wrists[i] = hood.Lerp(drop, _sprint) + WristFromGrip;
        }

        // The model's spine is longer than the old torso, so aim it lower and further forward.
        PoseBody(pelvis + new Vector3(0, 0, -0.03f), chest + new Vector3(0, -0.08f, -0.1f), balls, pitches, wrists, 1f - _sprint);

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
    static void OrientFrom(Node3D m, Vector3 a, Vector3 b, float yScale = 1f, float widthScale = 1f)
    {
        var y = b - a;
        if (y.LengthSquared() < 1e-10f) return;
        y = y.Normalized();
        var x = Vector3.Right - y * y.X;
        if (x.LengthSquared() < 1e-3f) x = Vector3.Forward - y * y.Dot(Vector3.Forward);
        x = x.Normalized();
        m.Transform = new Transform3D(new Basis(x * widthScale, y * yScale, x.Cross(y) * widthScale), a);
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
