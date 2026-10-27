using System;
using System.Collections.Generic;
using System.IO;

namespace VeloGenesis.Ride;

/// <summary>
/// Minimal Garmin FIT activity encoder (file_id, events, records, lap, session, activity).
/// Produces files Strava / Garmin Connect / intervals.icu accept as a virtual ride.
/// </summary>
public static class FitWriter
{
    static readonly DateTime FitEpoch = new(1989, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    // FIT base types
    const byte Enum = 0x00, UInt8 = 0x02, SInt32 = 0x85, UInt16 = 0x84, UInt32 = 0x86, UInt32z = 0x8C;

    // Ocean origin used for the optional virtual GPS track (South Atlantic, far from any land).
    const double OriginLat = -15.0, OriginLon = -25.0;

    public static void Write(string path, RideRecorder rec, RideStats st, bool virtualGps)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        uint start = Ts(rec.StartTime);
        uint end = rec.Samples.Count > 0 ? Ts(rec.Samples[^1].Time) : start;

        // file_id (global 0)
        Define(w, 0, 0, (0, 1, Enum), (1, 2, UInt16), (2, 2, UInt16), (3, 4, UInt32z), (4, 4, UInt32));
        w.Write((byte)0); w.Write((byte)4); w.Write((ushort)255); w.Write((ushort)1); w.Write((uint)(start ^ 0x5A5A5A5A)); w.Write(start);

        // event (global 21): timer start
        Define(w, 1, 21, (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum));
        w.Write((byte)1); w.Write(start); w.Write((byte)0); w.Write((byte)0);

        // record (global 20)
        var fields = new List<(byte, byte, byte)> { (253, 4, UInt32), (2, 2, UInt16), (3, 1, UInt8), (4, 1, UInt8), (5, 4, UInt32), (6, 2, UInt16), (7, 2, UInt16) };
        if (virtualGps) { fields.Add((0, 4, SInt32)); fields.Add((1, 4, SInt32)); }
        Define(w, 2, 20, fields.ToArray());
        foreach (var s in rec.Samples)
        {
            w.Write((byte)2);
            w.Write(Ts(s.Time));
            w.Write((ushort)Math.Clamp((s.Altitude + 500f) * 5f, 0, 65534));
            w.Write((byte)(s.HeartRate > 0 ? Math.Min(s.HeartRate, 254) : 0xFF));
            w.Write((byte)Math.Clamp(s.Cadence, 0, 254));
            w.Write((uint)Math.Max(0, s.Distance * 100f));
            w.Write((ushort)Math.Clamp(s.Speed * 1000f, 0, 65534));
            w.Write((ushort)Math.Clamp(s.Power, 0, 65534));
            if (virtualGps)
            {
                double lat = OriginLat + -s.Z / 111320.0;
                double lon = OriginLon + s.X / (111320.0 * Math.Cos(OriginLat * Math.PI / 180.0));
                w.Write(Semicircles(lat));
                w.Write(Semicircles(lon));
            }
        }

        // event: timer stop all
        w.Write((byte)1); w.Write(end); w.Write((byte)0); w.Write((byte)4);

        uint elapsedMs = (uint)(st.ElapsedSeconds * 1000);
        uint timerMs = (uint)(Math.Max(st.MovingSeconds, 1) * 1000);
        uint dist = (uint)(st.DistanceM * 100);
        byte avgHr = (byte)(st.AvgHr > 0 ? Math.Min(st.AvgHr, 254) : 0xFF);
        byte maxHr = (byte)(st.MaxHr > 0 ? Math.Min(st.MaxHr, 254) : 0xFF);

        // lap (global 19)
        Define(w, 3, 19, (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum), (2, 4, UInt32), (7, 4, UInt32), (8, 4, UInt32), (9, 4, UInt32),
            (15, 1, UInt8), (16, 1, UInt8), (17, 1, UInt8), (19, 2, UInt16), (20, 2, UInt16), (21, 2, UInt16), (22, 2, UInt16), (25, 1, Enum), (39, 1, Enum));
        w.Write((byte)3); w.Write(end); w.Write((byte)9); w.Write((byte)1); w.Write(start); w.Write(elapsedMs); w.Write(timerMs); w.Write(dist);
        w.Write(avgHr); w.Write(maxHr); w.Write((byte)Math.Min(st.AvgCadence, 254)); w.Write((ushort)st.AvgPower); w.Write((ushort)st.MaxPower);
        w.Write((ushort)st.Ascent); w.Write((ushort)st.Descent); w.Write((byte)2); w.Write((byte)58);

        // session (global 18): sport cycling (2), sub sport virtual_activity (58)
        Define(w, 4, 18, (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum), (2, 4, UInt32), (5, 1, Enum), (6, 1, Enum), (7, 4, UInt32), (8, 4, UInt32),
            (9, 4, UInt32), (11, 2, UInt16), (14, 2, UInt16), (15, 2, UInt16), (16, 1, UInt8), (17, 1, UInt8), (18, 1, UInt8), (19, 1, UInt8),
            (20, 2, UInt16), (21, 2, UInt16), (22, 2, UInt16), (23, 2, UInt16), (25, 2, UInt16), (26, 2, UInt16), (34, 2, UInt16));
        w.Write((byte)4); w.Write(end); w.Write((byte)8); w.Write((byte)1); w.Write(start); w.Write((byte)2); w.Write((byte)58);
        w.Write(elapsedMs); w.Write(timerMs); w.Write(dist); w.Write((ushort)st.Kilojoules);
        w.Write((ushort)Math.Clamp(st.AvgSpeed * 1000, 0, 65534)); w.Write((ushort)Math.Clamp(st.MaxSpeed * 1000, 0, 65534));
        w.Write(avgHr); w.Write(maxHr); w.Write((byte)Math.Min(st.AvgCadence, 254)); w.Write((byte)Math.Min(st.MaxCadence, 254));
        w.Write((ushort)st.AvgPower); w.Write((ushort)st.MaxPower); w.Write((ushort)st.Ascent); w.Write((ushort)st.Descent);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)st.NormalizedPower);

        // activity (global 34)
        Define(w, 5, 34, (253, 4, UInt32), (0, 4, UInt32), (1, 2, UInt16), (2, 1, Enum), (3, 1, Enum), (4, 1, Enum), (5, 4, UInt32));
        int offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
        w.Write((byte)5); w.Write(end); w.Write(timerMs); w.Write((ushort)1); w.Write((byte)0); w.Write((byte)26); w.Write((byte)1); w.Write((uint)(end + offset));

        w.Flush();
        var data = body.ToArray();

        using var f = File.Create(path);
        var header = new byte[14];
        header[0] = 14;
        header[1] = 0x20; // protocol 2.0
        BitConverter.GetBytes((ushort)2132).CopyTo(header, 2);
        BitConverter.GetBytes((uint)data.Length).CopyTo(header, 4);
        header[8] = (byte)'.'; header[9] = (byte)'F'; header[10] = (byte)'I'; header[11] = (byte)'T';
        BitConverter.GetBytes(Crc(header, 0, 12, 0)).CopyTo(header, 12);
        f.Write(header);
        f.Write(data);
        ushort crc = Crc(header, 0, 14, 0);
        crc = Crc(data, 0, data.Length, crc);
        f.Write(BitConverter.GetBytes(crc));
    }

    static void Define(BinaryWriter w, byte local, ushort global, params (byte num, byte size, byte type)[] fields)
    {
        w.Write((byte)(0x40 | local));
        w.Write((byte)0);      // reserved
        w.Write((byte)0);      // little endian
        w.Write(global);
        w.Write((byte)fields.Length);
        foreach (var (num, size, type) in fields) { w.Write(num); w.Write(size); w.Write(type); }
    }

    static uint Ts(DateTime t) => (uint)(t.ToUniversalTime() - FitEpoch).TotalSeconds;
    static int Semicircles(double deg) => (int)(deg * (2147483648.0 / 180.0));

    static readonly ushort[] CrcTable =
    {
        0x0000, 0xCC01, 0xD801, 0x1400, 0xF001, 0x3C00, 0x2800, 0xE401,
        0xA001, 0x6C00, 0x7800, 0xB401, 0x5000, 0x9C01, 0x8801, 0x4400,
    };

    static ushort Crc(byte[] data, int start, int count, ushort crc)
    {
        for (int i = start; i < start + count; i++)
        {
            byte b = data[i];
            ushort tmp = CrcTable[crc & 0xF];
            crc = (ushort)((crc >> 4) & 0x0FFF);
            crc = (ushort)(crc ^ tmp ^ CrcTable[b & 0xF]);
            tmp = CrcTable[crc & 0xF];
            crc = (ushort)((crc >> 4) & 0x0FFF);
            crc = (ushort)(crc ^ tmp ^ CrcTable[(b >> 4) & 0xF]);
        }
        return crc;
    }
}
