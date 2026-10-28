using System;
using Godot;

namespace VeloGenesis.World;

/// <summary>Build and kit of a rider avatar: body type, skin and hair, jersey, shorts, helmet, socks and bike.</summary>
public class RiderLook
{
    public bool Female;
    /// <summary>Overall size of rider and bike (1 = the player's 56 cm bike).</summary>
    public float Size = 1f;
    public Color Skin = new(0.56f, 0.37f, 0.26f);
    public Color Hair = new(0.08f, 0.05f, 0.03f);
    public Color Jersey = new(0.8f, 0.58f, 0.07f);
    public Color Shorts = new(0.03f, 0.03f, 0.035f);
    public Color Helmet = new(0.86f, 0.87f, 0.89f);
    public Color Socks = new(0.86f, 0.87f, 0.89f);
    public Color Shoes = new(0.86f, 0.87f, 0.89f);
    public Color Frame = new(0.1f, 0.9f, 1f);

    static readonly Color[] Jerseys =
    {
        new(0.75f, 0.06f, 0.08f), new(0.08f, 0.22f, 0.7f), new(0.02f, 0.55f, 0.55f), new(0.88f, 0.88f, 0.9f),
        new(0.05f, 0.05f, 0.06f), new(0.95f, 0.4f, 0.05f), new(0.9f, 0.3f, 0.55f), new(0.45f, 0.8f, 0.1f),
        new(0.4f, 0.15f, 0.65f), new(0.35f, 0.65f, 0.95f), new(0.5f, 0.85f, 0.7f), new(0.95f, 0.8f, 0.1f),
    };
    static readonly Color[] ShortsColors = { new(0.03f, 0.03f, 0.035f), new(0.03f, 0.05f, 0.14f), new(0.12f, 0.12f, 0.13f), new(0.2f, 0.16f, 0.1f) };
    static readonly Color[] Skins =
    {
        new(0.9f, 0.7f, 0.58f), new(0.8f, 0.58f, 0.44f), new(0.66f, 0.45f, 0.32f), new(0.5f, 0.32f, 0.2f), new(0.32f, 0.2f, 0.13f),
    };
    static readonly Color[] Hairs =
    {
        new(0.03f, 0.025f, 0.02f), new(0.15f, 0.08f, 0.04f), new(0.32f, 0.18f, 0.08f), new(0.75f, 0.58f, 0.3f), new(0.5f, 0.17f, 0.06f),
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
        var white = new Color(0.86f, 0.87f, 0.89f);
        var black = new Color(0.04f, 0.04f, 0.045f);
        return new RiderLook
        {
            Female = female,
            Size = female ? 0.93f + (float)rng.NextDouble() * 0.05f : 0.98f + (float)rng.NextDouble() * 0.05f,
            Skin = Pick(rng, Skins),
            Hair = Pick(rng, Hairs),
            Jersey = jersey,
            // Mostly dark shorts; sometimes a deep version of the jersey colour.
            Shorts = rng.NextDouble() < 0.25 ? jersey.Darkened(0.7f) : Pick(rng, ShortsColors),
            Helmet = rng.Next(3) switch { 0 => white, 1 => black, _ => jersey.Lerp(white, 0.15f) },
            Socks = rng.NextDouble() < 0.6 ? white : rng.NextDouble() < 0.5 ? black : jersey,
            Shoes = rng.NextDouble() < 0.6 ? white : black,
            Frame = Pick(rng, Frames),
        };
    }
}
