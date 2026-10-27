using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using VeloGenesis.Core;

namespace VeloGenesis.Devices;

/// <summary>
/// Thin wrapper around a WinRT BLE device: connection, characteristic lookup, notifications, writes,
/// and automatic reconnection after drop-outs.
/// </summary>
public abstract class BleLink
{
    protected BluetoothLEDevice Device;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastData = -1;
    bool _disposed;
    bool _reconnecting;

    public ulong Address { get; }
    public string Name { get; protected set; }
    public ConnState State { get; private set; } = ConnState.Disconnected;
    public string Status { get; private set; } = "";
    public event Action StateChanged;

    public double DataAge => _lastData < 0 ? double.MaxValue : _clock.Elapsed.TotalSeconds - _lastData;
    protected void MarkData() => _lastData = _clock.Elapsed.TotalSeconds;

    protected BleLink(ulong address, string name)
    {
        Address = address;
        Name = string.IsNullOrWhiteSpace(name) ? $"BLE {address:X12}" : name;
    }

    public async Task<bool> ConnectAsync()
    {
        SetState(ConnState.Connecting, "Connecting…");
        try
        {
            Device = await BluetoothLEDevice.FromBluetoothAddressAsync(Address);
            if (Device == null) { SetState(ConnState.Disconnected, "Device not found"); return false; }
            if (!string.IsNullOrWhiteSpace(Device.Name)) Name = Device.Name;
            Device.ConnectionStatusChanged += OnConnectionStatusChanged;
            if (!await SetupAsync()) { SetState(ConnState.Disconnected, Status == "" ? "Setup failed" : Status); return false; }
            SetState(ConnState.Connected, "Connected");
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[{Name}] connect failed: {e.Message}");
            SetState(ConnState.Disconnected, "Error: " + e.Message);
            return false;
        }
    }

    /// <summary>Discover characteristics and subscribe. Return false on failure.</summary>
    protected abstract Task<bool> SetupAsync();

    protected async Task<GattCharacteristic> GetCharacteristicAsync(Guid service, Guid characteristic)
    {
        var sr = await Device.GetGattServicesForUuidAsync(service, BluetoothCacheMode.Uncached);
        if (sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0) return null;
        var cr = await sr.Services[0].GetCharacteristicsForUuidAsync(characteristic, BluetoothCacheMode.Uncached);
        if (cr.Status != GattCommunicationStatus.Success || cr.Characteristics.Count == 0) return null;
        return cr.Characteristics[0];
    }

    protected static async Task<bool> SubscribeAsync(GattCharacteristic c, bool indicate, Action<byte[]> handler)
    {
        c.ValueChanged += (_, args) =>
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            try { handler(bytes); } catch (Exception e) { GD.PrintErr($"BLE parse error: {e.Message}"); }
        };
        var value = indicate
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        var status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(value);
        return status == GattCommunicationStatus.Success;
    }

    protected static async Task<bool> WriteAsync(GattCharacteristic c, byte[] data)
    {
        var w = new DataWriter();
        w.WriteBytes(data);
        var status = await c.WriteValueAsync(w.DetachBuffer(), GattWriteOption.WriteWithResponse);
        return status == GattCommunicationStatus.Success;
    }

    void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (_disposed) return;
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            SetState(ConnState.Connecting, "Signal lost – reconnecting…");
            _ = ReconnectLoopAsync();
        }
        else if (State != ConnState.Connected && !_reconnecting)
        {
            SetState(ConnState.Connected, "Connected");
        }
    }

    async Task ReconnectLoopAsync()
    {
        if (_reconnecting) return;
        _reconnecting = true;
        try
        {
            while (!_disposed)
            {
                await Task.Delay(3000);
                if (_disposed) break;
                // Windows usually restores the link itself once a GATT request is made;
                // re-running setup re-subscribes notifications on the fresh session.
                try
                {
                    if (await SetupAsync()) { SetState(ConnState.Connected, "Reconnected"); break; }
                }
                catch (Exception e) { GD.PrintErr($"[{Name}] reconnect attempt failed: {e.Message}"); }
            }
        }
        finally { _reconnecting = false; }
    }

    void SetState(ConnState s, string status)
    {
        State = s;
        Status = status;
        MainThread.Post(() => StateChanged?.Invoke());
    }

    public virtual void Disconnect()
    {
        _disposed = true;
        if (Device != null)
        {
            Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            Device.Dispose();
            Device = null;
        }
        SetState(ConnState.Disconnected, "Disconnected");
    }
}
