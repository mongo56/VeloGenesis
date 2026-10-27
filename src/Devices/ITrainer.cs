namespace VeloGenesis.Devices;

public enum ConnState { Disconnected, Connecting, Connected }

/// <summary>A controllable source of power/cadence: a real FTMS trainer or the keyboard simulator.</summary>
public interface ITrainer
{
    string Name { get; }
    ConnState State { get; }
    bool IsSimulated { get; }

    float Power { get; }
    float Cadence { get; }
    /// <summary>Speed the trainer reports from its flywheel, in m/s (0 if unknown).</summary>
    float TrainerSpeed { get; }
    /// <summary>Heart rate reported by the trainer itself (0 if none).</summary>
    int HeartRate { get; }
    /// <summary>Seconds since the last data packet.</summary>
    double DataAge { get; }

    /// <summary>ERG mode: hold this power regardless of cadence.</summary>
    void SetErgTarget(int watts);

    /// <summary>Simulation mode: trainer computes resistance from grade and its own speed.</summary>
    void SetSimulation(float gradePercent, float crr, float cwKgPerM);

    void Tick(double dt);
    void Disconnect();
}
