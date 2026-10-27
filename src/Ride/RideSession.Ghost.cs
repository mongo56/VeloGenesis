using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.UI;
using VeloGenesis.World;

namespace VeloGenesis.Ride;

/// <summary>
/// Ghost races: after taking a harder branch in free ride, a holographic rival sometimes appears
/// alongside and challenges you to a finish arch further along the segment. The ghost rides the
/// same terrain with the same physics at a steady power derived from your FTP.
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
    }

    const float GhostChance = 0.6f;
    const float GhostLane = -WorldBuilder.RiderLane;
    static readonly Color GhostColor = new(0.55f, 0.95f, 1f);

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
            if (Config.Mode != RideMode.FreeRide || !Config.GhostRaces || !seg.IsHarder) return false;
            if (_rng.NextDouble() > GhostChance) return false;
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

        var avatar = new RiderAvatar();
        AddChild(avatar);
        avatar.MakeGhost(GhostColor);

        _ghost = new GhostRacer
        {
            Seg = seg,
            Phys = new RiderPhysics { Mass = Physics.Mass, Speed = Math.Max(Physics.Speed, 3f) },
            Avatar = avatar,
            Name = $"GHOST {tier.name}",
            Power = power,
            S = World.DistInSeg,
            Start = World.DistInSeg,
            Finish = World.DistInSeg + length,
        };
        _ghost.Arch = World.AddFinishArch(_ghost.Finish, GhostColor);

        Music.Synth.SfxSector();
        Hud?.Banner($"CHALLENGE  ·  {power:0} W ({pct * 100:0}% FTP)  ·  {length / 1000f:0.0} km", $"BEAT {_ghost.Name}", GhostColor);
        GD.Print($"Ghost race: {_ghost.Name} at {power} W over {length:0} m on {seg.Name}");
        return true;
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
        // Steady effort with a little human-like variation; digs in on steeper ramps.
        float surge = 1f + 0.04f * MathF.Sin(g.T * 0.7f) + 0.02f * MathF.Sin(g.T * 2.3f) + Math.Clamp(grade, 0f, 10f) * 0.006f;
        g.Phys.Step(g.Power * surge, grade, dt);
        g.S = Math.Min(g.S + g.Phys.Speed * dt, g.Seg.Length - 1f);

        World.PoseOnCurrent(g.S, GhostLane, out var pos, out var fwd, out _);
        g.Avatar.GlobalPosition = pos;
        g.Avatar.Basis = Basis.LookingAt(fwd, Vector3.Up);
        g.Avatar.Animate(g.Phys.Speed, 88f + 4f * MathF.Sin(g.T * 0.5f), dt);

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
            Hud?.Banner($"YOU BEAT {g.Name}{reward}", "VICTORY", UiTheme.Lime);
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
