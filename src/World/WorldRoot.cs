using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Ride;

namespace VeloGenesis.World;

/// <summary>
/// Streams the procedural course around the rider: generates forks ahead on a worker thread,
/// builds their visuals, moves the rider along the chosen branch and frees what's left behind.
/// </summary>
public partial class WorldRoot : Node3D
{
    public const float ForkWindow = 250f;

    public RideConfig Config { get; private set; }
    public Quality Quality { get; private set; }
    CourseGenerator _gen;

    public Segment Current { get; private set; }
    public float DistInSeg { get; private set; }
    public float GlobalDist { get; private set; }
    public float GainSoFar { get; private set; }
    /// <summary>Child index (0 = left, 1 = right) the rider will take at the next fork.</summary>
    public int Selection;

    public Vector3 RiderPosition { get; private set; }
    public Vector3 RiderForward { get; private set; } = Vector3.Forward;
    public float RiderHeading { get; private set; }
    public float Grade { get; private set; }
    public readonly List<Vector2> Breadcrumbs = new();

    public event Action<Segment> SectorEntered;
    public event Action<Segment, Segment> ForkTaken;
    public event Action<Segment> GelCollected;
    public event Action<int> KmPassed;

    readonly List<Segment> _live = new();
    Task<(Segment[] kids, SegmentGeometry[] geo)> _pending;
    Segment _pendingFor;

    Godot.Environment _env;
    ShaderMaterial _skyMat;
    DirectionalLight3D _sun;
    MeshInstance3D _horizon;
    Vector3 _sunDir = new(0, 0.1f, -1);
    int _lastKm;

    static BoxMesh _pylonMesh, _beamMesh, _towerMesh;
    static ArrayMesh _palmMesh;
    static TorusMesh _tunnelMesh, _floatRingMesh;
    static CylinderMesh _beamCylinder;
    static ShaderMaterial _floaterMat, _buildingMat, _beamMat, _gelMat;
    static ArrayMesh _gelMesh;
    GpuParticles3D _sparks;

    static void EnsureSceneryResources()
    {
        if (_palmMesh != null) return;
        var trunk = new ShaderMaterial { Shader = Shaders.Palm };
        var frond = new ShaderMaterial { Shader = Shaders.Palm };
        frond.SetShaderParameter("frond", 1f);
        _palmMesh = Meshes.Palm(trunk, frond);
        _tunnelMesh = new TorusMesh { InnerRadius = 6.3f, OuterRadius = 6.55f, Rings = 64, RingSegments = 8 };
        _floatRingMesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 1f, Rings = 48, RingSegments = 8 };
        _beamCylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 1, CapTop = false, CapBottom = false };
        _floaterMat = new ShaderMaterial { Shader = Shaders.Floater };
        _buildingMat = new ShaderMaterial { Shader = Shaders.Building };
        _beamMat = new ShaderMaterial { Shader = Shaders.Beam };
        _gelMat = new ShaderMaterial { Shader = Shaders.Gel };
        _gelMat.SetShaderParameter("color", new Color(1f, 0.82f, 0.25f));
        // Energy-gel pouch: flat sachet with a narrow tear-off top.
        _gelMesh = Meshes.Lathe(
            Meshes.Profile(-0.5f, 0, -0.44f, 0.3f, 0.2f, 0.32f, 0.34f, 0.16f, 0.42f, 0.08f, 0.5f, 0),
            Meshes.Profile(-0.5f, 0, -0.44f, 0.07f, 0.2f, 0.09f, 0.34f, 0.06f, 0.42f, 0.04f, 0.5f, 0),
            -0.5f, 0.5f, 20, 16);
    }

    /// <summary>Drifting glowing specks around the rider that sell the sense of speed.</summary>
    void SetupSparks()
    {
        int amount = Quality.Name switch { "Low" => 0, "Medium" => 160, "Ultra" => 500, _ => 320 };
        if (amount == 0) return;
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(0.6f, 0.9f, 1f, 0.9f),
            VertexColorUseAsAlbedo = true,
        };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(45f, 14f, 70f),
            Gravity = Vector3.Zero,
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 1.2f,
            ScaleMin = 0.5f,
            ScaleMax = 1.6f,
            Color = new Color(0.7f, 0.9f, 1f),
        };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, 0.4f, 0.9f, 0f));
        ramp.SetColor(1, new Color(0.3f, 0.9f, 1f, 0f));
        ramp.AddPoint(0.2f, new Color(1f, 0.5f, 1f, 1f));
        ramp.AddPoint(0.8f, new Color(0.4f, 0.95f, 1f, 1f));
        process.ColorRamp = new GradientTexture1D { Gradient = ramp };
        _sparks = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = 5f,
            Preprocess = 5f,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.12f, 0.12f), Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-80, -40, -120), new Vector3(160, 80, 240)),
        };
        AddChild(_sparks);
    }

    public bool ForkAhead => Current?.Children != null && Current.Length - DistInSeg < ForkWindow;
    public float DistanceToFork => Current == null ? float.MaxValue : Current.Length - DistInSeg;

    public void Init(RideConfig cfg, Quality q)
    {
        Config = cfg;
        Quality = q;
        _gen = new CourseGenerator(cfg);
        _pylonMesh ??= new BoxMesh { Size = new Vector3(0.12f, 2.2f, 0.12f) };
        _beamMesh ??= new BoxMesh { Size = Vector3.One };
        _towerMesh ??= new BoxMesh { Size = Vector3.One };

        SetupEnvironment();

        var start = _gen.CreateStart();
        var geo = WorldBuilder.Build(start, null, q, _gen.WorldSeed);
        Attach(start, geo);
        Current = start;
        UpdatePose();
        SectorEntered?.Invoke(start);
    }

    void SetupEnvironment()
    {
        _skyMat = new ShaderMaterial { Shader = Shaders.Sky };
        var sky = new Sky { SkyMaterial = _skyMat, ProcessMode = Sky.ProcessModeEnum.Realtime, RadianceSize = Sky.RadianceSizeEnum.Size256 };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.3f, 0.14f, 0.45f),
            AmbientLightEnergy = 0.45f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            TonemapWhite = 6f,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 1.0f,
            GlowBloom = 0.03f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            FogEnabled = true,
            FogLightColor = new Color(0.07f, 0.01f, 0.13f),
            FogLightEnergy = 1f,
            FogDensity = Quality.FogDensity,
            FogSkyAffect = 0f,
            // Low-lying mist in the valleys below the road (height follows the rider).
            FogHeightDensity = 0.015f,
            SsrEnabled = Quality.Ssr,
            SsrMaxSteps = 96,
            SsrFadeIn = 0.1f,
            SsrFadeOut = 1.5f,
            SsrDepthTolerance = 0.4f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.15f,
            AdjustmentContrast = 1.05f,
        };
        if (Quality.VolumetricFog)
        {
            // Thin haze that the sun scatters through (shafts past the ridges) and the
            // pylon lights glow in.
            _env.VolumetricFogEnabled = true;
            _env.VolumetricFogDensity = 0.0012f;
            _env.VolumetricFogAlbedo = new Color(0.6f, 0.35f, 0.7f);
            _env.VolumetricFogEmission = new Color(0.008f, 0.001f, 0.016f);
            _env.VolumetricFogEmissionEnergy = 1f;
            _env.VolumetricFogAnisotropy = 0.5f;
            _env.VolumetricFogLength = 260f;
            _env.VolumetricFogDetailSpread = 2f;
            _env.VolumetricFogSkyAffect = 0f;
            _env.VolumetricFogAmbientInject = 0.05f;
            _env.VolumetricFogTemporalReprojectionEnabled = true;
        }
        float[] levels = { 0.9f, 1f, 0.75f, 0.5f, 0.3f, 0.18f, 0.1f };
        for (int i = 0; i < 7; i++) _env.SetGlowLevel(i, i < Quality.GlowLevels ? levels[i] : 0f);
        AddChild(new WorldEnvironment { Environment = _env });

        // Low warm sunset key light: rakes across the slopes facing the sun.
        _sun = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.5f, 0.5f),
            LightEnergy = 0.9f,
            // It shines from higher than the painted sky sun, so a full highlight would smear the
            // whole road; the puddles reflect the real sun disc from the sky instead.
            LightSpecular = 0.12f,
            LightVolumetricFogEnergy = 0.7f,
            ShadowEnabled = Quality.Shadows,
            ShadowBlur = 1.5f,
            DirectionalShadowMaxDistance = 260f,
        };
        AddChild(_sun);

        var horizonMat = new ShaderMaterial { Shader = Shaders.Terrain };
        horizonMat.SetShaderParameter("grid_size", 40f);
        horizonMat.SetShaderParameter("glow", 1.3f);
        horizonMat.SetShaderParameter("grid_color", new Color(0.6f, 0.15f, 0.9f));
        horizonMat.SetShaderParameter("solid", 0f);
        _horizon = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(9000, 9000) },
            MaterialOverride = horizonMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_horizon);
        SetupSparks();

        if (Quality.PostFx)
        {
            var post = new CanvasLayer { Layer = 0 };
            post.AddChild(new ColorRect
            {
                Material = new ShaderMaterial { Shader = Shaders.PostFx },
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorRight = 1, AnchorBottom = 1,
            });
            AddChild(post);
        }
    }

    void Attach(Segment seg, SegmentGeometry g)
    {
        var root = new Node3D { Name = $"Seg{seg.Id}" };
        AddChild(root);
        seg.Visual = root;
        _live.Add(seg);

        var roadMat = new ShaderMaterial { Shader = Shaders.Road };
        roadMat.SetShaderParameter("edge_color", seg.Primary);
        roadMat.SetShaderParameter("accent_color", seg.Secondary);
        root.AddChild(new MeshInstance3D { Mesh = g.Road.ToMesh(), MaterialOverride = roadMat });

        var wallMat = new ShaderMaterial { Shader = Shaders.Wall };
        wallMat.SetShaderParameter("color", seg.Primary);
        root.AddChild(new MeshInstance3D { Mesh = g.Walls.ToMesh(), MaterialOverride = wallMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var terrainMat = new ShaderMaterial { Shader = Shaders.Terrain };
        terrainMat.SetShaderParameter("grid_color", seg.Primary);
        terrainMat.SetShaderParameter("accent_color", seg.Secondary);
        root.AddChild(new MeshInstance3D { Mesh = g.Terrain.ToMesh(), MaterialOverride = terrainMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        root.AddChild(MultiMeshNode(_pylonMesh, Meshes.Glow(seg.Primary, 2.2f), g.Pylons, null));
        AddPylonLights(root, g.Pylons, seg.Primary);

        // Rock formations share the terrain grid material so they belong to the landscape.
        root.AddChild(MultiMeshNode(Meshes.Rock, terrainMat, g.Rocks.ConvertAll(c => c.xf), null));
        var towers = MultiMeshNode(Meshes.Rock, terrainMat, g.Towers.ConvertAll(c => c.xf), null);
        root.AddChild(towers);
        for (int i = 0; i < g.Towers.Count; i++)
            seg.Blockers.Add((towers.Multimesh, i, new Vector2(g.Towers[i].xf.Origin.X, g.Towers[i].xf.Origin.Z), 22f));

        // Extra scenery
        EnsureSceneryResources();
        if (g.Palms.Count > 0) root.AddChild(MultiMeshNode(_palmMesh, null, g.Palms.ConvertAll(c => c.xf), g.Palms.ConvertAll(c => c.color)));
        if (g.TunnelRings.Count > 0) root.AddChild(MultiMeshNode(_tunnelMesh, Meshes.Glow(seg.Primary, 2.6f), g.TunnelRings, null));
        if (g.Floaters.Count > 0) root.AddChild(MultiMeshNode(Meshes.Octahedron, _floaterMat, g.Floaters.ConvertAll(c => c.xf), g.Floaters.ConvertAll(c => c.color)));
        if (g.FloatRings.Count > 0) root.AddChild(MultiMeshNode(_floatRingMesh, _floaterMat, g.FloatRings.ConvertAll(c => c.xf), g.FloatRings.ConvertAll(c => c.color)));
        if (g.Buildings.Count > 0)
        {
            var node = MultiMeshNode(_towerMesh, _buildingMat, g.Buildings.ConvertAll(c => c.xf), g.Buildings.ConvertAll(c => c.color));
            root.AddChild(node);
            for (int i = 0; i < g.Buildings.Count; i++)
                seg.Blockers.Add((node.Multimesh, i, new Vector2(g.Buildings[i].xf.Origin.X, g.Buildings[i].xf.Origin.Z), 400f));
        }
        if (g.Beams.Count > 0) root.AddChild(MultiMeshNode(_beamCylinder, _beamMat, g.Beams.ConvertAll(c => c.xf), g.Beams.ConvertAll(c => c.color)));

        var gelNode = MultiMeshNode(_gelMesh, _gelMat, g.Gels.ConvertAll(t => t.ScaledLocal(Vector3.One * 1.5f)), null);
        seg.GelMesh = gelNode.Multimesh;
        root.AddChild(gelNode);

        // Big scenery can't be checked against road that doesn't exist yet, so whenever a new
        // segment arrives, remove anything that would end up on or next to any live road.
        RemoveBlockers(seg);

        foreach (var (xf, km) in g.Gates) root.AddChild(MakeGate(xf, km, seg.Secondary));
    }

    /// <summary>Real lights on some of the pylons so the neon spills onto the wet road and the rider.</summary>
    void AddPylonLights(Node3D root, List<Transform3D> pylons, Color color)
    {
        if (Quality.PylonLightSpacing <= 0) return;
        // Pylons come in left/right pairs every 40 m; light every Nth pair, alternating sides.
        int step = Math.Max(1, (int)(Quality.PylonLightSpacing / 40f));
        for (int pair = 0, k = 0; pair * 2 + 1 < pylons.Count; pair += step, k++)
        {
            var xf = pylons[pair * 2 + (k & 1)];
            root.AddChild(PylonLight(xf.Origin + Vector3.Up * 1.0f, color));
        }
    }

    static OmniLight3D PylonLight(Vector3 pos, Color color) => new()
    {
        Position = pos,
        LightColor = color,
        LightEnergy = 4f,
        LightVolumetricFogEnergy = 0.6f,
        LightSpecular = 1f,
        OmniRange = 13f,
        OmniAttenuation = 1.2f,
        ShadowEnabled = false,
        DistanceFadeEnabled = true,
        DistanceFadeBegin = 200f,
        DistanceFadeLength = 60f,
    };

    void RemoveBlockers(Segment added)
    {
        static void Hide(MultiMesh mm, int i) => mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * 0.0001f), new Vector3(0, -10000, 0)));
        static bool Near(Segment road, Vector2 p, float clear)
        {
            float c2 = clear * clear;
            for (int k = 0; k < road.Count; k += 10)
                if (new Vector2(road.Pos[k].X - p.X, road.Pos[k].Z - p.Y).LengthSquared() < c2) return true;
            return false;
        }

        foreach (var s in _live)
        {
            if (s.Disposed) continue;
            // Existing scenery vs the new road, and the new segment's scenery vs existing roads.
            foreach (var b in s.Blockers)
                if (Near(added, b.pos, b.clear)) Hide(b.mm, b.index);
            if (s != added)
                foreach (var b in added.Blockers)
                    if (Near(s, b.pos, b.clear)) Hide(b.mm, b.index);
        }
    }

    static MultiMeshInstance3D MultiMeshNode(Mesh mesh, Material mat, List<Transform3D> xfs, List<Color> colors)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors != null,
            Mesh = mesh,
            InstanceCount = xfs.Count,
        };
        for (int i = 0; i < xfs.Count; i++)
        {
            mm.SetInstanceTransform(i, xfs[i]);
            if (colors != null) mm.SetInstanceColor(i, colors[i]);
        }
        return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    Node3D MakeGate(Transform3D xf, int km, Color color) => MakeGate(xf, $"{km} KM", color);

    Node3D MakeGate(Transform3D xf, string text, Color color)
    {
        var gate = new Node3D { Transform = xf };
        var mat = Meshes.Glow(color, 2.5f);
        float half = WorldBuilder.RoadHalfWidth + 1.2f;
        const float height = 6.5f;
        foreach (int side in new[] { -1, 1 })
            gate.AddChild(new MeshInstance3D { Mesh = _beamMesh, MaterialOverride = mat, Position = new Vector3(side * half, height * 0.5f, 0), Scale = new Vector3(0.25f, height, 0.25f) });
        gate.AddChild(new MeshInstance3D { Mesh = _beamMesh, MaterialOverride = mat, Position = new Vector3(0, height, 0), Scale = new Vector3(half * 2 + 0.25f, 0.25f, 0.25f) });
        if (Quality.PylonLightSpacing > 0)
        {
            var light = PylonLight(new Vector3(0, height - 0.8f, 0), color);
            light.OmniRange = 16f;
            light.LightEnergy = 3f;
            gate.AddChild(light);
        }
        // Sign panel hung under the top beam.
        var board = HoloBoard.Build(color, new[] { new HoloBoard.Line(text, 150, HoloBoard.Glow(color.Lightened(0.55f), 1.6f)) }, HorizontalAlignment.Center, 3.2f);
        board.Position = new Vector3(0, height - 0.25f - HoloBoard.SizeOf(board).Y, 0);
        gate.AddChild(board);
        // Short hangers from the beam to the panel corners.
        float hw = HoloBoard.SizeOf(board).X * 0.5f - 0.6f;
        foreach (int side in new[] { -1, 1 })
            gate.AddChild(new MeshInstance3D { Mesh = _beamMesh, MaterialOverride = mat, Position = new Vector3(side * hw, height - 0.2f, 0), Scale = new Vector3(0.06f, 0.3f, 0.06f) });
        return gate;
    }

    /// <summary>Adds a finish arch on the current segment (for ghost races). Free it when done.</summary>
    public Node3D AddFinishArch(float s, Color color)
    {
        if (Current?.Visual == null) return null;
        Current.Sample(s, out var p, out float h, out _);
        var arch = MakeGate(new Transform3D(Basis.FromEuler(new Vector3(0, -h, 0)), p), "FINISH", color);
        Current.Visual.AddChild(arch);
        return arch;
    }

    /// <summary>Position, travel direction and gradient at a distance along the current segment, in a lane.</summary>
    public void PoseOnCurrent(float s, float lane, out Vector3 pos, out Vector3 forward, out float grade)
    {
        Current.Sample(s, out var p, out float h, out grade);
        Current.Sample(Math.Min(s + 2f, Current.Length), out var ahead, out _, out _);
        pos = p + Segment.RightOf(h) * lane;
        var f = ahead - p;
        forward = f.LengthSquared() > 1e-4f ? f.Normalized() : Segment.ForwardOf(h);
    }

    void AddForkSigns(Segment parent)
    {
        if (parent.Children == null) return;
        // Each sign stands on the outside of its own branch, far enough past the split that the
        // two branches (and signs) have already separated.
        foreach (var c in parent.Children)
        {
            if (c.Visual == null) continue;
            c.Sample(Math.Min(130f, c.Length), out var p, out float h, out _);
            string dir = c.Side < 0 ? "◀  LEFT" : "RIGHT  ▶";
            string tag = c.IsHarder ? "PUSH ▲" : "EASE ▼";
            var accent = c.Primary;
            var label = HoloBoard.Build(accent, new[]
            {
                new HoloBoard.Line($"{dir}   ·   {tag}", 72, HoloBoard.Glow(UI.UiTheme.CaptionColor, 1.2f), 0.18f),
                new HoloBoard.Line(c.Name.ToUpper(), 180, HoloBoard.Glow(accent.Lightened(0.45f), 1.5f), 0.2f),
                new HoloBoard.Line($"{c.Length / 1000f:0.0} km   ·   +{c.Gain:0} m   ·   max {c.MaxGrade:0}%", 78, new Color(0.85f, 0.88f, 1f)),
            });
            var size = HoloBoard.SizeOf(label);
            // Stand beside the branch, facing the oncoming rider and angled slightly towards the road.
            var ground = p + Segment.RightOf(h) * c.Side * (WorldBuilder.RoadHalfWidth + 4f + size.X * 0.5f);
            var sign = new Node3D { Transform = new Transform3D(Basis.FromEuler(new Vector3(0, -h, 0)) * Basis.FromEuler(new Vector3(0, -c.Side * 0.35f, 0)), ground) };
            label.Position = new Vector3(0, 4.2f, 0);
            sign.AddChild(label);
            var postMat = Meshes.Glow(accent, 2.2f);
            foreach (int sx in new[] { -1, 1 })
                sign.AddChild(new MeshInstance3D { Mesh = _beamMesh, MaterialOverride = postMat, Position = new Vector3(sx * (size.X * 0.5f - 1.2f), 1.6f, -0.08f), Scale = new Vector3(0.18f, 5.4f, 0.18f) });
            c.Visual.AddChild(sign);
        }
    }

    public override void _Process(double delta)
    {
        if (Current == null) return;

        // Kick off generation of the next fork when it comes into view distance.
        if (Current.Children == null && _pending == null && Current.Length - DistInSeg < Quality.ViewAhead)
        {
            var parent = Current;
            float gain = GainSoFar + Math.Max(0, parent.Gain * (1f - DistInSeg / parent.Length));
            var q = Quality;
            int seed = _gen.WorldSeed;
            _pendingFor = parent;
            _pending = Task.Run(() =>
            {
                var kids = _gen.CreateFork(parent, gain);
                var geo = new[]
                {
                    WorldBuilder.Build(kids[0], kids[1], q, seed),
                    WorldBuilder.Build(kids[1], kids[0], q, seed),
                };
                return (kids, geo);
            });
        }

        if (_pending != null && _pending.IsCompleted)
        {
            if (_pending.IsFaulted) GD.PrintErr("Course generation failed: " + _pending.Exception);
            else
            {
                var (kids, geo) = _pending.Result;
                for (int i = 0; i < kids.Length; i++) Attach(kids[i], geo[i]);
                _pendingFor.Children = kids;
                AddForkSigns(_pendingFor);
                if (Selection < 0 || Selection > 1) Selection = 0;
            }
            _pending = null;
            _pendingFor = null;
        }

        CullSegments();
        UpdateSky((float)delta);
        _gelGlow = Mathf.MoveToward(_gelGlow, GelsActive ? 1f : 0f, (float)delta * 4f);
        _gelMat?.SetShaderParameter("active", _gelGlow);
    }

    void CullSegments()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var s = _live[i];
            bool keep = s == Current
                || s.Parent == Current
                || (s == Current.Parent && DistInSeg < 500f)
                || (Current.Parent != null && s.Parent == Current.Parent && DistInSeg < 350f);
            if (keep) continue;
            s.Visual?.QueueFree();
            s.Visual = null;
            s.Disposed = true;
            _live.RemoveAt(i);
        }
    }

    void UpdateSky(float dt)
    {
        // The sun drifts to stay roughly ahead of the rider.
        var target = new Vector3(RiderForward.X, 0, RiderForward.Z).Normalized();
        var flat = new Vector3(_sunDir.X, 0, _sunDir.Z).Normalized();
        flat = flat.Slerp(target, 1f - MathF.Exp(-dt / 25f)).Normalized();
        _sunDir = new Vector3(flat.X, 0.09f, flat.Z);
        _skyMat.SetShaderParameter("sun_dir", _sunDir);
        var lightDir = new Vector3(flat.X, 0.45f, flat.Z).Normalized();
        _sun.Basis = Basis.LookingAt(-lightDir, Vector3.Up);
        _horizon.GlobalPosition = new Vector3(Mathf.Snapped(RiderPosition.X, 40f), RiderPosition.Y - 70f, Mathf.Snapped(RiderPosition.Z, 40f));
        _env.FogHeight = RiderPosition.Y - 6f;
        // Ringed planet hangs well away from the sun.
        var planetFlat = flat.Rotated(Vector3.Up, 2.1f);
        _skyMat.SetShaderParameter("planet_dir", new Vector3(planetFlat.X, 0.33f, planetFlat.Z));
        if (_sparks != null) _sparks.GlobalPosition = RiderPosition + RiderForward * 25f + Vector3.Up * 6f;
    }

    /// <summary>Moves the rider forward along the course.</summary>
    public void Advance(float metres)
    {
        if (Current == null || metres <= 0) return;
        float prevElev = RiderPosition.Y;
        float prevS = DistInSeg;
        DistInSeg += metres;
        GlobalDist += metres;

        CheckGels(Current, prevS, DistInSeg);

        while (DistInSeg >= Current.Length)
        {
            if (Current.Children == null)
            {
                // Generation hasn't caught up (very slow machine) – hold at the fork.
                GlobalDist -= DistInSeg - (Current.Length - 0.01f);
                DistInSeg = Current.Length - 0.01f;
                break;
            }
            float overflow = DistInSeg - Current.Length;
            int sel = Math.Clamp(Selection, 0, 1);
            var chosen = Current.Children[sel];
            var other = Current.Children[1 - sel];
            Current = chosen;
            DistInSeg = overflow;
            Selection = 0;
            ForkTaken?.Invoke(chosen, other);
            SectorEntered?.Invoke(chosen);
            CheckGels(Current, 0, DistInSeg);
        }

        UpdatePose();
        float d = RiderPosition.Y - prevElev;
        if (d > 0) GainSoFar += d;

        int km = (int)(GlobalDist / 1000f);
        if (km > _lastKm) { _lastKm = km; KmPassed?.Invoke(km); }

        if (Breadcrumbs.Count == 0 || new Vector2(RiderPosition.X, RiderPosition.Z).DistanceTo(Breadcrumbs[^1]) > 10f)
        {
            Breadcrumbs.Add(new Vector2(RiderPosition.X, RiderPosition.Z));
            if (Breadcrumbs.Count > 600) Breadcrumbs.RemoveAt(0);
        }
    }

    void CheckGels(Segment seg, float from, float to)
    {
        for (int i = 0; i < seg.Gels.Length; i++)
        {
            if (seg.GelTaken[i] || seg.Gels[i] < from || seg.Gels[i] > to) continue;
            seg.GelTaken[i] = true;
            seg.GelMesh?.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.0001f), Vector3.Zero));
            if (GelsActive) GelCollected?.Invoke(seg);
            else GelMissed?.Invoke(seg);
        }
    }

    /// <summary>True while the rider is in the gel zone (Z4+): gels glow and can be grabbed.</summary>
    public bool GelsActive = true;
    float _gelGlow = 1f;
    public event Action<Segment> GelMissed;

    /// <summary>Distance to the next gel still on the road ahead (current segment), or -1.</summary>
    public float NextGelDistance
    {
        get
        {
            if (Current == null) return -1;
            for (int i = 0; i < Current.Gels.Length; i++)
                if (!Current.GelTaken[i] && Current.Gels[i] > DistInSeg) return Current.Gels[i] - DistInSeg;
            return -1;
        }
    }

    void UpdatePose()
    {
        Current.Sample(DistInSeg, out var p, out float h, out float g);
        Current.Sample(Math.Min(DistInSeg + 2f, Current.Length), out var ahead, out _, out _);
        RiderHeading = h;
        Grade = g;
        var lane = Segment.RightOf(h) * WorldBuilder.RiderLane;
        RiderPosition = p + lane;
        var fwd = ahead - p;
        RiderForward = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Segment.ForwardOf(h);
    }

    /// <summary>Samples the road ahead: (distance from rider, elevation) pairs along the selected route.</summary>
    public List<Vector2> ProfileAhead(float metres, float step)
    {
        var pts = new List<Vector2>();
        var seg = Current;
        float s = DistInSeg;
        float d = 0;
        while (d <= metres && seg != null)
        {
            if (s > seg.Length)
            {
                if (seg.Children == null) break;
                s -= seg.Length;
                seg = seg.Children[seg == Current ? Math.Clamp(Selection, 0, 1) : (seg.Children[0].IsPlan ? 0 : 1)];
                continue;
            }
            pts.Add(new Vector2(d, seg.ElevationAt(s)));
            s += step;
            d += step;
        }
        return pts;
    }

    /// <summary>Plan-view polyline of a segment, decimated.</summary>
    public static List<Vector2> Outline(Segment seg, float from, float to, float step)
    {
        var pts = new List<Vector2>();
        for (float s = Math.Max(0, from); s <= Math.Min(seg.Length, to); s += step)
        {
            seg.Sample(s, out var p, out _, out _);
            pts.Add(new Vector2(p.X, p.Z));
        }
        return pts;
    }
}
