namespace VeloGenesis.Ride;

public enum RideMode { FreeRide, Erg }

/// <summary>Everything chosen on the ride setup screen.</summary>
public class RideConfig
{
    public RideMode Mode = RideMode.FreeRide;

    /// <summary>Typical gradient of the hills, in percent.</summary>
    public float AvgGradient = 4f;
    /// <summary>Planned total elevation gain for the ride, in metres.</summary>
    public float ElevationGain = 400f;
    /// <summary>Planned distance (free ride) – used to pace the elevation budget.</summary>
    public float DistanceKm = 25f;
    /// <summary>Allow long sustained climbs (otherwise rolling terrain only).</summary>
    public bool Climb = true;
    /// <summary>Power gels / slipstream power-up and ghost races (free ride only; ERG always has gels).</summary>
    public bool PowerGels = true;
    public bool GhostRaces = true;
    public int Seed;

    public WorkoutType Workout = WorkoutType.SweetSpot;
    public int WorkoutMinutes = 60;
    public int WorkoutSeed;

    /// <summary>Distance used for pacing the climbing budget.</summary>
    public float PlannedDistanceKm => Mode == RideMode.Erg ? WorkoutMinutes / 60f * 30f : DistanceKm;
    public float GainPerMetre => PlannedDistanceKm <= 0.1f ? 0.01f : ElevationGain / (PlannedDistanceKm * 1000f);
}
