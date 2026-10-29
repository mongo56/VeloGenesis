using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace VeloGenesis.Core;

/// <summary>Rider profile and app preferences, persisted as JSON in the Godot user folder.</summary>
public class Settings
{
    public int Ftp = 220;
    public float RiderWeightKg = 75f;
    public float BikeWeightKg = 9f;

    public string Quality = "High"; // Low, Medium, High, Ultra
    public bool ShowFps = false;
    /// <summary>Fade the secondary HUD panels during long uneventful stretches.</summary>
    public bool SteadyHud = true;
    /// <summary>One of Quality.AaModes.</summary>
    public string AntiAliasing = "Auto";
    /// <summary>One of Display.Modes: Windowed, Borderless, Exclusive.</summary>
    public string DisplayMode = "Windowed";
    /// <summary>Fullscreen mode F11 returns to.</summary>
    public string LastFullscreenMode = "Borderless";
    /// <summary>Windowed-mode size; 0 = default.</summary>
    public int WindowWidth, WindowHeight;
    /// <summary>3D render scale (0 = by quality preset). Upscaled with FSR below 100%.</summary>
    public float RenderScale;

    /// <summary>0..1 scale applied to the grade sent to the trainer in free ride (like "trainer difficulty").</summary>
    public float TrainerDifficulty = 0.5f;
    public bool VirtualShifting = true;

    public string MusicSource = "Procedural"; // Procedural, Files, Both, External
    public float MusicVolume = 0.5f;
    public float SfxVolume = 0.8f;
    public float WindVolume = 0.5f;

    /// <summary>Write a virtual GPS track (in the open ocean) to FIT files so Strava draws a map.</summary>
    public bool VirtualGps = false;
    /// <summary>Upload each saved ride to Strava as soon as the summary screen opens (when connected).</summary>
    public bool StravaAutoUpload = false;

    /// <summary>Custom key bindings per action (physical keycodes, 0 = empty slot). Missing = defaults.</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<long>> KeyBindings = new();
    /// <summary>Custom gamepad bindings per action (JoyButton values, -1 = empty slot).</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<long>> PadBindings = new();

    public ulong LastTrainerAddress;
    public string LastTrainerName = "";
    public ulong LastHrAddress;
    public string LastHrName = "";

    // Last used ride setup.
    public string LastMode = "FreeRide";
    public float LastAvgGradient = 4f;
    public float LastElevationGain = 400f;
    public float LastDistanceKm = 25f;
    public bool LastClimb = true;
    public bool LastPowerGels = true;
    public bool LastGhostRaces = true;
    public string LastWorkoutType = "SweetSpot";
    public int LastWorkoutMinutes = 60;

    /// <summary>Bumped when defaults change so saved settings can be migrated.</summary>
    public int Version;
    const int CurrentVersion = 2;

    public static Settings Current { get; private set; } = new();

    static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };

    static string FilePath => ProjectSettings.GlobalizePath("user://settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOpts) ?? new Settings();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Settings load failed, using defaults: {e.Message}");
            Current = new Settings();
        }

        if (Current.Version < 2)
        {
            // v2: music default lowered from 70% to 50%.
            if (MathF.Abs(Current.MusicVolume - 0.7f) < 0.01f) Current.MusicVolume = 0.5f;
        }
        if (Current.Version != CurrentVersion)
        {
            Current.Version = CurrentVersion;
            Save();
        }
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch (Exception e) { GD.PrintErr($"Settings save failed: {e.Message}"); }
    }

    public float TotalMassKg => RiderWeightKg + BikeWeightKg;
}
