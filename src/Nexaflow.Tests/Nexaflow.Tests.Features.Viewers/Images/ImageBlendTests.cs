using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Photoshop's blends that Skia has not, as Nexaflow draws them: each against its formula — the W3C's general compositing of
/// the blended colour — over colours that take every branch of it; and Dissolve showing each pixel whole or not at all, as
/// many as it is opaque, the same every time it is drawn.
/// </summary>
[TestClass]
public class ImageBlendTests
{
    private static readonly MarkupPainter Painter = new();

    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }

    private static float Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

    /// <summary>Each blend's formula, per channel or whole, backdrop then source, not premultiplied.</summary>
    private static readonly Dictionary<string, Func<float[], float[], float[]>> Formulas = new()
    {
        ["linear-burn"] = (b, s) => Each(b, s, (x, y) => Math.Max(x + y - 1, 0)),
        ["linear-dodge"] = (b, s) => Each(b, s, (x, y) => Math.Min(x + y, 1)),
        ["linear-light"] = (b, s) => Each(b, s, (x, y) => Math.Clamp(x + 2 * y - 1, 0, 1)),
        ["vivid-light"] = (b, s) => Each(b, s, (x, y) => y <= 0.5f
            ? (x >= 1 ? 1 : 2 * y <= 0 ? 0 : 1 - Math.Min(1, (1 - x) / (2 * y)))
            : (x <= 0 ? 0 : 2 * y - 1 >= 1 ? 1 : Math.Min(1, x / (1 - (2 * y - 1))))),
        ["pin-light"] = (b, s) => Each(b, s, (x, y) => y <= 0.5f ? Math.Min(x, 2 * y) : Math.Max(x, 2 * y - 1)),
        ["hard-mix-photoshop"] = (b, s) => Each(b, s, (x, y) => x + y >= 1 ? 1 : 0),
        ["subtract"] = (b, s) => Each(b, s, (x, y) => Math.Max(x - y, 0)),
        ["divide"] = (b, s) => Each(b, s, (x, y) => y <= 0 ? (x <= 0 ? 0 : 1) : Math.Min(x / y, 1)),
        ["darker-color"] = (b, s) => Lum(s[0], s[1], s[2]) < Lum(b[0], b[1], b[2]) ? s : b,
        ["lighter-color"] = (b, s) => Lum(s[0], s[1], s[2]) > Lum(b[0], b[1], b[2]) ? s : b,
    };

    private static float[] Each(float[] b, float[] s, Func<float, float, float> f) => [f(b[0], s[0]), f(b[1], s[1]), f(b[2], s[2])];

    private static PixelLayer Solid(PictureColour colour, int side, SKColorF fill, LayerBlend? blend = null, double opacity = 1)
        => new()
        {
            Pixels = TiledPixels.EmptyIn(colour.Tiles).Filled(TiledCoverage.Covered(side, side, colour.Depth), fill),
            Blend = blend ?? LayerBlend.Normal, Opacity = opacity,
        };

    private static float[] Composite(LayeredPicture picture)
    {
        using var compositor = new LayerCompositor(Painter, cache: false);
        using var image = compositor.Render(picture, 1, new NoPictures());
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.RgbaF32, SKAlphaType.Unpremul, picture.Colour.ColourSpace);
        using var bitmap = new SKBitmap(info);
        Assert.IsTrue(image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan()).ToArray();
    }

    private static LayeredPicture Picture(PictureColour colour, int side, params Layer[] layers)
        => new() { Width = side, Height = side, Colour = colour, Layers = [.. layers], Selection = TiledCoverage.Clear(side, side) };

    [TestMethod]
    [CoversNode("images-layers")]
    public void EveryBlendNexaflowDraws_CompositesAsItsFormulaSays()
    {
        var colour = new PictureColour(ColourModel.Rgb, ChannelDepth.U16);
        // Pairs that take each formula's branches: dark and light, each side of a half — none on a branch's very edge, where
        // which side a value falls is rounding's to decide.
        (float[] Under, float[] Over)[] pairs =
        [
            ([0.8f, 0.35f, 0.55f], [0.25f, 0.7f, 0.5f]),
            ([0.1f, 0.9f, 0.0f], [0.6f, 0.2f, 1.0f]),
        ];
        var drawn = LayerBlend.Offered.Where(o => o.Skia is null && !o.Blend.IsDissolve).Select(o => o.Blend).ToArray();
        CollectionAssert.AreEquivalent(Formulas.Keys.ToArray(), drawn.Select(b => b.Key).ToArray(), "every blend Skia has not has a formula here");
        foreach (var blend in drawn)
            foreach (var (under, over) in pairs)
                foreach (float alpha in new[] { 1f, 0.6f })
                {
                    var picture = Picture(colour, 4,
                        Solid(colour, 4, new SKColorF(under[0], under[1], under[2], 1)),
                        Solid(colour, 4, new SKColorF(over[0], over[1], over[2], alpha), blend));
                    var actual = Composite(picture);
                    var b = Formulas[blend.Key](under, over);
                    for (int c = 0; c < 3; c++)
                    {
                        float expected = (1 - alpha) * under[c] + alpha * b[c];   // the backdrop opaque: the general formula, simplified
                        Assert.AreEqual(expected, actual[c], 2e-3, $"{blend.Key} at {alpha}: channel {c} of ({string.Join(",", under)}) under ({string.Join(",", over)})");
                    }
                }
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void AClippedLayer_OverWhatIsPartlyTransparent_KeepsItsAlpha_AsPhotoshopAndKritaDo()
    {
        var colour = new PictureColour(ColourModel.Rgb, ChannelDepth.U16);
        foreach (var blend in new[] { LayerBlend.Normal, new LayerBlend("svg:multiply"), new LayerBlend("krita:linear_burn") })
        {
            var beneath = Solid(colour, 4, new SKColorF(1, 1, 1, 0.5f));
            var clipped = Solid(colour, 4, new SKColorF(1, 0, 0, 1), blend) with { ClipsToBelow = true };
            var actual = Composite(Picture(colour, 4, beneath, clipped));
            Assert.AreEqual(0.5f, actual[3], 1e-3, $"{blend.Key}: the alpha beneath, no more — source-atop");
            if (blend == LayerBlend.Normal) Assert.AreEqual((1f, 0f, 0f), (actual[0], actual[1], actual[2]), $"{blend.Key}: the clipped colour where it shows");
        }
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void Dissolve_ShowsEachPixelWholeOrNotAtAll_AsManyAsItIsOpaque_AndTheSameEachTime()
    {
        const int side = 128;
        var colour = PictureColour.Rgb8;
        var picture = Picture(colour, side, Solid(colour, side, new SKColorF(1, 0, 0, 1), LayerBlend.Dissolve, opacity: 0.4));
        var once = Composite(picture);
        var again = Composite(picture);

        int shown = 0;
        for (int p = 0; p < side * side; p++)
        {
            float a = once[p * 4 + 3];
            Assert.IsTrue(a is 0 or 1, $"pixel {p} is shown whole or not at all, not at {a}");
            if (a == 1) shown++;
        }
        Assert.AreEqual(0.4, shown / (double)(side * side), 0.03, "as many shown as the layer is opaque");
        CollectionAssert.AreEqual(once, again, "drawn again, every pixel falls the same way");
    }
}
