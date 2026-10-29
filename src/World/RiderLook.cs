using System;
using Godot;

namespace VeloGenesis.World;

/// <summary>Build and kit of a rider avatar: body type, skin and hair, jersey, shorts, socks, shoes and bike.</summary>
public class RiderLook
{
    public bool Female;
    /// <summary>Overall size of rider and bike (1 = the player's 56 cm bike).</summary>
    public float Size = 1f;
    public Color Skin = new(0.56f, 0.37f, 0.26f);
    public Color Hair = new(0.18f, 0.05f, 0.25f);   // deep violet
    /// <summary>Neon magenta: the synthwave partner of the default cyan frame.</summary>
    public Color Jersey = new(0.6f, 0.04f, 0.36f);
    public Color Shorts = new(0.035f, 0.02f, 0.05f);
    public Color Socks = new(0.04f, 0.55f, 0.65f);  // cyan, like the frame
    public Color Shoes = new(0.07f, 0.05f, 0.12f);  // midnight
    public Color Frame = new(0.1f, 0.9f, 1f);

    // Synthwave kit: deep neon jerseys (the rim light brightens them), near-black shorts with a purple or blue
    // cast, and accents that pick up the jersey or the bike's neon.
    static readonly Color[] Jerseys =
    {
        new(0.6f, 0.04f, 0.36f),  // magenta
        new(0.75f, 0.1f, 0.3f),   // hot pink
        new(0.38f, 0.08f, 0.62f), // electric purple
        new(0.22f, 0.06f, 0.45f), // violet
        new(0.02f, 0.5f, 0.6f),   // cyan
        new(0.05f, 0.2f, 0.65f),  // electric blue
        new(0.85f, 0.32f, 0.05f), // sunset orange
        new(0.85f, 0.25f, 0.3f),  // coral
        new(0.06f, 0.05f, 0.1f),  // midnight
        new(0.82f, 0.8f, 0.88f),  // white
    };
    static readonly Color[] ShortsColors =
    {
        new(0.035f, 0.02f, 0.05f), new(0.03f, 0.03f, 0.035f), new(0.06f, 0.025f, 0.11f), new(0.03f, 0.04f, 0.12f), new(0.02f, 0.07f, 0.09f),
    };
    static readonly Color[] Skins =
    {
        new(0.9f, 0.7f, 0.58f), new(0.8f, 0.58f, 0.44f), new(0.66f, 0.45f, 0.32f), new(0.5f, 0.32f, 0.2f), new(0.32f, 0.2f, 0.13f),
    };
    // Hair in the game's palette: dyed violet, magenta, teal, electric blue or lavender platinum, or blue-black.
    static readonly Color[] Hairs =
    {
        new(0.18f, 0.05f, 0.25f), new(0.4f, 0.06f, 0.28f), new(0.03f, 0.25f, 0.3f), new(0.06f, 0.1f, 0.35f),
        new(0.55f, 0.5f, 0.62f), new(0.04f, 0.035f, 0.07f),
    };
    // Socks and shoes: neon accents, midnight, or soft lavender-white.
    static readonly Color[] Accents =
    {
        new(0.04f, 0.55f, 0.65f), new(0.6f, 0.04f, 0.36f), new(0.38f, 0.08f, 0.62f), new(0.07f, 0.05f, 0.12f), new(0.78f, 0.74f, 0.86f),
    };
    static readonly Color[] Frames =
    {
        new(0.1f, 0.9f, 1f), new(1f, 0.15f, 0.8f), new(1f, 0.45f, 0.08f), new(0.45f, 1f, 0.3f), new(0.6f, 0.3f, 1f), new(0.9f, 0.9f, 0.95f),
    };

    static T Pick<T>(Random rng, T[] options) => options[rng.Next(options.Length)];

    /// <summary>A random rider: kit colours that go together, any skin tone, and a matching bike size.</summary>
    public static RiderLook Random(Random rng, bool female)
    {
        var jersey = Pick(rng, Jerseys);
        var frame = Pick(rng, Frames);
        return new RiderLook
        {
            Female = female,
            Size = female ? 0.93f + (float)rng.NextDouble() * 0.05f : 0.98f + (float)rng.NextDouble() * 0.05f,
            Skin = Pick(rng, Skins),
            Hair = Pick(rng, Hairs),
            Jersey = jersey,
            // Near-black shorts; sometimes a deep version of the jersey colour.
            Shorts = rng.NextDouble() < 0.25 ? jersey.Darkened(0.8f) : Pick(rng, ShortsColors),
            // Socks and shoes from the accent palette, sometimes picking up the jersey or the bike's neon.
            Socks = rng.Next(4) switch { 0 => jersey, 1 => frame.Darkened(0.35f), _ => Pick(rng, Accents) },
            Shoes = rng.Next(3) switch { 0 => frame.Darkened(0.45f), _ => Pick(rng, Accents) },
            Frame = frame,
        };
    }
}
