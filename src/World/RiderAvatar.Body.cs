using System;
using System.Collections.Generic;
using Godot;

namespace VeloGenesis.World;

/// <summary>
/// The rider's body: a MakeHuman mesh (assets/rider, made by tools/rider/export_rider.py) whose skeleton is
/// posed every frame from the bike's contact points – saddle, pedals and hoods – with two-bone IK using the
/// model's own bone lengths. The kit is painted into the mesh's vertex colours and coloured by
/// <see cref="Shaders.Body"/>.
/// </summary>
public partial class RiderAvatar
{
    static readonly Dictionary<bool, PackedScene> _bodyScenes = new();

    Node3D _model;
    Skeleton3D _skel;
    ShaderMaterial _bodyMat;
    readonly List<StandardMaterial3D> _shirtMats = new();
    /// <summary>Skeleton space → rig space (the model is turned to face −Z and scaled to the bike's leg length).</summary>
    Transform3D _skelToRig, _rigToSkel;
    Transform3D[] _rest, _posed;
    Vector3 _fwd, _up, _right;   // rig directions in skeleton space

    int _bPelvis, _bNeck, _bHead;
    readonly int[] _bSpine = new int[3];
    readonly int[] _bThigh = new int[2], _bCalf = new int[2], _bFoot = new int[2], _bBall = new int[2],
        _bUpperArm = new int[2], _bLowerArm = new int[2], _bHand = new int[2];
    readonly List<(int bone, float curl)> _fingers = new();
    float _thighLen, _calfLen, _upperArmLen, _lowerArmLen;
    Vector3[] _ankleFromBall = new Vector3[2];

    void BuildBody(RiderLook look)
    {
        if (!_bodyScenes.TryGetValue(look.Female, out var scene))
            _bodyScenes[look.Female] = scene = GD.Load<PackedScene>(look.Female ? "res://assets/rider/rider_female.glb" : "res://assets/rider/rider_male.glb");
        _model = scene.Instantiate<Node3D>();
        _rig.AddChild(_model);
        _skel = Find<Skeleton3D>(_model);

        _bodyMat = new ShaderMaterial { Shader = Shaders.Body };
        _bodyMat.SetShaderParameter("skin", look.Skin);
        _bodyMat.SetShaderParameter("shorts", look.Shorts);
        _bodyMat.SetShaderParameter("socks", look.Socks);
        // Hair textures are dark brown; scale them up so dyed colours read.
        var hairTint = new Color(Math.Min(1f, look.Hair.R * 4.5f), Math.Min(1f, look.Hair.G * 4.5f), Math.Min(1f, look.Hair.B * 4.5f));
        foreach (var mesh in _skel.GetChildren())
        {
            if (mesh is not MeshInstance3D mi) continue;
            switch (mi.Name.ToString())
            {
                case "Body": mi.MaterialOverride = _bodyMat; break;
                case "Shirt": _shirtMats.AddRange(Fabric(mi, look.Jersey)); break;
                case "Shorts": Fabric(mi, look.Shorts); break;
                case "Shoes": Tint(mi, look.Shoes, false); break;
                case "Hair": Tint(mi, hairTint, true); break;
                case "Eyebrows": Tint(mi, hairTint, true); break;
                case "Eyelashes": Tint(mi, Colors.White, true); break;
            }
        }

        int n = _skel.GetBoneCount();
        _rest = new Transform3D[n];
        _posed = new Transform3D[n];
        for (int i = 0; i < n; i++) _rest[i] = _skel.GetBoneGlobalRest(i);

        int B(string name)
        {
            int i = _skel.FindBone(name);
            if (i < 0) throw new InvalidOperationException($"Rider model has no bone '{name}'");
            return i;
        }
        _bPelvis = B("pelvis");
        _bSpine[0] = B("spine_01"); _bSpine[1] = B("spine_02"); _bSpine[2] = B("spine_03");
        _bNeck = B("neck_01");
        _bHead = B("head");
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "_l" : "_r";
            _bThigh[i] = B("thigh" + s); _bCalf[i] = B("calf" + s); _bFoot[i] = B("foot" + s); _bBall[i] = B("ball" + s);
            _bUpperArm[i] = B("upperarm" + s); _bLowerArm[i] = B("lowerarm" + s); _bHand[i] = B("hand" + s);
            foreach (var f in new[] { "index", "middle", "ring", "pinky" })
                for (int k = 1; k <= 3; k++) _fingers.Add((B($"{f}_0{k}{s}"), k == 1 ? 0.9f : 0.75f));
            _fingers.Add((B("thumb_01" + s), 0f));
            _fingers.Add((B("thumb_02" + s), 0.35f));
        }

        Vector3 Head(int b) => _rest[b].Origin;
        _thighLen = Head(_bCalf[0]).DistanceTo(Head(_bThigh[0]));
        _calfLen = Head(_bFoot[0]).DistanceTo(Head(_bCalf[0]));
        _upperArmLen = Head(_bLowerArm[0]).DistanceTo(Head(_bUpperArm[0]));
        _lowerArmLen = Head(_bHand[0]).DistanceTo(Head(_bLowerArm[0]));

        // glTF faces +Z; the rig faces −Z. Scale so the legs match the bike's design leg (Thigh + Shin).
        float scale = (Thigh + Shin) / (_thighLen + _calfLen);
        _model.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi).Scaled(Vector3.One * scale), Vector3.Zero);
        _skelToRig = RelativeTransform(_skel, _rig);
        _rigToSkel = _skelToRig.AffineInverse();
        _fwd = (_rigToSkel.Basis * Vector3.Forward).Normalized();
        _up = (_rigToSkel.Basis * Vector3.Up).Normalized();
        _right = (_rigToSkel.Basis * Vector3.Right).Normalized();
        for (int i = 0; i < 2; i++) _ankleFromBall[i] = Head(_bFoot[i]) - Head(_bBall[i]);
    }

    /// <summary>Colours an imported asset material (texture × tint); hair-like cards get alpha-tested.</summary>
    static void Tint(MeshInstance3D mi, Color tint, bool cards)
    {
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            if (mi.Mesh.SurfaceGetMaterial(i)?.Duplicate() is not BaseMaterial3D m) continue;
            m.AlbedoColor = tint;
            if (cards)
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaHash;
                m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }
            mi.SetSurfaceOverrideMaterial(i, m);
        }
    }

    static Texture2D _knit;

    /// <summary>
    /// Matte cloth for the shirt and shorts: the rider's colour, fully rough with little specular, a fine knit
    /// normal map tiled over the garment's UVs and a soft sheen at grazing angles, instead of the imported glossy
    /// finish (the assets' own textures are plain white and would tile along with the knit).
    /// </summary>
    static List<StandardMaterial3D> Fabric(MeshInstance3D mi, Color tint)
    {
        _knit ??= new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, AsNormalMap = true, BumpStrength = 6f,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.18f },
        };
        var mats = new List<StandardMaterial3D>();
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Roughness = 1f,
                Metallic = 0f,
                MetallicSpecular = 0.15f,
                NormalEnabled = true,
                NormalTexture = _knit,
                NormalScale = 0.25f,
                Uv1Scale = new Vector3(18f, 18f, 1f),
                RimEnabled = true,
                Rim = 0.35f,
                RimTint = 0.6f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, // the inside of the collar and sleeves shows
            };
            mi.SetSurfaceOverrideMaterial(i, m);
            mats.Add(m);
        }
        return mats;
    }

    static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T found) return found;
        return null;
    }

    static Transform3D RelativeTransform(Node3D node, Node3D ancestor)
    {
        var t = Transform3D.Identity;
        for (Node n = node; n != null && n != ancestor; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }

    /// <summary>Orthonormal frame with Y along <paramref name="axis"/> and X towards <paramref name="reference"/>.</summary>
    static Basis Frame(Vector3 axis, Vector3 reference)
    {
        var y = axis.Normalized();
        var x = (reference - y * reference.Dot(y)).Normalized();
        return new Basis(x, y, x.Cross(y));
    }

    Vector3 RestDir(int from, int to) => _rest[to].Origin - _rest[from].Origin;

    /// <summary>
    /// Poses a bone in skeleton space: its rest direction (towards <paramref name="restTo"/>) turns to
    /// <paramref name="dir"/>, and <paramref name="restRef"/> (a direction that was "sideways" for the bone at
    /// rest) turns towards <paramref name="reference"/>, which fixes the twist. The head stays attached to the parent.
    /// </summary>
    void Aim(int bone, int restTo, Vector3 restRef, Vector3 dir, Vector3 reference)
    {
        var rot = Frame(dir, reference) * Frame(RestDir(bone, restTo), restRef).Transposed();
        _posed[bone] = new Transform3D(rot * _rest[bone].Basis, Attached(bone));
        Commit(bone);
    }

    /// <summary>Where the bone's head is with its parent's current pose.</summary>
    Vector3 Attached(int bone)
    {
        int p = _skel.GetBoneParent(bone);
        return p < 0 ? _rest[bone].Origin : _posed[p] * _skel.GetBoneRest(bone).Origin;
    }

    /// <summary>Keeps the bone's rest pose relative to its (posed) parent, optionally bent by a local rotation.</summary>
    void Carry(int bone, Quaternion bend = default)
    {
        int p = _skel.GetBoneParent(bone);
        var local = _skel.GetBoneRest(bone);
        if (bend != default) local.Basis = local.Basis * new Basis(bend);
        _posed[bone] = p < 0 ? local : _posed[p] * local;
        Commit(bone);
    }

    void Commit(int bone)
    {
        int p = _skel.GetBoneParent(bone);
        var local = p < 0 ? _posed[bone] : _posed[p].AffineInverse() * _posed[bone];
        _skel.SetBonePosePosition(bone, local.Origin);
        _skel.SetBonePoseRotation(bone, local.Basis.GetRotationQuaternion());
    }

    Vector3 ToSkel(Vector3 rigPoint) => _rigToSkel * rigPoint;
    Vector3 DirToSkel(Vector3 rigDir) => (_rigToSkel.Basis * rigDir).Normalized();
    /// <summary>Rig-space position of a bone's head in the current pose.</summary>
    Vector3 RigPos(int bone) => _skelToRig * _posed[bone].Origin;

    /// <summary>
    /// Poses the whole body (rig space inputs): hip centre on the saddle, the base of the neck, the balls of the
    /// feet on the pedals with the foot pitch, and the wrists at the bars.
    /// </summary>
    void PoseBody(Vector3 hips, Vector3 neckBase, Vector3[] ballOfFoot, Basis[] footPitch, Vector3[] wrist, float headLift)
    {
        _skel.ResetBonePoses();
        for (int i = 0; i < _posed.Length; i++) _posed[i] = _rest[i];

        // Root bones keep their rest pose; the pelvis sits on the saddle, tipped forward with the spine.
        int root = _skel.GetBoneParent(_bPelvis);
        if (root >= 0) Carry(root);
        var hipsS = ToSkel(hips);
        var spineDir = (ToSkel(neckBase) - hipsS).Normalized();
        var pelvisDir = _up.Lerp(spineDir, 0.7f).Normalized();
        var rot = Frame(pelvisDir, _fwd) * Frame(RestDir(_bPelvis, _bSpine[0]), _fwd).Transposed();
        _posed[_bPelvis] = new Transform3D(rot * _rest[_bPelvis].Basis, hipsS);
        Commit(_bPelvis);

        // Spine: each vertebra a little further round towards the neck, ending slightly hunched.
        float[] bendAt = { 0.75f, 1.0f, 1.15f };
        int[] next = { _bSpine[1], _bSpine[2], _bNeck };
        for (int k = 0; k < 3; k++)
        {
            var d = pelvisDir.Lerp(spineDir, bendAt[k]).Normalized();
            Aim(_bSpine[k], next[k], _fwd, d, _fwd);
        }
        // Road-bike neck: it carries on from the upper back and only extends a little (≈20° above the
        // spine line, a bit more on the hoods than in the drops); the head tips back at the skull instead,
        // so the face looks ≈20° down the road.
        var neckDir = spineDir.Lerp(_up, 0.25f + 0.1f * headLift).Normalized();
        Aim(_bNeck, _bHead, _fwd, neckDir, _fwd);
        var headDir = _up.Lerp(_fwd, 0.3f).Normalized();
        _posed[_bHead] = new Transform3D(Frame(headDir, _fwd) * Frame(_up, _fwd).Transposed() * _rest[_bHead].Basis, Attached(_bHead));
        Commit(_bHead);

        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? -1 : 1;
            var outward = _right * side;

            // Legs: ankle placed so the ball of the foot is over the pedal spindle.
            var ball = ToSkel(ballOfFoot[i]);
            var pitch = _rigToSkel.Basis.Orthonormalized() * footPitch[i] * _skelToRig.Basis.Orthonormalized();
            var ankle = ball + pitch * _ankleFromBall[i];
            var hip = Attached(_bThigh[i]);
            var knee = Joint(hip, ankle, _thighLen, _calfLen, _fwd * 1f + outward * 0.12f + _up * 0.25f);
            var kneeOut = (knee - (hip + ankle) * 0.5f).Normalized();
            Aim(_bThigh[i], _bCalf[i], _fwd, knee - hip, kneeOut);
            Aim(_bCalf[i], _bFoot[i], _fwd, ankle - Attached(_bCalf[i]), kneeOut);
            Aim(_bFoot[i], _bBall[i], _up, ball - Attached(_bFoot[i]), pitch * _up);
            Carry(_bBall[i]);

            // Arms: shoulders follow the spine (clavicle at rest). On drop bars the elbows bend down and
            // back, tucked close to the body, only slightly out.
            // Shoulders roll forward towards the bars and lift a little (the trapezius then frames the
            // neck instead of leaving it long and bare).
            int clav = _skel.GetBoneParent(_bUpperArm[i]);
            var chestRot = _posed[_bSpine[2]].Basis * _rest[_bSpine[2]].Basis.Inverse();
            var clavDir = (chestRot * RestDir(clav, _bUpperArm[i])).Normalized() + chestRot * _fwd * 0.35f + chestRot * _up * 0.12f;
            Aim(clav, _bUpperArm[i], _fwd, clavDir, chestRot * _fwd);
            var shoulder = Attached(_bUpperArm[i]);
            var w = ToSkel(wrist[i]);
            var elbow = Joint(shoulder, w, _upperArmLen, _lowerArmLen, outward * 0.18f - _fwd * 0.45f - _up * 0.85f);
            var bend = (elbow - (shoulder + w) * 0.5f).Normalized();
            Aim(_bUpperArm[i], _bLowerArm[i], -_fwd, elbow - shoulder, bend);
            Aim(_bLowerArm[i], _bHand[i], -_fwd, w - Attached(_bLowerArm[i]), bend);
            // Hands along the hoods, knuckles up and out, fingers wrapped round the bar.
            int fingerRoot = _skel.FindBone(i == 0 ? "middle_01_l" : "middle_01_r");
            // Twist reference: the palm normal (down in MakeHuman's rest pose; the old "outward" reference ran
            // along the bone and let the hand roll). On the hoods the palm lies on top, tipped a little inward,
            // the hand points forward along the hood and the fingers wrap down round its outer side.
            Aim(_bHand[i], fingerRoot, -_up, _fwd * 1f - _up * 0.2f + outward * 0.08f, -_up - outward * 0.25f);
        }
        foreach (var (bone, curl) in _fingers)
            Carry(bone, new Quaternion(Vector3.Right, curl));
    }
}
