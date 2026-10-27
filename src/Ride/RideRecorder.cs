using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloGenesis.Ride;

public struct RideSample
{
    public DateTime Time;
    public float Distance;  // m
    public float Speed;     // m/s
    public int Power;
    public int Cadence;
    public int HeartRate;
    public float Altitude;  // m
    public float X, Z;      // world position
}

public class RideStats
{
    public double ElapsedSeconds;
    public double MovingSeconds;
    public float DistanceM;
    public float AvgPower, MaxPower, NormalizedPower;
    public float AvgHr, MaxHr;
    public float AvgCadence, MaxCadence;
    public float AvgSpeed, MaxSpeed;
    public float Ascent, Descent;
    public float Kilojoules;
    public float Tss, IntensityFactor;
}

/// <summary>Collects 1 Hz samples during a ride and computes summary stats.</summary>
public class RideRecorder
{
    public readonly List<RideSample> Samples = new();
    public DateTime StartTime { get; private set; }
    public double MovingSeconds;
    float _lastAlt = float.NaN;
    public float Ascent, Descent;

    public void Begin() => StartTime = DateTime.UtcNow;

    public void Add(RideSample s)
    {
        if (!float.IsNaN(_lastAlt))
        {
            float d = s.Altitude - _lastAlt;
            if (d > 0) Ascent += d; else Descent -= d;
        }
        _lastAlt = s.Altitude;
        Samples.Add(s);
    }

    public RideStats Compute(int ftp)
    {
        var st = new RideStats { MovingSeconds = MovingSeconds, Ascent = Ascent, Descent = Descent };
        if (Samples.Count == 0) return st;
        st.ElapsedSeconds = (Samples[^1].Time - StartTime).TotalSeconds;
        st.MovingSeconds = Math.Min(MovingSeconds, st.ElapsedSeconds);
        st.DistanceM = Samples[^1].Distance;
        st.AvgPower = (float)Samples.Average(s => s.Power);
        st.MaxPower = Samples.Max(s => s.Power);
        var hr = Samples.Where(s => s.HeartRate > 0).ToList();
        if (hr.Count > 0) { st.AvgHr = (float)hr.Average(s => s.HeartRate); st.MaxHr = hr.Max(s => s.HeartRate); }
        var cad = Samples.Where(s => s.Cadence > 0).ToList();
        if (cad.Count > 0) { st.AvgCadence = (float)cad.Average(s => s.Cadence); st.MaxCadence = cad.Max(s => s.Cadence); }
        st.MaxSpeed = Samples.Max(s => s.Speed);
        st.AvgSpeed = MovingSeconds > 1 ? (float)(st.DistanceM / MovingSeconds) : 0;
        st.Kilojoules = Samples.Sum(s => s.Power) / 1000f;

        // Normalized power: 30 s rolling average, raised to 4th power, averaged, 4th root.
        if (Samples.Count >= 30)
        {
            double acc = 0, window = 0;
            int n = 0;
            for (int i = 0; i < Samples.Count; i++)
            {
                window += Samples[i].Power;
                if (i >= 30) window -= Samples[i - 30].Power;
                if (i >= 29) { acc += Math.Pow(window / 30.0, 4); n++; }
            }
            st.NormalizedPower = (float)Math.Pow(acc / n, 0.25);
        }
        else st.NormalizedPower = st.AvgPower;

        if (ftp > 0)
        {
            st.IntensityFactor = st.NormalizedPower / ftp;
            st.Tss = (float)(Samples.Count * st.NormalizedPower * st.IntensityFactor / (ftp * 3600.0) * 100.0);
        }
        return st;
    }
}
