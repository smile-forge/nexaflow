using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// The tone and colour adjustments as one Skia colour filter: a small SkSL program compiled once, which every
/// adjustment only re-parameterises. It runs on Skia's CPU raster pipeline, and would run unchanged on a GPU context.
/// </summary>
/// <remarks>
/// <para>Order inside the program, per pixel:</para>
/// <list type="number">
/// <item>exposure and white balance — physical gains, so applied in linear light;</item>
/// <item>shadows and highlights — a lift or cut of the darks and lights of the luminance, peaking at a third and two
/// thirds of the way up, so black and white stay put;</item>
/// <item>contrast — towards an S-curve around middle grey, or flattened towards it, never a gain that clips;</item>
/// <item>saturation — away from or towards each pixel's own grey.</item>
/// </list>
/// Tone changes scale the colour by the luminance ratio, keeping each pixel's hue. Skia hands a colour filter
/// premultiplied colour, so the program unpremultiplies first and premultiplies its answer.
/// </remarks>
public sealed class AdjustmentFilter : IDisposable
{
    private const string Program = """
        uniform float exposure;
        uniform float3 balance;
        uniform float shadows;
        uniform float highlights;
        uniform float contrast;
        uniform float saturation;

        const half3 kLuma = half3(0.2126, 0.7152, 0.0722);

        half4 main(half4 color) {
            if (color.a <= 0.0) return color;
            half3 c = color.rgb / color.a;

            half3 lin = toLinearSrgb(c) * exp2(exposure) * balance;
            c = fromLinearSrgb(max(lin, half3(0.0)));

            float l = dot(c, kLuma);
            float t = clamp(l, 0.0, 1.0);
            float lifted = l + 0.35 * shadows * 6.75 * t * (1.0 - t) * (1.0 - t)
                             + 0.35 * highlights * 6.75 * t * t * (1.0 - t);
            float shaped = clamp(lifted, 0.0, 1.0);
            float toned = contrast >= 0.0
                ? mix(lifted, smoothstep(0.0, 1.0, shaped), contrast)
                : mix(lifted, 0.5, -0.5 * contrast);
            c = l > 0.001 ? c * (toned / l) : c + (toned - l);

            float g = dot(c, kLuma);
            c = mix(half3(g), c, 1.0 + saturation);

            c = clamp(c, 0.0, 1.0);
            return half4(c * color.a, color.a);
        }
        """;

    // How far the temperature and tint sliders reach at their ends, as linear gains.
    private const float TemperatureReach = 0.30f;
    private const float TintReach = 0.20f;

    private readonly SKRuntimeEffect _effect;

    public AdjustmentFilter()
    {
        _effect = SKRuntimeEffect.CreateColorFilter(Program, out var errors)
            ?? throw new InvalidOperationException($"The adjustment program did not compile: {errors}");
    }

    /// <summary>The filter for <paramref name="adjustments"/>; null at identity, so an unadjusted picture is not filtered.</summary>
    public SKColorFilter? Create(RecipeAdjustments adjustments)
    {
        if (adjustments.IsIdentity) return null;

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["exposure"]   = (float)Math.Clamp(adjustments.Exposure, -RecipeAdjustments.ExposureRange, RecipeAdjustments.ExposureRange),
            ["balance"]    = WhiteBalance(adjustments.Temperature, adjustments.Tint),
            ["shadows"]    = Unit(adjustments.Shadows),
            ["highlights"] = Unit(adjustments.Highlights),
            ["contrast"]   = Unit(adjustments.Contrast),
            ["saturation"] = Unit(adjustments.Saturation),
        };
        return _effect.ToColorFilter(uniforms);
    }

    /// <summary>
    /// Red/blue gains for temperature and a green gain for tint, normalised so the luminance of grey is unchanged —
    /// warming a picture should not also brighten it.
    /// </summary>
    internal static float[] WhiteBalance(double temperature, double tint)
    {
        float t = Unit(temperature), m = Unit(tint);
        float r = 1 + TemperatureReach * t, g = 1 - TintReach * m, b = 1 - TemperatureReach * t;
        float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        return [r / luma, g / luma, b / luma];
    }

    private static float Unit(double slider) =>
        (float)(Math.Clamp(slider, -RecipeAdjustments.SliderRange, RecipeAdjustments.SliderRange) / RecipeAdjustments.SliderRange);

    public void Dispose() => _effect.Dispose();
}
