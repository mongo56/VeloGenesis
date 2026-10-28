using Godot;

namespace VeloGenesis.World;

/// <summary>
/// A themed region of the world: sky, fog, ground colours, road palettes and which scenery grows
/// there. The course moves into a new biome every few kilometres (see <see cref="CourseGenerator"/>).
/// </summary>
public class Biome
{
    public string Name;
    public string Tagline;

    // Sky / lighting
    public Color SkyTop, SkyHorizon, SunTop, SunBottom, CloudDark, GridColor, FogColor, Ambient, SunLight;
    /// <summary>0..1 skyline painted under the sun, and aurora curtains.</summary>
    public float City, Aurora;
    public Color AuroraColor = new(0.2f, 1f, 0.6f);

    // Ground: shadowed valleys and the two strata colours of the high slopes.
    public Color RockLow, RockHighA, RockHighB;

    /// <summary>Road palettes (primary, secondary) per sector kind.</summary>
    public (Color, Color)[] Climb, Descent, Flat, Rolling;

    // Scenery multipliers (1 = the original sunset strip).
    public float Mountains = 1f, Palms = 1f, Rocks = 1f, Stacks = 1f, Floaters = 1f, Beams = 1f;
    /// <summary>Chance of a megacity block on the horizon in flatter sectors, and its size.</summary>
    public float CityChance = 0.55f, CityBlocks = 1f;

    static readonly Color Cyan = new(0.1f, 0.9f, 1f), Magenta = new(1f, 0.15f, 0.8f), Orange = new(1f, 0.45f, 0.08f),
        Red = new(1f, 0.12f, 0.3f), Amber = new(1f, 0.75f, 0.15f), Violet = new(0.6f, 0.3f, 1f), Lime = new(0.45f, 1f, 0.3f),
        Teal = new(0.1f, 1f, 0.7f), Blue = new(0.25f, 0.45f, 1f), Pink = new(1f, 0.4f, 0.75f), Ice = new(0.6f, 0.9f, 1f),
        Gold = new(1f, 0.6f, 0.2f);

    public static readonly Biome[] All =
    {
        new()
        {
            Name = "Sunset Strip", Tagline = "WHERE IT ALL BEGINS",
            SkyTop = new(0.012f, 0f, 0.05f), SkyHorizon = new(0.38f, 0.03f, 0.36f),
            SunTop = new(1f, 0.86f, 0.25f), SunBottom = new(1f, 0.12f, 0.55f), CloudDark = new(0.07f, 0.01f, 0.12f),
            GridColor = new(0.6f, 0.15f, 0.9f), FogColor = new(0.07f, 0.01f, 0.13f), Ambient = new(0.3f, 0.14f, 0.45f),
            SunLight = new(1f, 0.5f, 0.5f), City = 1f,
            RockLow = new(0.045f, 0.02f, 0.085f), RockHighA = new(0.13f, 0.045f, 0.16f), RockHighB = new(0.2f, 0.07f, 0.12f),
            Climb = new[] { (Orange, Magenta), (Red, Amber), (Amber, Red) },
            Descent = new[] { (Cyan, Blue), (Teal, Cyan), (Blue, Violet) },
            Flat = new[] { (Lime, Cyan), (Teal, Violet) },
            Rolling = new[] { (Magenta, Cyan), (Violet, Pink), (Pink, Cyan) },
        },
        new()
        {
            Name = "Neon Metropolis", Tagline = "CITY OF A MILLION LIGHTS",
            SkyTop = new(0.004f, 0.004f, 0.04f), SkyHorizon = new(0.1f, 0.05f, 0.38f),
            SunTop = new(0.4f, 0.95f, 1f), SunBottom = new(1f, 0.1f, 0.7f), CloudDark = new(0.02f, 0.02f, 0.1f),
            GridColor = new(0.15f, 0.4f, 1f), FogColor = new(0.02f, 0.02f, 0.12f), Ambient = new(0.18f, 0.16f, 0.5f),
            SunLight = new(0.6f, 0.55f, 1f), City = 1f,
            RockLow = new(0.02f, 0.02f, 0.06f), RockHighA = new(0.06f, 0.06f, 0.14f), RockHighB = new(0.1f, 0.05f, 0.16f),
            Climb = new[] { (Magenta, Cyan), (Pink, Blue) },
            Descent = new[] { (Cyan, Magenta), (Blue, Pink) },
            Flat = new[] { (Cyan, Pink), (Violet, Cyan) },
            Rolling = new[] { (Blue, Magenta), (Pink, Cyan), (Violet, Teal) },
            Mountains = 0.6f, Palms = 0.25f, Rocks = 0.5f, Stacks = 0.3f, Floaters = 1.4f, Beams = 2.2f,
            CityChance = 1f, CityBlocks = 2.2f,
        },
        new()
        {
            Name = "Desert Grid", Tagline = "HEAT HAZE AND RED ROCK",
            SkyTop = new(0.05f, 0.008f, 0.03f), SkyHorizon = new(0.55f, 0.13f, 0.06f),
            SunTop = new(1f, 0.95f, 0.55f), SunBottom = new(1f, 0.32f, 0.08f), CloudDark = new(0.14f, 0.03f, 0.05f),
            GridColor = new(1f, 0.35f, 0.1f), FogColor = new(0.13f, 0.04f, 0.03f), Ambient = new(0.45f, 0.22f, 0.18f),
            SunLight = new(1f, 0.62f, 0.38f), City = 0.15f,
            RockLow = new(0.09f, 0.03f, 0.03f), RockHighA = new(0.26f, 0.09f, 0.05f), RockHighB = new(0.32f, 0.15f, 0.07f),
            Climb = new[] { (Orange, Red), (Red, Gold) },
            Descent = new[] { (Amber, Teal), (Gold, Cyan) },
            Flat = new[] { (Amber, Orange), (Teal, Gold) },
            Rolling = new[] { (Gold, Magenta), (Orange, Teal), (Red, Amber) },
            Mountains = 1.1f, Palms = 0.45f, Rocks = 1.9f, Stacks = 2.2f, Floaters = 0.4f, Beams = 0.5f,
            CityChance = 0.08f,
        },
        new()
        {
            Name = "Aurora Peaks", Tagline = "COLD AIR, HIGH MOUNTAINS",
            SkyTop = new(0f, 0.01f, 0.04f), SkyHorizon = new(0.03f, 0.2f, 0.3f),
            SunTop = new(0.75f, 1f, 0.95f), SunBottom = new(0.25f, 0.45f, 1f), CloudDark = new(0.01f, 0.04f, 0.08f),
            GridColor = new(0.1f, 0.9f, 0.8f), FogColor = new(0.01f, 0.05f, 0.08f), Ambient = new(0.14f, 0.3f, 0.42f),
            SunLight = new(0.6f, 0.85f, 1f), City = 0f, Aurora = 1f, AuroraColor = new(0.15f, 1f, 0.55f),
            RockLow = new(0.02f, 0.04f, 0.07f), RockHighA = new(0.1f, 0.14f, 0.2f), RockHighB = new(0.22f, 0.26f, 0.32f),
            Climb = new[] { (Ice, Violet), (Teal, Blue) },
            Descent = new[] { (Cyan, Teal), (Violet, Ice) },
            Flat = new[] { (Lime, Teal), (Teal, Violet) },
            Rolling = new[] { (Teal, Pink), (Ice, Lime), (Violet, Cyan) },
            Mountains = 1.55f, Palms = 0.03f, Rocks = 1.1f, Stacks = 0.6f, Floaters = 2f, Beams = 0.8f,
            CityChance = 0f,
        },
    };

    public static Biome Clone(Biome b) => (Biome)b.MemberwiseClone();

    /// <summary>Moves the sky / lighting fields of <paramref name="look"/> a fraction t towards <paramref name="to"/>.</summary>
    public static void Blend(Biome look, Biome to, float t)
    {
        look.SkyTop = look.SkyTop.Lerp(to.SkyTop, t);
        look.SkyHorizon = look.SkyHorizon.Lerp(to.SkyHorizon, t);
        look.SunTop = look.SunTop.Lerp(to.SunTop, t);
        look.SunBottom = look.SunBottom.Lerp(to.SunBottom, t);
        look.CloudDark = look.CloudDark.Lerp(to.CloudDark, t);
        look.GridColor = look.GridColor.Lerp(to.GridColor, t);
        look.FogColor = look.FogColor.Lerp(to.FogColor, t);
        look.Ambient = look.Ambient.Lerp(to.Ambient, t);
        look.SunLight = look.SunLight.Lerp(to.SunLight, t);
        look.AuroraColor = look.AuroraColor.Lerp(to.AuroraColor, t);
        look.City = Mathf.Lerp(look.City, to.City, t);
        look.Aurora = Mathf.Lerp(look.Aurora, to.Aurora, t);
    }

    public (Color, Color)[] Palettes(SectorKind kind) => kind switch
    {
        SectorKind.Climb => Climb,
        SectorKind.Descent => Descent,
        SectorKind.Flat => Flat,
        SectorKind.Start => new[] { (Cyan, Magenta) },
        _ => Rolling,
    };
}
