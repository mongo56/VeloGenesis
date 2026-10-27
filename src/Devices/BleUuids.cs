using System;

namespace VeloGenesis.Devices;

/// <summary>Bluetooth SIG assigned numbers used by fitness devices.</summary>
public static class BleUuids
{
    public static Guid Short(ushort id) => new($"0000{id:x4}-0000-1000-8000-00805f9b34fb");

    // Fitness Machine Service (FTMS) - used by the Cycplus R200 and most smart trainers.
    public static readonly Guid FitnessMachine = Short(0x1826);
    public static readonly Guid FtmsFeature = Short(0x2ACC);
    public static readonly Guid IndoorBikeData = Short(0x2AD2);
    public static readonly Guid FtmsControlPoint = Short(0x2AD9);
    public static readonly Guid FtmsStatus = Short(0x2ADA);

    // Cycling Power Service - power only, no control.
    public static readonly Guid CyclingPower = Short(0x1818);

    // Heart Rate Service.
    public static readonly Guid HeartRate = Short(0x180D);
    public static readonly Guid HeartRateMeasurement = Short(0x2A37);
}
