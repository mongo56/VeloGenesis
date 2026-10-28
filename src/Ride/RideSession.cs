using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VeloGenesis.Audio;
using VeloGenesis.Core;
using VeloGenesis.Devices;
using VeloGenesis.UI;
using VeloGenesis.World;

namespace VeloGenesis.Ride;

public class RideSummary
{
    public RideStats Stats;
    public string FitPath;
    public int ForksTaken;
    public int Gels;
    public int SlipstreamsUsed;
    public int RacesWon, RacesLost;
    public bool PowerGels = true;
    public int GelsSeen;
    /// <summary>Seconds spent in each power zone (index 0 = Z1).</summary>
    public int[] ZoneSeconds = new int[7];
    public List<string> Sectors = new();
    public string WorkoutName;
    public RideMode Mode;
    public int PacksCaught, SurgesHeld, SurgesTotal;
    public float DraftSeconds;
    public int RhythmZones, RhythmPerfect, RhythmGood;
    public float RhythmAccuracy;
    public List<string> Biomes = new();
    /// <summary>Durations where this ride set a new all-time best.</summary>
    public List<Effort> NewBests = new();
}

/// <summary>
/// The ride game loop: reads the trainer, drives ERG / simulation resistance, integrates speed,
/// moves through the world, resolves forks, records the activity and feeds the HUD and music.
/// In attract mode (menu background) it rides itself with a private simulator.
/// </summary>
public partial class RideSession : Node3D
{
    public RideConfig Config { get; }
    public bool Attract { get; }
    public MusicDirector Music { get; }

    public WorldRoot World { get; private set; }
    public Hud Hud { get; private set; }
    RiderAvatar _avatar;
    ChaseCamera _cam;
    public ChaseCamera Camera => _cam;
    LightTrail _trail;
    readonly SimulatedTrainer _attractSim = new() { AutoPilot = true, Effort = 230 };

    public readonly RiderPhysics Physics = new();
    public readonly Gearing Gearing = new();
    public readonly ForkDecider Fork = new();
    public readonly RideRecorder Recorder = new();
    public Workout Workout { get; private set; }
    public float WorkoutTime { get; private set; }
    public int ErgTarget { get; private set; }
    public int ErgBias { get; private set; }
    public float SentGrade { get; private set; }

    readonly RollingAverage _p3 = new(3), _p20 = new(20), _c3 = new(3), _c15 = new(15);
    public float Power3s => _p3.Value;
    public float Cadence { get; private set; }
    public int HeartRate { get; private set; }
    public float Speed => Physics.Speed;
    public double Elapsed { get; private set; }
    double _powerSum, _powerTime;
    public float AvgPower => _powerTime > 0 ? (float)(_powerSum / _powerTime) : 0;

    public bool Paused;
    public bool Finished { get; private set; }
    public event Action PauseRequested;

    /// <summary>Debug: metres to jump ahead once the first fork exists, and camera views to cycle.</summary>
    public float DebugSkip;
    public int DebugCameraCycles;
    /// <summary>Debug: grant this many slipstream charges and fire one.</summary>
    public int DebugSlip;

    double _sampleAcc;
    /// <summary>Game-time seconds since the ride started (keeps FIT timestamps right when time is sped up).</summary>
    double _rideClock;
    int _forksTaken, _gels, _gelsSeen;
    readonly List<string> _sectors = new();
    int _lastBeep = -1;
    bool _targetAnnounced;
    readonly Random _rng = new();

    public string NowPlaying => Music?.NowPlaying ?? "";
    public string CameraMode => _cam?.ModeName ?? "";
    ITrainer Trainer => Attract ? _attractSim : DeviceManager.Trainer;

    public RideSession() { }

    public RideSession(RideConfig cfg, MusicDirector music, bool attract)
    {
        Config = cfg;
        Music = music;
        Attract = attract;
    }

    public override void _Ready()
    {
        var s = Settings.Current;
        Physics.Mass = s.TotalMassKg;

        World = new WorldRoot();
        AddChild(World);
        World.SectorEntered += OnSector;
        World.ForkTaken += OnForkTaken;
        World.GelCollected += OnGel;
        World.GelMissed += OnGelMissed;
        World.KmPassed += OnKm;
        World.BiomeEntered += OnBiome;
        World.Init(Config, Quality.Get(s.Quality));
        _biomes.Add(World.Current.Biome.Name);

        _avatar = new RiderAvatar();
        AddChild(_avatar);
        _trail = new LightTrail();
        AddChild(_trail);
        _cam = new ChaseCamera { Far = World.Quality.CameraFar, Near = 0.1f, Fov = 68 };
        if (Attract) _cam.Mode = ChaseCamera.View.Cinematic;
        AddChild(_cam);
        _cam.MakeCurrent();
        OnSector(World.Current);

        if (Config.Mode == RideMode.Erg)
            Workout = WorkoutGenerator.Generate(Config.Workout, Config.WorkoutMinutes, Config.WorkoutSeed);

        if (!Attract)
        {
            Hud = new Hud(this);
            var layer = new CanvasLayer { Layer = 1 };
            AddChild(layer);
            layer.AddChild(Hud);
            Recorder.Begin();
            Hud.Banner("VELOGENESIS", Config.Mode == RideMode.Erg ? Workout.Name.ToUpper() : "FREE RIDE", UiTheme.Cyan);
        }
    }

    public override void _Process(double delta)
    {
        // Allow bigger steps when time is sped up for testing.
        float dt = Math.Min((float)delta, 0.1f * (float)Math.Max(1.0, Engine.TimeScale));
        _rideClock += dt;
        var s = Settings.Current;
        var trainer = Trainer;
        if (Attract) trainer.Tick(dt);

        if (DebugSkip > 0 && World.Current.Children != null)
        {
            World.Advance(Math.Min(DebugSkip, World.Current.Length - World.DistInSeg - 1f));
            DebugSkip = 0;
            _cam.Snap();
            _trail.Reset();
        }
        for (; DebugCameraCycles > 0; DebugCameraCycles--) _cam.Cycle();
        if (DebugSlip > 0)
        {
            SlipCharges = Math.Min(DebugSlip, MaxSlipCharges);
            DebugSlip = 0;
            ActivateSlipstream();
        }

        HandleInput();

        if (Paused)
        {
            if (!Attract)
            {
                if (Config.Mode == RideMode.Erg) trainer.SetErgTarget(Math.Min(100, (int)(s.Ftp * 0.45f)));
                else trainer.SetSimulation(0, Physics.Crr, Physics.Cw);
            }
            Physics.Speed = Math.Max(0, Physics.Speed - dt * 4f);
            FollowCamera(dt);
            return;
        }

        float power = trainer.Power;
        Cadence = trainer.Cadence;
        HeartRate = Attract ? 0 : DeviceManager.HeartRate;
        _p3.Add(power, dt);
        _p20.Add(power, dt);
        _c3.Add(Cadence, dt);
        _c15.Add(Cadence, dt);
        float grade = World.Grade;
        bool pedalling = power > 15 || Cadence > 20;

        // Resistance control
        if (Config.Mode == RideMode.Erg && Workout != null)
        {
            if (pedalling) WorkoutTime += dt;
            float pct = Workout.TargetPct(WorkoutTime);
            ErgTarget = (int)MathF.Round(pct * s.Ftp * (1f + ErgBias / 100f));
            trainer.SetErgTarget(ErgTarget);
            WorkoutBeeps();
        }
        else
        {
            float feel = grade * s.TrainerDifficulty * (SlipstreamActive ? SlipstreamGradeFactor : 1f);
            // Sitting in a draft pack lowers the wind resistance the trainer simulates too.
            float cw = Physics.Cw * PackDraft;
            SentGrade = s.VirtualShifting
                ? Gearing.TrainerGrade(feel, Cadence, trainer.TrainerSpeed, Physics.Mass, Physics.Crr, cw, dt)
                : feel;
            trainer.SetSimulation(SentGrade, Physics.Crr, cw);
        }
        if (trainer is SimulatedTrainer sim)
        {
            sim.VirtualSpeed = Physics.Speed;
            sim.GearRatio = Gearing.Ratio;
        }

        UpdateSlipstream(dt);
        UpdatePack(dt);
        Physics.Step(power, grade, dt);
        World.Advance(Physics.Speed * dt);
        UpdateGhostRace(dt);
        UpdateFork();
        UpdateRhythm(dt);

        if (Physics.Speed > 0.5f || pedalling)
        {
            Elapsed += dt;
            _powerSum += power * dt;
            _powerTime += dt;
        }

        if (!Attract)
        {
            Recorder.MovingSeconds = Elapsed;
            _sampleAcc += dt;
            if (_sampleAcc >= 1.0)
            {
                _sampleAcc -= 1.0;
                Recorder.Add(new RideSample
                {
                    Time = Recorder.StartTime.AddSeconds(_rideClock),
                    Distance = World.GlobalDist,
                    Speed = Physics.Speed,
                    Power = (int)MathF.Round(power),
                    Cadence = (int)MathF.Round(Cadence),
                    HeartRate = HeartRate,
                    Altitude = World.RiderPosition.Y + 120f,
                    X = World.RiderPosition.X,
                    Z = World.RiderPosition.Z,
                });
            }
            if (Config.Mode == RideMode.FreeRide && !_targetAnnounced && World.GlobalDist >= Config.DistanceKm * 1000f)
            {
                _targetAnnounced = true;
                Hud?.Banner("TARGET DISTANCE REACHED", "KEEP EXPLORING", UiTheme.Lime);
            }
        }

        // Power zone: gels only light up (and can be grabbed) in Z4 and above.
        CurrentZone = PowerZones.Zone(Power3s, s.Ftp);
        World.GelsActive = Attract || CurrentZone >= PowerZones.GelZone;

        Music.Synth.Intensity = Attract ? 0.62f : Math.Clamp(Power3s / Math.Max(1, s.Ftp) / 1.05f + Math.Max(0, grade) * 0.01f, 0.05f, 1f);

        _avatar.GlobalPosition = World.RiderPosition;
        _avatar.Basis = Basis.LookingAt(World.RiderForward, Vector3.Up);
        UpdateStance(dt, s.Ftp);
        _avatar.Animate(Physics.Speed, Cadence, dt);
        _trail.Push(World.RiderPosition - World.RiderForward * 0.55f);
        FollowCamera(dt);
    }

    void FollowCamera(float dt) => _cam.Follow(World.RiderPosition, World.RiderForward, Physics.Speed, dt, SlipstreamActive ? 7f : 0f);

    // ---------------------------------------------------------------- standing / sprinting

    float _stand, _sprint, _standTimer;
    bool _standing;

    /// <summary>
    /// Out of the saddle when pedal force is high: lots of power at a comparatively low cadence
    /// (torque well above what FTP at 90 rpm needs), or an all-out sprint. Uses hysteresis so the
    /// rider doesn't flicker between seated and standing.
    /// </summary>
    void UpdateStance(float dt, int ftp)
    {
        float cad = _c3.Value, p = Power3s;
        float torque = cad > 25f ? p / (cad * MathF.Tau / 60f) : 0f;
        float seatedTorque = Math.Max(ftp, 100) / (90f * MathF.Tau / 60f);
        bool sprinting = p > 1.5f * ftp && cad > 40f;
        bool wantStand = sprinting || (p > 0.85f * ftp && torque > 1.45f * seatedTorque);

        _standTimer = wantStand ? Math.Max(0f, _standTimer) + dt : Math.Min(0f, _standTimer) - dt;
        if (_standTimer > 0.8f) _standing = true;
        else if (_standTimer < -1.5f) _standing = false;

        _stand += ((_standing ? 1f : 0f) - _stand) * Math.Min(1f, dt * 3f);
        _sprint += ((_standing && sprinting ? 1f : 0f) - _sprint) * Math.Min(1f, dt * 2.5f);
        _avatar.SetStance(_stand, _sprint);
    }

    // ---------------------------------------------------------------- slipstream power-up

    public const int MaxSlipCharges = 3;
    public const float SlipstreamDuration = 20f;
    const float SlipstreamDraft = 0.6f;        // 40% less air drag
    const float SlipstreamGradeFactor = 0.8f;  // climbs feel 20% easier on the trainer
    static readonly Color SlipColor = new(1f, 0.78f, 0.2f);

    /// <summary>Stored charges, earned by collecting power gels.</summary>
    public int SlipCharges { get; private set; }
    public float SlipRemaining { get; private set; }
    public bool SlipstreamActive => SlipRemaining > 0;
    int _slipsUsed;

    void ActivateSlipstream()
    {
        if (!Config.PowerGels) return;
        if (SlipCharges <= 0)
        {
            Hud?.Toast("NO SLIPSTREAM CHARGES – COLLECT POWER GELS (Z4+)", UiTheme.Dim);
            return;
        }
        SlipCharges--;
        _slipsUsed++;
        SlipRemaining += SlipstreamDuration; // stacking extends the current one
        _trail.SetColor(SlipColor);
        _trail.SetBoost(3f);
        Music.Synth.SfxSlipstream(true);
        Hud?.Toast(Config.Mode == RideMode.Erg ? "SLIPSTREAM  ·  SPEED BOOST" : "SLIPSTREAM  ·  -40% DRAG  ·  EASIER CLIMBING", SlipColor);
    }

    void UpdateSlipstream(float dt)
    {
        if (!SlipstreamActive) { Physics.DraftFactor = 1f; return; }
        SlipRemaining = Math.Max(0, SlipRemaining - dt);
        Physics.DraftFactor = SlipstreamDraft;
        if (!SlipstreamActive)
        {
            Physics.DraftFactor = 1f;
            _trail.SetColor(World.Current.Primary);
            _trail.SetBoost(1f);
            Music.Synth.SfxSlipstream(false);
        }
    }

    void HandleInput()
    {
        if (Attract) return;
        if (Input.IsActionJustPressed(InputSetup.Pause)) PauseRequested?.Invoke();
        if (Paused) return;
        if (Input.IsActionJustPressed(InputSetup.Camera)) _cam.Cycle();
        if (Input.IsActionJustPressed(InputSetup.ToggleHud) && Hud != null) Hud.Visible = !Hud.Visible;
        if (Input.IsActionJustPressed(InputSetup.NextTrack)) Music.Next();
        if (Input.IsActionJustPressed(InputSetup.Slipstream)) ActivateSlipstream();

        int shift = (Input.IsActionJustPressed(InputSetup.GearUp) ? 1 : 0) - (Input.IsActionJustPressed(InputSetup.GearDown) ? 1 : 0);
        if (shift != 0)
        {
            if (Config.Mode == RideMode.Erg)
            {
                ErgBias = Math.Clamp(ErgBias + shift * 5, -30, 30);
                Hud?.Toast($"ERG INTENSITY {ErgBias:+0;-0;0}%", UiTheme.Amber);
            }
            else if (Gearing.Shift(shift))
            {
                if (shift > 0) Music.Synth.SfxShiftUp(); else Music.Synth.SfxShiftDown();
            }
        }

        if (World.ForkAhead && Fork.Active)
        {
            if (Input.IsActionJustPressed(InputSetup.ForkLeft)) Fork.Choose(0);
            if (Input.IsActionJustPressed(InputSetup.ForkRight)) Fork.Choose(1);
        }
    }

    void UpdateFork()
    {
        if (!World.ForkAhead) return;
        bool erg = Config.Mode == RideMode.Erg;
        if (!Fork.Active)
        {
            var kids = World.Current.Children;
            int harder = kids[0].IsHarder ? 0 : 1;
            int plan = kids[0].IsPlan ? 0 : 1;
            Fork.Begin(erg ? _c15.Value : _p20.Value, harder, plan, erg);
            if (Attract) Fork.Choose(_rng.Next(2));
            else
            {
                Hud?.Toast("FORK AHEAD", UiTheme.Magenta);
                Music.Synth.SfxForkAhead();
            }
        }
        Fork.Update(erg ? _c3.Value : _p3.Value);
        World.Selection = Fork.Selection;
    }

    void WorkoutBeeps()
    {
        int idx = Workout.Locate(WorkoutTime, out float tIn);
        if (idx < 0 || idx + 1 >= Workout.Intervals.Count) return;
        var iv = Workout.Intervals[idx];
        var next = Workout.Intervals[idx + 1];
        if (MathF.Abs(next.StartPct - iv.PctAt(iv.Duration)) < 0.03f) return;
        int remaining = (int)MathF.Ceiling(iv.Duration - tIn);
        if (remaining <= 3 && remaining >= 1 && remaining != _lastBeep)
        {
            _lastBeep = remaining;
            Music.Synth.SfxCountdown(false);
        }
        else if (remaining > 3) _lastBeep = -1;
        if (iv.Duration - tIn < 0.05f && _lastBeep == 1)
        {
            _lastBeep = 0;
            Music.Synth.SfxCountdown(true);
            Hud?.Toast($"{next.Label.ToUpper()}  ·  {next.StartPct * Settings.Current.Ftp:0} W", UiTheme.ZoneColor(next.StartPct));
        }
    }

    void OnSector(Segment seg)
    {
        if (seg == null) return;
        _avatar?.SetColors(seg.Primary, seg.Secondary);
        if (!SlipstreamActive) _trail?.SetColor(seg.Primary);
        _gelsSeen += seg.Gels.Length;
        if (!_sectors.Contains(seg.Name)) _sectors.Add(seg.Name);
        Music.Synth.SetMood(seg.Kind switch { SectorKind.Climb => 3, SectorKind.Descent => 1, SectorKind.Flat => 2, _ => 0 });
        if (seg.Kind != SectorKind.Start)
        {
            string info = $"{seg.Kind.ToString().ToUpper()}  ·  {seg.Length / 1000f:0.0} km  ·  +{seg.Gain:0} m";
            Hud?.Banner(info, seg.Name.ToUpper(), seg.Primary.Lightened(0.2f));
            if (!Attract) Music.Synth.SfxSector();
        }
    }

    void OnForkTaken(Segment chosen, Segment other)
    {
        Fork.End();
        _forksTaken++;
        EndGhostRace(null);
        MaybeStartGhostRace(chosen);
        MaybeSpawnPack(chosen);
        GD.Print($"Fork {_forksTaken}: options {other.Kind}/{chosen.Kind}; took {chosen.Name} ({(chosen.IsHarder ? "harder" : "easier")}, {chosen.Kind}, {chosen.Length:0} m, +{chosen.Gain:0} m) at {World.GlobalDist / 1000f:0.00} km, climbed {World.GainSoFar:0} m");
    }

    /// <summary>Current power zone (1–7) from the 3 s average.</summary>
    public int CurrentZone { get; private set; } = 1;
    int _gelsMissed;
    double _lastMissToast = -100;

    void OnGelMissed(Segment seg)
    {
        _gelsMissed++;
        if (Attract || _rideClock - _lastMissToast < 25) return;
        _lastMissToast = _rideClock;
        int w = PowerZones.LowerWatts(PowerZones.GelZone, Settings.Current.Ftp);
        Hud?.Toast($"GEL MISSED  ·  ride in Z{PowerZones.GelZone}+ (≥ {w} W) to grab power gels", UiTheme.Dim);
    }

    void OnGel(Segment seg)
    {
        _gels++;
        if (Attract) return;
        Music.Synth.SfxGel();
        if (SlipCharges < MaxSlipCharges)
        {
            SlipCharges++;
            Hud?.Toast($"+1 SLIPSTREAM CHARGE  ({SlipCharges}/{MaxSlipCharges})  ·  {InputSetup.Describe(InputSetup.Slipstream)}", UiTheme.Amber);
        }
        else Hud?.Toast("POWER GEL  ·  SLIPSTREAM CHARGES FULL", UiTheme.Amber);
    }

    readonly List<string> _biomes = new();

    void OnBiome(Biome b)
    {
        if (!_biomes.Contains(b.Name)) _biomes.Add(b.Name);
        GD.Print($"Biome: {b.Name} at {World.GlobalDist / 1000f:0.0} km");
        if (Attract) return;
        Music.Synth.SfxBiome();
        Music.FreshSong();
        Hud?.Banner($"BIOME {_biomes.Count}  ·  {b.Tagline}", b.Name.ToUpper(), b.Rolling[0].Item1.Lightened(0.25f));
    }

    void OnKm(int km)
    {
        if (Attract) return;
        Music.Synth.SfxKm();
    }

    int[] ZoneSeconds(int ftp)
    {
        var z = new int[7];
        foreach (var smp in Recorder.Samples)
            if (smp.Power > 0 || smp.Speed > 0.5f) z[PowerZones.Zone(smp.Power, ftp) - 1]++;
        return z;
    }

    /// <summary>Ends the ride, writes the FIT file and returns the summary.</summary>
    public RideSummary Finish(bool save)
    {
        Finished = true;
        var s = Settings.Current;
        if (!Attract)
        {
            if (Config.Mode == RideMode.Erg) DeviceManager.Trainer.SetErgTarget(Math.Min(100, (int)(s.Ftp * 0.4f)));
            else DeviceManager.Trainer.SetSimulation(0, Physics.Crr, Physics.Cw);
        }

        var stats = Recorder.Compute(s.Ftp);
        string path = null;
        if (save && Recorder.Samples.Count >= 10)
        {
            string name = $"VeloGenesis_{Recorder.StartTime.ToLocalTime():yyyy-MM-dd_HHmm}.fit";
            path = Path.Combine(Paths.Rides, name);
            try { FitWriter.Write(path, Recorder, stats, s.VirtualGps); }
            catch (Exception e) { GD.PrintErr("FIT write failed: " + e); path = null; }
        }
        var newBests = save && !Attract ? PersonalBests.Current.Record(Recorder.Samples, Recorder.StartTime) : new List<Effort>();
        return new RideSummary
        {
            Stats = stats,
            FitPath = path,
            ForksTaken = _forksTaken,
            Gels = _gels,
            SlipstreamsUsed = _slipsUsed,
            RacesWon = _racesWon,
            RacesLost = _racesLost,
            PowerGels = Config.PowerGels,
            GelsSeen = _gelsSeen,
            ZoneSeconds = ZoneSeconds(s.Ftp),
            Sectors = new List<string>(_sectors),
            WorkoutName = Workout?.Name,
            Mode = Config.Mode,
            PacksCaught = _packsCaught,
            DraftSeconds = _draftSeconds,
            SurgesHeld = _surgesHeld,
            SurgesTotal = _surgesTotal,
            RhythmZones = _rhythmZones,
            RhythmPerfect = _rhythmPerfect,
            RhythmGood = _rhythmGood,
            RhythmAccuracy = _rhythmZones > 0 ? _rhythmAccSum / _rhythmZones : 0,
            Biomes = new List<string>(_biomes),
            NewBests = newBests,
        };
    }
}
