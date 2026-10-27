using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Windows.Devices.Bluetooth.Advertisement;
using VeloGenesis.Core;

namespace VeloGenesis.Devices;

public class FoundDevice
{
    public ulong Address;
    public string Name;
    public bool IsTrainer;
    public bool IsHeartRate;
    public short Rssi;
    public DateTime LastSeen;
}

/// <summary>Scans BLE advertisements for trainers (FTMS) and heart rate straps.</summary>
public class BleScanner
{
    BluetoothLEAdvertisementWatcher _watcher;
    readonly Dictionary<ulong, FoundDevice> _found = new();
    readonly object _lock = new();

    public bool Scanning { get; private set; }
    public string Error { get; private set; } = "";
    public event Action Changed;

    public List<FoundDevice> Devices
    {
        get { lock (_lock) return _found.Values.OrderByDescending(d => d.IsTrainer).ThenByDescending(d => d.Rssi).ToList(); }
    }

    public void Start()
    {
        if (Scanning) return;
        try
        {
            lock (_lock) _found.Clear();
            _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            _watcher.Received += OnReceived;
            _watcher.Stopped += (_, e) => MainThread.Post(() =>
            {
                Scanning = false;
                if (e.Error != Windows.Devices.Bluetooth.BluetoothError.Success) Error = $"Bluetooth: {e.Error}";
                Changed?.Invoke();
            });
            _watcher.Start();
            Scanning = true;
            Error = "";
        }
        catch (Exception e)
        {
            Error = "Bluetooth unavailable: " + e.Message;
            GD.PrintErr(Error);
        }
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            try { _watcher.Stop(); } catch { /* already stopped */ }
            _watcher.Received -= OnReceived;
            _watcher = null;
        }
        Scanning = false;
        Changed?.Invoke();
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        var uuids = e.Advertisement.ServiceUuids;
        bool trainer = uuids.Contains(BleUuids.FitnessMachine);
        bool hr = uuids.Contains(BleUuids.HeartRate);
        string name = e.Advertisement.LocalName;
        bool changed = false;
        lock (_lock)
        {
            if (!_found.TryGetValue(e.BluetoothAddress, out var d))
            {
                // Scan responses can arrive before/after the advert that carries the service list,
                // so keep anything with a name and merge flags as they arrive.
                if (!trainer && !hr && string.IsNullOrWhiteSpace(name)) return;
                d = new FoundDevice { Address = e.BluetoothAddress, Name = name };
                _found[e.BluetoothAddress] = d;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(name) && d.Name != name) { d.Name = name; changed = true; }
            if (trainer && !d.IsTrainer) { d.IsTrainer = true; changed = true; }
            if (hr && !d.IsHeartRate) { d.IsHeartRate = true; changed = true; }
            d.Rssi = e.RawSignalStrengthInDBm;
            d.LastSeen = DateTime.UtcNow;
        }
        if (changed) MainThread.Post(() => Changed?.Invoke());
    }
}
