using System;

namespace VeloGenesis.World;

/// <summary>Deterministic, allocation-free, thread-safe value noise (safe to use from worker threads).</summary>
public static class Noise
{
    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 8388607.5f - 1f; // [-1, 1]
        }
    }

    static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    public static float Value1(float x, int seed)
    {
        int xi = (int)MathF.Floor(x);
        float t = Fade(x - xi);
        return Lerp(Hash(xi, 0, seed), Hash(xi + 1, 0, seed), t);
    }

    public static float Value2(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float tx = Fade(x - xi), ty = Fade(y - yi);
        float a = Lerp(Hash(xi, yi, seed), Hash(xi + 1, yi, seed), tx);
        float b = Lerp(Hash(xi, yi + 1, seed), Hash(xi + 1, yi + 1, seed), tx);
        return Lerp(a, b, ty);
    }

    /// <summary>Fractal noise in roughly [-1, 1].</summary>
    public static float Fbm2(float x, float y, int seed, int octaves)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value2(x, y, seed + o * 131) * amp;
            norm += amp;
            x = x * 2.03f + 17.1f;
            y = y * 2.03f - 9.7f;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    /// <summary>Ridged fractal noise in [0, 1] – sharp mountain crests.</summary>
    public static float Ridged2(float x, float y, int seed, int octaves)
    {
        float sum = 0, amp = 0.5f, norm = 0, prev = 1f;
        for (int o = 0; o < octaves; o++)
        {
            float n = 1f - MathF.Abs(Value2(x, y, seed + o * 71));
            n *= n;
            sum += n * amp * prev;
            prev = n;
            norm += amp;
            x = x * 2.1f + 5.3f;
            y = y * 2.1f + 1.7f;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
