using System.Threading.Tasks;

namespace VeloGenesis.Devices;

/// <summary>Standard Bluetooth heart rate strap (Heart Rate Service 0x180D).</summary>
public class HeartRateMonitor : BleLink
{
    volatile int _bpm;

    public HeartRateMonitor(ulong address, string name) : base(address, name) { }

    public int Bpm => DataAge > 5 ? 0 : _bpm;

    protected override async Task<bool> SetupAsync()
    {
        var c = await GetCharacteristicAsync(BleUuids.HeartRate, BleUuids.HeartRateMeasurement);
        if (c == null) return false;
        return await SubscribeAsync(c, false, b =>
        {
            if (b.Length < 2) return;
            _bpm = (b[0] & 0x01) != 0 && b.Length >= 3 ? b[1] | (b[2] << 8) : b[1];
            MarkData();
        });
    }
}
