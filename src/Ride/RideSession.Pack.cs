using System;
using System.Collections.Generic;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.UI;
using VeloGenesis.World;

namespace VeloGenesis.Ride;

/// <summary>
/// Draft packs: on a branch signposted DRAFT PACK a small bunch of AI riders rolls along at an
/// endurance pace. Close up behind them (or sit in) and the air drag drops, both in the virtual
/// physics and on the trainer. Once you've been sheltered a while the bunch may attack: hold the
/// wheel through the surge for a slipstream charge. In ERG the bunch rides at your pace instead.
/// The pack follows you through forks, but never onto a ghost-race branch.
/// </summary>
public partial class RideSession
{
    sealed class PackRider
    {
        public RiderAvatar Avatar;
        public float Back, Lane, Phase;
    }

    sealed class DraftPack
    {
        public Segment Seg;
        public float S, T, Power;
        public RiderPhysics Phys;
        public string Name;
        public Color Color;
        public readonly List<PackRider> Riders = new();
        public float Length, Shift = PackShiftBehind;
        public float DraftTime, SurgeLeft, NextSurgeAt = 45f;
        public int Surges;
        public bool SurgeFailed, Drafting, Caught;
    }

    public enum PackState { Chasing, Drafting, Front, Gone }

    const float PackSheltered = 0.62f;   // sitting in: 38% less air drag
    const float PackShiftBehind = 2.2f;
    const float PackReach = 12f;         // draft fades out over this many metres behind the last wheel
    public const float PackSurgeDuration = 25f;
    const float SurgeFactor = 1.3f;
    static readonly (float back, float lane)[] PackLayout = { (0f, -0.7f), (0f, -2.3f), (2.1f, -1.5f), (4.2f, -0.7f), (4.2f, -2.3f) };
    static readonly (string name, Color color)[] PackNames =
    {
        ("AMBER BUNCH", new Color(1f, 0.6f, 0.2f)), ("NIGHT TRAIN", new Color(0.6f, 0.4f, 1f)),
        ("CHROME CLUB", new Color(0.8f, 0.9f, 1f)), ("VAPOR CREW", new Color(1f, 0.4f, 0.75f)),
    };

    DraftPack _pack;
    int _packsCaught, _surgesHeld, _surgesTotal;
    float _draftSeconds;
    double _lastDraftToast = -100;

    /// <summary>Air-drag factor from the pack this frame (1 = no shelter).</summary>
    public float PackDraft { get; private set; } = 1f;
    public bool PackActive => _pack != null;
    public string PackName => _pack?.Name ?? "";
    public Color PackColor => _pack?.Color ?? Colors.White;
    public float PackPower => _pack == null ? 0 : _pack.Power * (_pack.SurgeLeft > 0 ? SurgeFactor : 1f);
    public PackState PackStatus { get; private set; } = PackState.Gone;
    /// <summary>Metres from the rider to the pack's last wheel (negative = inside the pack).</summary>
    public float PackGap { get; private set; }
    public float PackSurgeLeft => _pack?.SurgeLeft ?? 0;
    public bool PackSurgeFailed => _pack?.SurgeFailed ?? false;
    public Vector3? PackPosition => _pack?.Riders.Count > 0 ? _pack.Riders[0].Avatar.GlobalPosition : null;

    /// <summary>Debug: spawn a pack ahead on the current segment.</summary>
    public bool DebugPack;

    void MaybeSpawnPack(Segment seg, bool force = false)
    {
        if (_pack != null || Attract || _ghost != null) return;
        if (!force && seg.Feature != SegmentFeature.Pack) return;
        int ftp = Math.Max(Settings.Current.Ftp, 80);
        var (name, color) = PackNames[_rng.Next(PackNames.Length)];
        float pct = 0.62f + (float)_rng.NextDouble() * 0.16f;
        var p = new DraftPack
        {
            Seg = seg,
            S = Math.Min(World.DistInSeg + 60f, seg.Length - 5f),
            Power = MathF.Round(ftp * pct / 5f) * 5f,
            Phys = new RiderPhysics { Mass = Physics.Mass, Speed = Math.Max(Physics.Speed * 0.75f, 6f) },
            Name = name,
            Color = color,
        };
        // A mixed bunch: everyone in their own kit, men and women (at least one of each).
        var women = new bool[PackLayout.Length];
        for (int k = 0; k < women.Length; k++) women[k] = _rng.NextDouble() < 0.45;
        int woman = _rng.Next(women.Length);
        women[woman] = true;
        women[(woman + 1 + _rng.Next(women.Length - 1)) % women.Length] = false;
        for (int k = 0; k < PackLayout.Length; k++)
        {
            var (back, lane) = PackLayout[k];
            var av = new RiderAvatar { Look = RiderLook.Random(_rng, women[k]) };
            AddChild(av);
            av.SetColors(av.Look.Frame, color.Lightened(0.4f));
            av.AddTailLight(color);
            p.Riders.Add(new PackRider { Avatar = av, Back = back + (float)_rng.NextDouble() * 0.4f, Lane = lane, Phase = (float)_rng.NextDouble() * 10f });
            p.Length = Math.Max(p.Length, back);
        }
        _pack = p;
        Music.Synth.SfxPackSpotted();
        Hud?.Toast($"DRAFT PACK AHEAD  ·  {name}  ·  ~{p.Power:0} W  ·  catch the wheel", color);
        GD.Print($"Draft pack {name} at {p.Power} W on {seg.Name}");
    }

    /// <summary>Converts a distance on <paramref name="seg"/> to the rider's current segment, or null if unrelated.</summary>
    float? InRiderFrame(Segment seg, float s)
    {
        var cur = World.Current;
        if (seg == cur) return s;
        if (cur.Parent != null && seg == cur.Parent) return s - seg.Length;
        if (seg.Parent == cur) return cur.Length + s;
        return null;
    }

    void UpdatePack(float dt)
    {
        if (DebugPack && World.Current != null) { MaybeSpawnPack(World.Current, true); DebugPack = false; }
        PackDraft = 1f;
        var p = _pack;
        if (p == null) { PackStatus = PackState.Gone; return; }
        p.T += dt;
        bool erg = Config.Mode == RideMode.Erg;

        float riderS = World.DistInSeg;
        float? front0 = InRiderFrame(p.Seg, p.S);
        if (front0 == null) { EndPack(null); return; }

        WorldRoot.PoseOn(p.Seg, p.S, -1.5f, out _, out _, out float grade);
        if (erg && front0.Value - p.Length - riderS < PackReach && riderS - front0.Value < 3f)
        {
            // ERG fixes your power, so a bunch you've caught rolls along at your speed with you
            // tucked in near the back.
            float rel = riderS - (front0.Value - p.Length);
            p.Phys.Speed = Math.Max(0, Physics.Speed + Math.Clamp((rel - 0.5f) * 0.4f, -1.5f, 1.5f));
        }
        else
        {
            // Until you've caught them they cruise a little easier, so bridging across is quick.
            float power = p.Power * (p.SurgeLeft > 0 ? SurgeFactor : p.Caught ? 1f : 0.6f);
            p.Phys.Step(power * (1f + 0.03f * MathF.Sin(p.T * 0.4f)), grade, dt);
        }
        p.S += p.Phys.Speed * dt;

        // Through a fork: follow the rider's line, but leave ghost-race branches to the racers.
        while (p.S >= p.Seg.Length)
        {
            if (p.Seg.Children == null) { p.S = p.Seg.Length - 0.01f; break; }
            Segment next;
            if (p.Seg == World.Current) next = p.Seg.Children[Math.Clamp(World.Selection, 0, 1)];
            else if (World.Current.Parent == p.Seg) next = World.Current;
            else { EndPack(null); return; }
            if (next.Feature == SegmentFeature.Race) next = p.Seg.Children[0] == next ? p.Seg.Children[1] : p.Seg.Children[0];
            p.S -= p.Seg.Length;
            p.Seg = next;
        }

        float? front = InRiderFrame(p.Seg, p.S);
        if (front == null) { EndPack(null); return; }
        float tail = front.Value - p.Length;
        PackGap = tail - riderS;
        if (riderS - front.Value > 220f) { EndPack(true); return; }
        if (PackGap > 900f) { EndPack(false); return; }

        // Shelter: full inside the bunch, fading out over PackReach metres behind it, none on the front.
        float draft = 1f;
        if (riderS > front.Value + 0.5f) PackStatus = PackState.Front;
        else
        {
            draft = PackGap <= 1f ? PackSheltered : Mathf.Lerp(PackSheltered, 1f, Math.Clamp((PackGap - 1f) / (PackReach - 1f), 0f, 1f));
            PackStatus = draft < 0.9f ? PackState.Drafting : PackState.Chasing;
        }
        PackDraft = draft;
        Physics.DraftFactor = Math.Min(Physics.DraftFactor, draft);

        bool drafting = PackStatus == PackState.Drafting;
        if (drafting != p.Drafting)
        {
            p.Drafting = drafting;
            Music.Synth.SfxDraft(drafting);
            if (drafting && !p.Caught) { p.Caught = true; _packsCaught++; }
            if (_rideClock - _lastDraftToast > 12)
            {
                _lastDraftToast = _rideClock;
                Hud?.Toast(drafting ? $"IN THE DRAFT  ·  -{(1f - PackSheltered) * 100:0}% DRAG" : "OUT OF THE DRAFT", drafting ? UiTheme.Lime : UiTheme.Dim);
            }
        }
        if (drafting) { p.DraftTime += dt; _draftSeconds += dt; }

        UpdateSurge(p, dt, erg);
        PosePack(p, dt);
    }

    void UpdateSurge(DraftPack p, float dt, bool erg)
    {
        if (p.SurgeLeft > 0)
        {
            p.SurgeLeft -= dt;
            if (PackGap > PackReach + 3f || PackStatus == PackState.Gone) p.SurgeFailed = true;
            if (p.SurgeLeft <= 0)
            {
                p.SurgeLeft = 0;
                p.NextSurgeAt = p.DraftTime + 90f;
                if (!p.SurgeFailed)
                {
                    _surgesHeld++;
                    Music.Synth.SfxGel();
                    string reward = "";
                    if (Config.PowerGels && SlipCharges < MaxSlipCharges)
                    {
                        SlipCharges++;
                        reward = "  ·  +1 SLIPSTREAM CHARGE";
                    }
                    Hud?.Banner($"YOU HELD THE WHEEL{reward}", "SURGE SURVIVED", UiTheme.Lime);
                }
                else Hud?.Toast("DROPPED IN THE SURGE  ·  catch back on", UiTheme.Red);
            }
            return;
        }
        // Attacks only in free ride, and only once you've had a good spell of shelter.
        if (erg || p.Surges >= 2 || PackStatus != PackState.Drafting || p.DraftTime < p.NextSurgeAt) return;
        p.Surges++;
        _surgesTotal++;
        p.SurgeLeft = PackSurgeDuration;
        p.SurgeFailed = false;
        Music.Synth.SfxSurge();
        Hud?.Banner($"{p.Name} ATTACKS  ·  +{(SurgeFactor - 1f) * 100:0}% for {PackSurgeDuration:0} s", "HOLD THE WHEEL", p.Color);
    }

    void PosePack(DraftPack p, float dt)
    {
        // While you're behind them the bunch rides in your line so you sit right on a wheel; once
        // you come up inside it, it drifts left to let you alongside.
        p.Shift = Mathf.MoveToward(p.Shift, PackGap > 2.5f ? PackShiftBehind : 0f, dt * 1.2f);
        foreach (var r in p.Riders)
        {
            r.Phase += dt;
            float s = p.S - r.Back + 0.35f * MathF.Sin(r.Phase * 0.31f);
            var seg = p.Seg;
            if (s < 0 && seg.Parent != null) { s += seg.Parent.Length; seg = seg.Parent; }
            s = Math.Clamp(s, 0, seg.Length);
            float lane = r.Lane + p.Shift + 0.18f * MathF.Sin(r.Phase * 0.23f + r.Back);
            WorldRoot.PoseOn(seg, s, lane, out var pos, out var fwd, out _);
            r.Avatar.GlobalPosition = pos;
            r.Avatar.Basis = Basis.LookingAt(fwd, Vector3.Up);
            r.Avatar.Animate(p.Phys.Speed, 86f + 5f * MathF.Sin(r.Phase * 0.5f), dt);
        }
    }

    /// <summary>dropped: true = you rode away from them, false = never caught, null = parted at a fork.</summary>
    void EndPack(bool? dropped)
    {
        var p = _pack;
        if (p == null) return;
        _pack = null;
        PackStatus = PackState.Gone;
        PackDraft = 1f;
        foreach (var r in p.Riders)
            if (IsInstanceValid(r.Avatar)) r.Avatar.QueueFree();
        if (dropped == true && p.Caught) Hud?.Toast($"YOU RODE AWAY FROM {p.Name}", UiTheme.Lime);
        else if (dropped == null && p.Caught) Hud?.Toast($"{p.Name} TAKES THE OTHER ROAD", UiTheme.Dim);
    }
}
