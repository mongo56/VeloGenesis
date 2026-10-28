using Godot;

namespace VeloGenesis.Core;

/// <summary>Graphics quality profile. Controls render settings and procedural world density.</summary>
public class Quality
{
    public string Name;
    public Viewport.Msaa Msaa;
    public bool Fxaa;
    public float RenderScale;
    public int GlowLevels;
    public bool Shadows;
    public bool Ssr;
    public bool VolumetricFog;
    /// <summary>Spacing in metres between real lights on the road-edge pylons (0 = none).</summary>
    public float PylonLightSpacing;
    /// <summary>Film grain / chromatic aberration / vignette pass.</summary>
    public bool PostFx;
    /// <summary>Lateral terrain column offsets (metres from road centre) per side.</summary>
    public float[] TerrainColumns;
    /// <summary>Use every Nth path sample for terrain rows.</summary>
    public int TerrainRowStep;
    public float SceneryDensity;
    public float ViewAhead;
    public float FogDensity;
    public float CameraFar;

    static readonly float[] ColsLow = { 4, 9, 18, 32, 55, 85, 125, 175, 230 };
    static readonly float[] ColsMed = { 4, 7, 12, 19, 28, 40, 56, 76, 100, 130, 170, 230 };
    static readonly float[] ColsHigh = { 4, 6, 9, 13, 18, 24, 32, 42, 54, 68, 85, 105, 128, 155, 190, 230 };
    static readonly float[] ColsUltra = { 4, 5.5f, 7.5f, 10, 13, 17, 22, 28, 35, 43, 52, 63, 76, 91, 108, 128, 150, 175, 202, 230 };

    public static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };

    public static Quality Get(string name) => name switch
    {
        "Low" => new Quality { Name = "Low", Msaa = Viewport.Msaa.Disabled, Fxaa = true, RenderScale = 0.7f, GlowLevels = 3, TerrainColumns = ColsLow, TerrainRowStep = 4, SceneryDensity = 0.3f, ViewAhead = 1600, FogDensity = 0.0019f, CameraFar = 1900 },
        "Medium" => new Quality { Name = "Medium", Msaa = Viewport.Msaa.Msaa2X, RenderScale = 0.85f, GlowLevels = 4, TerrainColumns = ColsMed, TerrainRowStep = 3, SceneryDensity = 0.55f, ViewAhead = 2100, FogDensity = 0.0015f, CameraFar = 2400, PylonLightSpacing = 160, PostFx = true },
        "Ultra" => new Quality { Name = "Ultra", Msaa = Viewport.Msaa.Msaa4X, RenderScale = 1f, GlowLevels = 6, Shadows = true, Ssr = true, VolumetricFog = true, PylonLightSpacing = 40, PostFx = true, TerrainColumns = ColsUltra, TerrainRowStep = 2, SceneryDensity = 1f, ViewAhead = 3000, FogDensity = 0.0011f, CameraFar = 3400 },
        _ => new Quality { Name = "High", Msaa = Viewport.Msaa.Msaa4X, RenderScale = 1f, GlowLevels = 5, Shadows = true, Ssr = true, VolumetricFog = true, PylonLightSpacing = 80, PostFx = true, TerrainColumns = ColsHigh, TerrainRowStep = 2, SceneryDensity = 0.8f, ViewAhead = 2600, FogDensity = 0.0013f, CameraFar = 3000 },
    };

    /// <summary>Anti-aliasing modes offered in Settings ("Auto" picks one per quality preset).</summary>
    public static readonly string[] AaModes = { "Auto", "Off", "FXAA", "SMAA", "TAA", "Supersampling" };

    public static readonly string[] AaDescriptions =
    {
        "Auto (by quality preset)",
        "Off",
        "FXAA (fastest, slightly soft)",
        "MSAA + SMAA (sharp edges)",
        "MSAA + TAA (smoothest, best for distant shimmer)",
        "Supersampling 1.5× + SMAA (sharpest, very heavy)",
    };

    /// <summary>The AA mode "Auto" resolves to for this preset.</summary>
    public string DefaultAa => Name switch { "Low" => "FXAA", "Medium" => "SMAA", _ => "TAA" };

    public void ApplyToViewport(Viewport vp)
    {
        string aa = Settings.Current.AntiAliasing;
        if (string.IsNullOrEmpty(aa) || aa == "Auto") aa = DefaultAa;

        // MSAA smooths polygon edges; the screen-space passes also catch shader edges
        // (thin glowing grid lines), and TAA removes their shimmer in the distance.
        var msaa = Msaa == Viewport.Msaa.Disabled ? Viewport.Msaa.Msaa2X : Msaa;
        float scale = RenderScale;
        vp.UseTaa = false;
        switch (aa)
        {
            case "Off":
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
            case "FXAA":
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;
            case "SMAA":
                vp.Msaa3D = msaa;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
                break;
            case "Supersampling":
                vp.Msaa3D = msaa;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
                scale = 1.5f;
                break;
            default: // TAA
                vp.Msaa3D = msaa;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.UseTaa = true;
                break;
        }
        vp.Scaling3DMode = scale < 0.99f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = scale;
    }
}
