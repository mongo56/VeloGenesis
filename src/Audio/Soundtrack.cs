using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace VeloGenesis.Audio;

/// <summary>
/// The bundled soundtrack: assets/music/soundtrack.json, written by tools/music/generate.py (ACE-Step 1.5).
/// Tracks are grouped by biome key; the keys follow the order of <see cref="World.Biome.All"/>.
/// </summary>
public static class Soundtrack
{
    public record Track(string Title, string Biome, string File);

    public static readonly string[] BiomeKeys = { "sunset", "metropolis", "desert", "aurora" };
    const string Dir = "res://assets/music/";

    static List<Track> _tracks;
    public static IReadOnlyList<Track> Tracks => _tracks ??= Load();

    static List<Track> Load()
    {
        try
        {
            if (!FileAccess.FileExists(Dir + "soundtrack.json")) return new();
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Dir + "soundtrack.json"));
            return doc.RootElement.GetProperty("tracks").EnumerateArray()
                .Select(t => new Track(t.GetProperty("title").GetString(), t.GetProperty("biome").GetString(), t.GetProperty("file").GetString()))
                .Where(t => ResourceLoader.Exists(Dir + t.File))
                .ToList();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Soundtrack manifest unreadable: {e.Message}");
            return new();
        }
    }

    public static AudioStream Stream(Track t) => GD.Load<AudioStream>(Dir + t.File);

    public static string BiomeKey(int biome) => BiomeKeys[((biome % BiomeKeys.Length) + BiomeKeys.Length) % BiomeKeys.Length];
}
