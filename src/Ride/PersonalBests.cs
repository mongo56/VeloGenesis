using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace VeloGenesis.Ride;

/// <summary>One best effort: its average power and the second-by-second power that produced it.</summary>
public class Effort
{
    public int Seconds;
    public float Watts;
    public DateTime Date;
    public int[] Trace = Array.Empty<int>();
}

/// <summary>
/// Your best efforts over a set of durations, all-time and from the most recent ride, persisted in
/// the Godot user folder. Self-ghosts replay these traces so you race the you of another day.
/// </summary>
public class PersonalBests
{
    public static readonly int[] Durations = { 60, 120, 180, 240, 300, 420, 600 };

    public Dictionary<int, Effort> Best = new();
    public Dictionary<int, Effort> Last = new();

    public static PersonalBests Current { get; private set; } = new();

    static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };
    static string FilePath => ProjectSettings.GlobalizePath("user://bests.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<PersonalBests>(File.ReadAllText(FilePath), JsonOpts) ?? new PersonalBests();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Personal bests load failed: {e.Message}");
            Current = new PersonalBests();
        }
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch (Exception e) { GD.PrintErr($"Personal bests save failed: {e.Message}"); }
    }

    /// <summary>Best effort of at least <paramref name="seconds"/> to replay (null when none yet).</summary>
    public Effort For(int seconds, bool allTime)
    {
        var table = allTime ? Best : Last;
        foreach (int d in Durations)
            if (d >= seconds && table.TryGetValue(d, out var e)) return e;
        return null;
    }

    /// <summary>
    /// Scans a finished ride (1 Hz power) for its best effort at each duration, makes them the
    /// "last ride" set and returns the durations that set a new all-time best.
    /// </summary>
    public List<Effort> Record(IReadOnlyList<RideSample> samples, DateTime date)
    {
        var newBests = new List<Effort>();
        foreach (int d in Durations)
        {
            if (samples.Count < d) break;
            long window = 0, best = -1;
            int bestEnd = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                window += samples[i].Power;
                if (i >= d) window -= samples[i - d].Power;
                if (i >= d - 1 && window > best) { best = window; bestEnd = i; }
            }
            float watts = best / (float)d;
            if (watts < 30) continue;
            var trace = new int[d];
            for (int k = 0; k < d; k++) trace[k] = samples[bestEnd - d + 1 + k].Power;
            var effort = new Effort { Seconds = d, Watts = watts, Date = date, Trace = trace };
            Last[d] = effort;
            if (!Best.TryGetValue(d, out var prev) || watts > prev.Watts + 0.5f)
            {
                if (prev != null) newBests.Add(effort);
                Best[d] = effort;
            }
        }
        Save();
        return newBests;
    }

    public static string DurationName(int seconds) => seconds < 60 ? $"{seconds} s" : $"{seconds / 60} MIN";
}
