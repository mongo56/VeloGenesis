using System;
using System.Threading.Tasks;
using VeloGenesis.Core;

namespace VeloGenesis.Devices;

/// <summary>Owns the active trainer and heart rate monitor and remembers them between sessions.</summary>
public static class DeviceManager
{
    public static readonly BleScanner Scanner = new();
    public static readonly SimulatedTrainer Simulator = new();

    static FtmsTrainer _ble;
    public static HeartRateMonitor Hr { get; private set; }

    /// <summary>The trainer used for rides: the connected BLE trainer, or the simulator.</summary>
    public static ITrainer Trainer => _ble != null && _ble.State != ConnState.Disconnected ? _ble : Simulator;
    public static FtmsTrainer BleTrainer => _ble;

    public static event Action Changed;

    public static int HeartRate
    {
        get
        {
            int strap = Hr?.Bpm ?? 0;
            return strap > 0 ? strap : Trainer.HeartRate;
        }
    }

    public static async Task<bool> ConnectTrainer(ulong address, string name)
    {
        _ble?.Disconnect();
        _ble = new FtmsTrainer(address, name);
        _ble.StateChanged += () => Changed?.Invoke();
        Changed?.Invoke();
        bool ok = await _ble.ConnectAsync();
        if (ok)
        {
            Settings.Current.LastTrainerAddress = address;
            Settings.Current.LastTrainerName = _ble.Name;
            MainThread.Post(Settings.Save);
        }
        MainThread.Post(() => Changed?.Invoke());
        return ok;
    }

    public static async Task<bool> ConnectHr(ulong address, string name)
    {
        Hr?.Disconnect();
        Hr = new HeartRateMonitor(address, name);
        Hr.StateChanged += () => Changed?.Invoke();
        Changed?.Invoke();
        bool ok = await Hr.ConnectAsync();
        if (ok)
        {
            Settings.Current.LastHrAddress = address;
            Settings.Current.LastHrName = Hr.Name;
            MainThread.Post(Settings.Save);
        }
        MainThread.Post(() => Changed?.Invoke());
        return ok;
    }

    public static void DisconnectTrainer(bool forget)
    {
        _ble?.Disconnect();
        _ble = null;
        if (forget) { Settings.Current.LastTrainerAddress = 0; Settings.Save(); }
        Changed?.Invoke();
    }

    public static void DisconnectHr(bool forget)
    {
        Hr?.Disconnect();
        Hr = null;
        if (forget) { Settings.Current.LastHrAddress = 0; Settings.Save(); }
        Changed?.Invoke();
    }

    /// <summary>Reconnect to the devices used last time (non-blocking).</summary>
    public static void AutoConnect()
    {
        var s = Settings.Current;
        if (s.LastTrainerAddress != 0) _ = ConnectTrainer(s.LastTrainerAddress, s.LastTrainerName);
        if (s.LastHrAddress != 0) _ = ConnectHr(s.LastHrAddress, s.LastHrName);
    }

    public static void Tick(double dt) => Trainer.Tick(dt);

    public static void Shutdown()
    {
        Scanner.Stop();
        _ble?.Disconnect();
        Hr?.Disconnect();
    }
}
