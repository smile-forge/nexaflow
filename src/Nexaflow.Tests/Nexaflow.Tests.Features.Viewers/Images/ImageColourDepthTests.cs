using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// A layered picture held at its colour end to end — 8-bit, 16-bit or float, colour or grey: every tile format carries its
/// values exactly, edits keep 16-bit steps, grey tiles draw as neutral colour, a float picture keeps light past white, masks
/// keep their depth, a history step costs its format's bytes, and a picture converts between colours.
/// </summary>
[TestClass]
public class ImageColourDepthTests
{
    private static readonly MarkupPainter Painter = new();

    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }

    private static readonly PictureColour Rgb16 = new(ColourModel.Rgb, ChannelDepth.U16);
    private static readonly PictureColour Rgb32 = new(ColourModel.Rgb, ChannelDepth.F32);
    private static readonly PictureColour Gray16 = new(ColourModel.Gray, ChannelDepth.U16);
    private static readonly PictureColour Gray32 = new(ColourModel.Gray, ChannelDepth.F32);

    private static IEnumerable<object[]> EveryColour =>
    [
        [PictureColour.Rgb8], [Rgb16], [Rgb32], [new PictureColour(ColourModel.Gray, ChannelDepth.U8)], [Gray16], [Gray32],
    ];

    private static LayeredPicture Picture(PictureColour colour, int width, int height, params Layer[] layers)
        => new() { Width = width, Height = height, Colour = colour, Layers = [.. layers], Selection = TiledCoverage.Clear(width, height) };

    /// <summary>The composite of <paramref name="picture"/>, its pixels as straight floats, four a pixel.</summary>
    private static float[] Composite(LayeredPicture picture)
    {
        using var compositor = new LayerCompositor(Painter, cache: false);
        using var image = compositor.Render(picture, 1, new NoPictures());
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.RgbaF32, SKAlphaType.Unpremul, picture.Colour.ColourSpace);
        var values = new float[image.Width * image.Height * 4];
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            Assert.IsTrue(image.ReadPixels(info, handle.AddrOfPinnedObject(), image.Width * 16, 0, 0));
        }
        finally
        {
            handle.Free();
        }
        return values;
    }

    // ── Tile formats ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-colour-depth")]
    [DynamicData(nameof(EveryColour))]
    public void EveryTileFormat_CarriesItsValuesThroughPremultipliedFloatsAndBack(PictureColour colour)
    {
        var format = colour.Tiles;
        var random = new Random(7);
        var pixels = new byte[4096 * format.PixelBytes];
        random.NextBytes(pixels);
        if (colour.Depth == ChannelDepth.F32)
        {
            // Floats: colour anywhere from below black to well past white, alpha 0…1.
            var floats = MemoryMarshal.Cast<byte, float>(pixels.AsSpan());
            for (int i = 0; i < floats.Length; i++)
                floats[i] = i % format.Channels == format.Channels - 1 ? (float)random.NextDouble() * 0.99f + 0.01f : (float)(random.NextDouble() * 8 - 1);
        }
        else
        {
            // Every pixel shows something: a wholly transparent one keeps no colour.
            int alpha = (format.Channels - 1) * format.SampleBytes;
            for (int p = 0; p < pixels.Length; p += format.PixelBytes) pixels[p + alpha] |= 1;
            if (colour == PictureColour.Rgb8)
                for (int p = 0; p < pixels.Length; p += 4)
                    for (int c = 0; c < 3; c++) pixels[p + c] = Math.Min(pixels[p + c], pixels[p + 3]);   // premultiplied
        }

        var premul = new float[4096 * 4];
        format.ToPremul(pixels, premul);
        var back = new byte[pixels.Length];
        format.FromPremul(premul, back);

        if (colour.Depth != ChannelDepth.F32)
        {
            CollectionAssert.AreEqual(pixels, back, $"{colour}: every value comes back as it was");
            return;
        }
        var was = MemoryMarshal.Cast<byte, float>(pixels.AsSpan()).ToArray();
        var now = MemoryMarshal.Cast<byte, float>(back.AsSpan()).ToArray();
        for (int i = 0; i < was.Length; i++)
            Assert.AreEqual(was[i], now[i], Math.Abs(was[i]) * 1e-6 + 1e-7, $"{colour}: float {i} comes back as it was");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void SixteenBitPixels_AreReadAndWrittenBitForBit_EvenWherePartlyTransparent()
    {
        var format = Rgb16.Tiles;
        var samples = new ushort[300 * 4];
        for (int i = 0; i < 300; i++)
            (samples[i * 4], samples[i * 4 + 1], samples[i * 4 + 2], samples[i * 4 + 3]) = ((ushort)(i * 211), (ushort)(65535 - i * 97), (ushort)(i * 7), (ushort)(1 + i * 213));
        var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        var rect = new SKRectI(250, 250, 550, 251);   // across two tiles

        var pixels = TiledPixels.EmptyIn(format).Written(rect, bytes);
        CollectionAssert.AreEqual(bytes, pixels.Read(rect), "what is written is what is read");

        var merged = pixels.MergedWith(TiledPixels.EmptyIn(format));
        CollectionAssert.AreEqual(bytes, merged.Read(rect), "merging nothing over them leaves every value");
    }

    // ── Edits at depth ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void AFillOverASixteenBitRamp_KeepsEveryStepOfTheRamp()
    {
        const int width = 4096;
        var format = Gray16.Tiles;
        var ramp = new ushort[width * 2];
        for (int x = 0; x < width; x++) (ramp[x * 2], ramp[x * 2 + 1]) = ((ushort)(x * 16), 65535);   // 4096 greys, 16 apart
        var rect = new SKRectI(0, 0, width, 1);
        var pixels = TiledPixels.EmptyIn(format).Written(rect, MemoryMarshal.AsBytes(ramp.AsSpan()));

        var half = TiledCoverage.Covered(width, 1, ChannelDepth.U16).ScaledBy(0.5f);
        var filled = pixels.Filled(half, new SKColorF(1, 1, 1, 1));

        var after = MemoryMarshal.Cast<byte, ushort>(filled.Read(rect).AsSpan()).ToArray();
        var greys = Enumerable.Range(0, width).Select(x => after[x * 2]).ToArray();
        for (int x = 1; x < width; x++) Assert.IsTrue(greys[x] > greys[x - 1], $"grey {x} still rises from the one before: {greys[x - 1]} → {greys[x]}");
        Assert.AreEqual(width, greys.Distinct().Count(), "no two steps of the ramp became one, as 8 bits would make 4096 into 256");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void ConvertingAPicture_CarriesItsLightBetweenEncodedAndLinear_AndColourToGrey()
    {
        var mid = TiledPixels.Empty.Filled(TiledCoverage.Covered(4, 4), new SKColorF(0.5f, 0.5f, 0.5f, 1));
        var picture = Picture(PictureColour.Rgb8, 4, 4, new PixelLayer { Pixels = mid });

        var linear = picture.ConvertedTo(Rgb32);
        var value = ((PixelLayer)linear.Layers[0]).Pixels.ReadPremul(new SKRectI(0, 0, 1, 1));
        Assert.AreEqual(PictureColour.ToLinear(128 / 255f), value[0], 1e-4, "sRGB's middle grey is about a fifth of the light, linear");

        var back = linear.ConvertedTo(PictureColour.Rgb8);
        Assert.AreEqual((byte)128, ((PixelLayer)back.Layers[0]).Pixels.Read(new SKRectI(0, 0, 1, 1))[0], "and comes back to the grey it was");

        var red = TiledPixels.Empty.Filled(TiledCoverage.Covered(4, 4), SKColors.Red);
        var grey = Picture(PictureColour.Rgb8, 4, 4, new PixelLayer { Pixels = red }).ConvertedTo(Gray16);
        var g = ((PixelLayer)grey.Layers[0]).Pixels;
        Assert.AreEqual(Gray16.Tiles, g.Format);
        Assert.AreEqual(0.2126f, g.ReadPremul(new SKRectI(0, 0, 1, 1))[0], 1e-4, "red's grey is its Rec. 709 weight");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void ALayerPutIntoADeeperPicture_IsCarriedIntoItsColour_MaskAndAll()
    {
        var picture = LayeredPicture.Blank(8, 8, LayerBackground.Transparent, "Layer", colour: Rgb16);
        Assert.AreEqual(Rgb16.Tiles, ((PixelLayer)picture.Layers[0]).Pixels.Format, "a blank canvas's first layer is in its colour");

        var eightBit = new PixelLayer { Pixels = TiledPixels.Empty.Filled(TiledCoverage.Covered(8, 8), SKColors.Blue), Mask = new LayerMask(TiledCoverage.Covered(8, 8)) };
        var inserted = picture.Inserted(eightBit, null);
        var layer = (PixelLayer)inserted.Active!;
        Assert.AreEqual(Rgb16.Tiles, layer.Pixels.Format, "its pixels are carried to 16 bits");
        Assert.AreEqual(ChannelDepth.U16, layer.Mask!.Coverage.Depth, "and its mask too");

        var changed = inserted.WithLayer(layer.Id, l => ((PixelLayer)l) with { Pixels = TiledPixels.Empty });
        Assert.AreEqual(Rgb16.Tiles, ((PixelLayer)changed.Find(layer.Id)!).Pixels.Format, "a change that hands back 8-bit pixels is carried too");
    }

    // ── Compositing at depth ────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void GreyTiles_DrawAsNeutralColour_AtTheirFullDepth()
    {
        var format = Gray16.Tiles;
        var grey = new ushort[] { 12345, 65535 };
        var pixels = TiledPixels.EmptyIn(format).Written(new SKRectI(3, 3, 4, 4), MemoryMarshal.AsBytes(grey.AsSpan()));
        var composite = Composite(Picture(Gray16, 8, 8, new PixelLayer { Pixels = pixels }));

        int at = (3 * 8 + 3) * 4;
        for (int c = 0; c < 3; c++) Assert.AreEqual(12345 / 65535f, composite[at + c], 1.5 / 65535, $"channel {c} holds the grey to 16 bits");
        Assert.AreEqual(1, composite[at + 3], 1e-5);
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void AFloatPicture_KeepsLightPastWhite_ThroughOpacity()
    {
        var bright = TiledPixels.EmptyIn(Rgb32.Tiles).Filled(TiledCoverage.Covered(4, 4, ChannelDepth.F32), new SKColorF(4, 2, 0.5f, 1));
        var composite = Composite(Picture(Rgb32, 4, 4, new PixelLayer { Pixels = bright, Opacity = 0.5 }) with { Background = LayerBackground.Transparent });

        Assert.AreEqual(4, composite[0], 1e-3, "red at four times white stays four times white");
        Assert.AreEqual(2, composite[1], 1e-3);
        Assert.AreEqual(0.5, composite[3], 1e-3, "at half opacity");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void ASixteenBitMask_HidesItsLayerAsExactlyAsItIsHeld()
    {
        const float share = 0.3f;   // between two 8-bit steps: 76/255 and 77/255 are 0.298 and 0.302
        var white = TiledPixels.EmptyIn(Rgb16.Tiles).Filled(TiledCoverage.Covered(4, 4, ChannelDepth.U16), new SKColorF(1, 1, 1, 1));
        var mask = TiledCoverage.Covered(4, 4, ChannelDepth.U16).ScaledBy(share);
        var composite = Composite(Picture(Rgb16, 4, 4, new PixelLayer { Pixels = white, Mask = new LayerMask(mask) }));

        Assert.AreEqual(share, composite[3], 2.0 / 65535, "the layer shows as much as the mask's 16-bit value");
    }

    // ── What history keeps ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-colour-depth")]
    [DynamicData(nameof(EveryColour))]
    public void AHistoryStep_CostsTheTilesItChanged_AtTheirFormatsSize(PictureColour colour)
    {
        var format = colour.Tiles;
        var one = TiledPixels.EmptyIn(format).Filled(TiledCoverage.Covered(10, 10, colour.Depth), new SKColorF(0.2f, 0.4f, 0.6f, 1));
        Assert.AreEqual(256L * 256 * format.PixelBytes, one.BytesNotIn(TiledPixels.EmptyIn(format)), $"{colour}: one tile, at its size");

        var mask = TiledCoverage.Clear(300, 10, colour.Depth).WrittenValues(new SKRectI(0, 0, 1, 1), [0.5f]);
        Assert.AreEqual(256L * 256 * (colour.Depth switch { ChannelDepth.U8 => 1, ChannelDepth.U16 => 2, _ => 4 }), mask.BytesNotIn(null),
                        $"{colour}: a mask tile at its depth's size");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void Coverage_CarriedToADeeperDepthAndBack_IsWhatItWas()
    {
        var bytes = new byte[300 * 20];
        new Random(3).NextBytes(bytes);
        var coverage = TiledCoverage.FromBytes(bytes, 300, 20);
        var deep = coverage.AtDepth(ChannelDepth.U16);
        Assert.AreEqual(ChannelDepth.U16, deep.Depth);
        CollectionAssert.AreEqual(bytes, deep.AtDepth(ChannelDepth.U8).ToBytes(), "16 bits hold every 8-bit value");
        CollectionAssert.AreEqual(bytes, deep.AtDepth(ChannelDepth.F32).ToBytes(), "and so does a float");

        var inverted = deep.Inverted().Inverted();
        CollectionAssert.AreEqual(bytes, inverted.ToBytes(), "turned over twice, it is as it was");
    }
}
