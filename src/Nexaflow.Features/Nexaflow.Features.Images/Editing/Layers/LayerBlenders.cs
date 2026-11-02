using System.Collections.Concurrent;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// The blends Photoshop has and Skia has not — linear burn and dodge, vivid, linear and pin light, hard mix, subtract, divide,
/// darker and lighter colour — drawn by Skia's runtime blenders from their formulas, and Dissolve, drawn by a shader since it
/// depends on where a pixel is. Each blender is built once and shared.
/// </summary>
/// <remarks>
/// Every blend composites as the W3C's general formula does: the blended colour <c>B(cb, cs)</c> of backdrop and source, not
/// premultiplied, weighted by both alphas, over what each shows alone. The formulas are Photoshop's, in the picture's own
/// values — encoded for 8 and 16 bits, light for 32.
/// </remarks>
public static class LayerBlenders
{
    private const string Composite = """
        vec4 main(vec4 src, vec4 dst) {
            vec3 cs = src.a > 0.0 ? src.rgb / src.a : vec3(0.0);
            vec3 cb = dst.a > 0.0 ? dst.rgb / dst.a : vec3(0.0);
            vec3 b = blended(cb, cs);
            return vec4((1.0 - dst.a) * src.rgb + (1.0 - src.a) * dst.rgb + src.a * dst.a * b, src.a + dst.a * (1.0 - src.a));
        }
        """;

    private static string PerChannel(string formula) => $$"""
        float channel(float b, float s) { {{formula}} }
        vec3 blended(vec3 cb, vec3 cs) { return vec3(channel(cb.r, cs.r), channel(cb.g, cs.g), channel(cb.b, cs.b)); }
        """;

    private const string Luminance = "float lum(vec3 c) { return dot(c, vec3(0.3, 0.59, 0.11)); }";

    /// <summary>Each blend's <c>blended</c> function, by its <see cref="LayerBlend.Key"/>.</summary>
    private static readonly IReadOnlyDictionary<string, string> Formulas = new Dictionary<string, string>
    {
        ["linear-burn"] = PerChannel("return max(b + s - 1.0, 0.0);"),
        ["linear-dodge"] = PerChannel("return min(b + s, 1.0);"),
        ["linear-light"] = PerChannel("return clamp(b + 2.0 * s - 1.0, 0.0, 1.0);"),
        ["vivid-light"] = PerChannel("""
            if (s <= 0.5) { float t = 2.0 * s; return b >= 1.0 ? 1.0 : t <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - b) / t); }
            float t = 2.0 * s - 1.0; return b <= 0.0 ? 0.0 : t >= 1.0 ? 1.0 : min(1.0, b / (1.0 - t));
            """),
        ["pin-light"] = PerChannel("return s <= 0.5 ? min(b, 2.0 * s) : max(b, 2.0 * s - 1.0);"),
        ["hard-mix-photoshop"] = PerChannel("return b + s >= 1.0 ? 1.0 : 0.0;"),
        ["subtract"] = PerChannel("return max(b - s, 0.0);"),
        ["divide"] = PerChannel("return s <= 0.0 ? (b <= 0.0 ? 0.0 : 1.0) : min(b / s, 1.0);"),
        ["darker-color"] = Luminance + " vec3 blended(vec3 cb, vec3 cs) { return lum(cs) < lum(cb) ? cs : cb; }",
        ["lighter-color"] = Luminance + " vec3 blended(vec3 cb, vec3 cs) { return lum(cs) > lum(cb) ? cs : cb; }",
    };

    private static readonly ConcurrentDictionary<string, SKBlender> Built = new();

    // Each pixel kept whole, and opaque, where a hash of where it is falls under its alpha times the opacity; else not at all.
    private const string DissolveShader = """
        uniform shader layer;
        uniform float opacity;
        uniform float2 origin;
        float hash(float2 p) { return fract(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
        half4 main(float2 p) {
            half4 c = layer.eval(p);
            float a = c.a * opacity;
            if (a <= 0.0 || hash(floor(p + origin)) >= a) return half4(0.0);
            return half4(c.rgb / c.a, 1.0);
        }
        """;

    private static readonly Lazy<SKRuntimeEffect> Dissolving = new(() =>
        SKRuntimeEffect.CreateShader(DissolveShader, out var error) ?? throw new InvalidOperationException($"Dissolve did not build: {error}"));

    /// <summary>The blends drawn here, by <see cref="LayerBlend.Key"/>.</summary>
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Formulas.Keys;

    /// <summary>The blender that draws the blend named <paramref name="key"/>; null where it is none of these.</summary>
    public static SKBlender? For(string key)
    {
        if (!Formulas.TryGetValue(key, out var formula)) return null;
        return Built.GetOrAdd(key, _ =>
        {
            using var effect = SKRuntimeEffect.CreateBlender(formula + "\n" + Composite, out var error)
                               ?? throw new InvalidOperationException($"The {key} blend did not build: {error}");
            return effect.ToBlender(new SKRuntimeEffectUniforms(effect));
        });
    }

    /// <summary>
    /// <paramref name="layer"/> — a layer drawn on a surface of its own — dissolved at <paramref name="opacity"/>: a shader
    /// over the same pixels, each shown whole or not at all, chosen by where it lies on the output, <paramref name="origin"/>
    /// being where the surface's top-left lies there, so a pixel dissolves the same in every band and every redraw.
    /// </summary>
    public static SKShader Dissolve(SKImage layer, double opacity, SKPointI origin)
    {
        var effect = Dissolving.Value;
        using var child = layer.ToRawShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, SKSamplingOptions.Default);
        var uniforms = new SKRuntimeEffectUniforms(effect) { ["opacity"] = (float)opacity, ["origin"] = new[] { (float)origin.X, (float)origin.Y } };
        var children = new SKRuntimeEffectChildren(effect) { ["layer"] = child };
        return effect.ToShader(uniforms, children);
    }
}
