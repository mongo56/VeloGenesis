using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.UI;
using VeloGenesis.World;

namespace VeloGenesis.Ride;

/// <summary>
/// Ghost races: on a branch signposted GHOST RACE (free ride), a holographic rival appears alongside
/// and challenges you to a finish arch further along the segment. The ghost rides the same terrain
/// with the same physics, either at a steady power derived from your FTP or replaying one of your
/// own best efforts (all-time or from your last ride) second by second.
/// </summary>
public partial class RideSession
{
    sealed class GhostRacer
    {
        public Segment Seg;
        public RiderPhysics Phys;
        public RiderAvatar Avatar;
        public Node3D Arch;
        public string Name;
        public float Power, S, Start, Finish, T;
        public Effort Replay;
        public Color Color;
        public int Lead;
        public bool FinalCalled;
    }

    const float GhostLane = -WorldBuilder.RiderLane;
    static readonly Color GhostColor = new(0.55f, 0.95f, 1f);
    static readonly Color SelfGhostColor = new(1f, 0.45f, 0.85f);
    const float SelfGhostChance = 0.45f;

    static readonly (string name, float pct)[] GhostTiers =
    {
        ("ROOKIE", 0.85f), ("CHASER", 0.93f), ("RIVAL", 1.0f), ("ELITE", 1.08f),
    };

    GhostRacer _ghost;
    int _racesWon, _racesLost;

    /// <summary>Debug: start a race on the current segment immediately.</summary>
    public bool DebugGhost;

    public bool RaceActive => _ghost != null;
    public string RaceGhostName => _ghost?.Name ?? "";
    public float RaceGhostPower => _ghost?.Power ?? 0;
    /// <summary>Metres ahead of the ghost (negative = behind).</summary>
    public float RaceGap => _ghost == null ? 0 : World.DistInSeg - _ghost.S;
    public float RaceToFinish => _ghost == null ? 0 : _ghost.Finish - World.DistInSeg;
    public float RaceLength => _ghost == null ? 0 : _ghost.Finish - _ghost.Start;
    public Vector3? GhostPosition => _ghost?.Avatar?.GlobalPosition;

    bool MaybeStartGhostRace(Segment seg, bool force = false)
    {
        if (_ghost != null || Attract) return false;
        if (!force)
        {
            if (Config.Mode != RideMode.FreeRide || !Config.GhostRaces || seg.Feature != SegmentFeature.Race) return false;
        }

        // Finish well before the next fork window so the panels never overlap.
        float available = seg.Length - World.DistInSeg - WorldRoot.ForkWindow - 60f;
        float length = Math.Min(available, 700f + (float)_rng.NextDouble() * 1100f);
        if (length < 400f) return false;

        int ftp = Math.Max(Settings.Current.Ftp, 80);
        var tier = GhostTiers[_rng.Next(GhostTiers.Length)];
        // Short races are fought harder than long ones.
        float pct = tier.pct * (1f + Math.Clamp((1200f - length) / 1200f, -0.4f, 0.4f) * 0.08f);
        float power = MathF.Round(ftp * pct / 5f) * 5f;
        string name = $"GHOST {tier.name}";
        var color = GhostColor;

        // Sometimes the rival is you: one of your own best efforts long enough to cover the race.
        Effort replay = null;
        bool allTime = false;
        if (_rng.NextDouble() < SelfGhostChance || DebugSelfGhost)
        {
            int est = (int)EstimateRaceSeconds(seg, World.DistInSeg, length, ftp);
            allTime = _rng.NextDouble() < 0.5;
            replay = PersonalBests.Current.For(est, allTime) ?? PersonalBests.Current.For(est, !allTime);
            if (replay != null) allTime = replay == PersonalBests.Current.For(est, true);
        }
        if (replay != null)
        {
            power = MathF.Round(replay.Watts);
            pct = power / ftp;
            name = allTime ? $"YOUR BEST {PersonalBests.DurationName(replay.Seconds)}" : "YOUR LAST RIDE";
            color = SelfGhostColor;
        }

        var avatar = new RiderAvatar();
        AddChild(avatar);
        avatar.MakeGhost(color);

        _ghost = new GhostRacer
        {
            Seg = seg,
            Phys = new RiderPhysics { Mass = Physics.Mass, Speed = Math.Max(Physics.Speed, 3f) },
            Avatar = avatar,
            Name = name,
            Power = power,
            Replay = replay,
            Color = color,
            S = World.DistInSeg,
            Start = World.DistInSeg,
            Finish = World.DistInSeg + length,
        };
        _ghost.Arch = World.AddFinishArch(_ghost.Finish, color);

        Music.Synth.SfxRaceStart();
        string from = replay == null ? $"{pct * 100:0}% FTP" : $"{replay.Date.ToLocalTime():d MMM}  ·  {PersonalBests.DurationName(replay.Seconds)} effort";
        Hud?.Banner($"CHALLENGE  ·  {power:0} W ({from})  ·  {length / 1000f:0.0} km", $"BEAT {_ghost.Name}", color);
        GD.Print($"Ghost race: {_ghost.Name} at {power} W over {length:0} m on {seg.Name}");
        return true;
    }

    /// <summary>Debug: prefer a replay of your own best effort when a race starts.</summary>
    public bool DebugSelfGhost;

    /// <summary>Rough time to ride the race at about threshold, simulated over the actual road.</summary>
    float EstimateRaceSeconds(Segment seg, float from, float length, int ftp)
    {
        var phys = new RiderPhysics { Mass = Physics.Mass, Speed = Math.Max(Physics.Speed, 5f) };
        float s = from, t = 0;
        while (s < from + length && t < 1200f)
        {
            seg.Sample(s, out _, out _, out float grade);
            phys.Step(ftp * 0.95f, grade, 0.5f);
            s += phys.Speed * 0.5f;
            t += 0.5f;
        }
        return t;
    }

    /// <summary>The ghost's power right now: its recorded effort when replaying one, else steady.</summary>
    static float GhostPower(GhostRacer g, float grade)
    {
        if (g.Replay != null && g.Replay.Trace.Length > 2)
        {
            var tr = g.Replay.Trace;
            if (g.T >= tr.Length - 2) return g.Replay.Watts;
            // Three-second average, interpolated, smooths the trainer's second-to-second noise.
            float At(int i) => (tr[Math.Max(i - 1, 0)] + tr[i] + tr[Math.Min(i + 1, tr.Length - 1)]) / 3f;
            int k = (int)g.T;
            return Mathf.Lerp(At(k), At(k + 1), g.T - k);
        }
        // Steady effort with a little human-like variation; digs in on steeper ramps.
        float surge = 1f + 0.04f * MathF.Sin(g.T * 0.7f) + 0.02f * MathF.Sin(g.T * 2.3f) + Math.Clamp(grade, 0f, 10f) * 0.006f;
        return g.Power * surge;
    }

    void UpdateGhostRace(float dt)
    {
        // Debug / trailer request: keep trying until there's enough road for a race.
        if (DebugGhost && World.Current != null && MaybeStartGhostRace(World.Current, true)) DebugGhost = false;
        var g = _ghost;
        if (g == null) return;
        if (World.Current != g.Seg) { EndGhostRace(null); return; }

        g.T += dt;
        World.PoseOnCurrent(g.S, GhostLane, out _, out _, out float grade);
        g.Phys.Step(GhostPower(g, grade), grade, dt);
        g.S = Math.Min(g.S + g.Phys.Speed * dt, g.Seg.Length - 1f);

        World.PoseOnCurrent(g.S, GhostLane, out var pos, out var fwd, out _);
        g.Avatar.GlobalPosition = pos;
        g.Avatar.Basis = Basis.LookingAt(fwd, Vector3.Up);
        g.Avatar.Animate(g.Phys.Speed, 88f + 4f * MathF.Sin(g.T * 0.5f), dt);

        // Audio cues: overtakes either way (with a little hysteresis) and the last 200 m.
        float gap = World.DistInSeg - g.S;
        int lead = gap > 2f ? 1 : gap < -2f ? -1 : g.Lead;
        if (g.Lead != 0 && lead != g.Lead)
        {
            Music.Synth.SfxOvertake(lead > 0);
            Hud?.Toast(lead > 0 ? $"YOU PASS {g.Name}" : $"{g.Name} PASSES YOU", lead > 0 ? UiTheme.Lime : UiTheme.Red);
        }
        g.Lead = lead;
        if (!g.FinalCalled && g.Finish - Math.Max(World.DistInSeg, g.S) < 200f)
        {
            g.FinalCalled = true;
            Music.Synth.SfxRaceFinal();
            Hud?.Toast("200 m TO GO", g.Color);
        }

        bool riderDone = World.DistInSeg >= g.Finish;
        bool ghostDone = g.S >= g.Finish;
        if (riderDone || ghostDone) EndGhostRace(riderDone && (!ghostDone || World.DistInSeg >= g.S));
    }

    /// <summary>won: true / false, or null when the race was abandoned.</summary>
    void EndGhostRace(bool? won)
    {
        var g = _ghost;
        if (g == null) return;
        _ghost = null;
        if (IsInstanceValid(g.Avatar)) g.Avatar.QueueFree();
        if (g.Arch != null && IsInstanceValid(g.Arch)) g.Arch.QueueFree();
        if (won == null) return;

        if (won.Value)
        {
            _racesWon++;
            Music.Synth.SfxGel();
            string reward = "";
            if (Config.PowerGels && SlipCharges < MaxSlipCharges)
            {
                SlipCharges++;
                reward = "  ·  +1 SLIPSTREAM CHARGE";
            }
            Hud?.Banner($"YOU BEAT {g.Name}{reward}", g.Replay != null ? "FASTER THAN YOU WERE" : "VICTORY", UiTheme.Lime);
        }
        else
        {
            _racesLost++;
            Music.Synth.SfxSlipstream(false);
            float behind = g.S - World.DistInSeg;
            Hud?.Banner($"{g.Name} WINS BY {behind:0} m  ·  NEXT TIME", "DEFEATED", UiTheme.Red);
        }
    }
}
