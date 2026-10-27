using System;
using Godot;

namespace VeloGenesis.Ride;

/// <summary>Standard 7-zone power model (Coggan), as used by most smart trainer apps.</summary>
public static class PowerZones
{
    /// <summary>Upper bound of each zone as a fraction of FTP (Z7 is open-ended).</summary>
    static readonly float[] Upper = { 0.55f, 0.75f, 0.90f, 1.05f, 1.20f, 1.50f, float.MaxValue };

    public static readonly string[] Names = { "Active Recovery", "Endurance", "Tempo", "Threshold", "VO2 Max", "Anaerobic", "Neuromuscular" };

    public static readonly Color[] Colors =
    {
        new(0.62f, 0.62f, 0.72f), // Z1 grey
        new(0.25f, 0.55f, 1f),    // Z2 blue
        new(0.3f, 1f, 0.45f),     // Z3 green
        new(1f, 0.85f, 0.2f),     // Z4 yellow
        new(1f, 0.5f, 0.15f),     // Z5 orange
        new(1f, 0.2f, 0.3f),      // Z6 red
        new(0.85f, 0.3f, 1f),     // Z7 purple
    };

    /// <summary>Zone number 1..7 for a fraction of FTP.</summary>
    public static int Zone(float pctFtp)
    {
        for (int i = 0; i < Upper.Length; i++)
            if (pctFtp <= Upper[i]) return i + 1;
        return 7;
    }

    public static int Zone(float watts, int ftp) => Zone(watts / Math.Max(1, ftp));
    public static string Name(int zone) => Names[Math.Clamp(zone, 1, 7) - 1];
    public static Color Color(int zone) => Colors[Math.Clamp(zone, 1, 7) - 1];

    /// <summary>Lower bound of a zone in watts (for "Z4 = 201–231 W" style labels).</summary>
    public static int LowerWatts(int zone, int ftp) => zone <= 1 ? 0 : (int)MathF.Round(Upper[zone - 2] * ftp) + 1;
    public static int UpperWatts(int zone, int ftp) => zone >= 7 ? int.MaxValue : (int)MathF.Round(Upper[zone - 1] * ftp);

    /// <summary>Power gels only light up (and can be collected) from this zone upwards.</summary>
    public const int GelZone = 4;
}
