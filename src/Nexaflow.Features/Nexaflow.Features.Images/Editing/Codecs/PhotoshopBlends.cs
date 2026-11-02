using Nexaflow.Features.Images.Editing.Layers;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>Photoshop's blend keys as the blends Nexaflow draws, and back.</summary>
internal static class PhotoshopBlends
{
    /// <summary>A pass-through group's key.</summary>
    public const string PassThrough = "pass";

    public const string Normal = "norm";

    private static readonly (string Key, LayerBlend Blend)[] Keys =
    [
        ("norm", LayerBlend.Normal),
        ("diss", LayerBlend.Dissolve),
        ("dark", new("svg:darken")),
        ("mul ", new("svg:multiply")),
        ("idiv", new("svg:color-burn")),
        ("lbrn", new("krita:linear_burn")),
        ("dkCl", new("krita:darker color")),
        ("lite", new("svg:lighten")),
        ("scrn", new("svg:screen")),
        ("div ", new("svg:color-dodge")),
        ("lddg", new("krita:linear_dodge")),
        ("lgCl", new("krita:lighter color")),
        ("over", new("svg:overlay")),
        ("sLit", new("svg:soft-light")),
        ("hLit", new("svg:hard-light")),
        ("vLit", new("krita:vivid_light")),
        ("lLit", new("krita:linear light")),
        ("pLit", new("krita:pin_light")),
        ("hMix", new("krita:hard_mix_photoshop")),
        ("diff", new("svg:difference")),
        ("smud", new("krita:exclusion")),
        ("fsub", new("krita:subtract")),
        ("fdiv", new("krita:divide")),
        ("hue ", new("svg:hue")),
        ("sat ", new("svg:saturation")),
        ("colr", new("svg:color")),
        ("lum ", new("svg:luminosity")),
    ];

    /// <summary>
    /// The blends whose fill opacity is not the same as their opacity — Photoshop's "special eight", which apply fill inside
    /// the blend — so a fill below full on one of them cannot be folded into the layer's opacity.
    /// </summary>
    private static readonly HashSet<string> FillInsideBlend = ["idiv", "lbrn", "div ", "lddg", "lLit", "vLit", "hMix", "diff"];

    /// <summary>The blend a key names; null where Nexaflow knows it not.</summary>
    public static LayerBlend? BlendOf(string key)
    {
        foreach (var (k, blend) in Keys)
            if (k == key) return blend;
        return null;
    }

    /// <summary>The key a blend is written under; null for one Photoshop has none of — <c>svg:plus</c>, <c>svg:dst-in</c>.</summary>
    public static string? KeyOf(LayerBlend blend)
    {
        foreach (var (key, b) in Keys)
            if (b == blend) return key;
        return null;
    }

    public static bool FillFoldsIntoOpacity(string key) => !FillInsideBlend.Contains(key);
}
