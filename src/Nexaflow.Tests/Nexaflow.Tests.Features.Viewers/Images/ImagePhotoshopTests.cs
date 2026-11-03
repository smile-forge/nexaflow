using System.IO;
using System.Runtime.InteropServices;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common;
using Nexaflow.Media.Photoshop;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Photoshop as a layered picture: everything Nexaflow writes reads back the same — every layer kind, attribute, blend, mask,
/// group and clipping run, at 8, 16 and 32 bits, colour and grey, as PSD and PSB; what Photoshop wrote that Nexaflow does not
/// draw kept and written back — a text layer's text until it is painted on, an adjustment layer whole — the file then opening
/// on its saved look; a CMYK file refused; pixels changed under Nexaflow's description winning over it; and the viewer showing
/// a file's merged image, or its layers where it saved none.
/// </summary>
[TestClass]
[CoversNode("images-psd")]
public class ImagePhotoshopTests
{
    private string _dir = "";
    private VirtualFileSystem _vfs = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-psd-").FullName;
        _vfs = new VirtualFileSystem();
        _vfs.RegisterHandler(new ZipArchiveHandler());
        _services = new ImageEditingServices(_vfs, new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget),
                                             new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget),
                                             new ErasedPictures(Path.Combine(_dir, "erased"), ErasedPictures.DefaultBudget));
    }

    [TestCleanup]
    public void Clean()
    {
        _services.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private PhotoshopFormat Format(bool large = false) => new(_vfs, large, Path.Combine(_dir, "sources"));

    private static PictureColour Rgb(ChannelDepth depth) => new(ColourModel.Rgb, depth);
    private static PictureColour Grey(ChannelDepth depth) => new(ColourModel.Grey, depth);

    private static IEnumerable<object[]> EveryColour =>
    [
        [PictureColour.Rgb8, false], [Rgb(ChannelDepth.U16), false], [Rgb(ChannelDepth.F32), false],
        [Grey(ChannelDepth.U8), false], [Grey(ChannelDepth.U16), false], [Grey(ChannelDepth.F32), true],
        [PictureColour.Rgb8, true],
    ];

    private static TiledCoverage Rect(int width, int height, double left, double top, double right, double bottom, ChannelDepth depth)
        => RegionRaster.Shape(new RegionRectangle(left, top, right, bottom), width, height, 1).AtDepth(depth);

    private static TiledPixels Painted(PictureColour colour, int width, int height, SKColorF fill, double left, double top, double right, double bottom)
        => TiledPixels.EmptyIn(colour.Tiles).Filled(Rect(width, height, left, top, right, bottom, colour.Depth), fill);

    private async Task Write(LayeredPicture picture, string path, bool large = false)
    {
        using var renderer = await LayeredRenderer.PrepareAsync(picture, _ => MadeByTools.None, _services);
        await Format(large).WriteAsync(picture, path, renderer);
    }

    private static void SameLayer(Layer before, Layer after)
    {
        Assert.AreEqual(before.GetType(), after.GetType(), before.Name);
        Assert.AreEqual((before.Id, before.Name, before.Visible, before.Blend, before.Locked, before.AlphaLocked, before.ClipsToBelow),
                        (after.Id, after.Name, after.Visible, after.Blend, after.Locked, after.AlphaLocked, after.ClipsToBelow), before.Name);
        Assert.AreEqual(before.Opacity, after.Opacity, 0.5 / 255, $"{before.Name}: its opacity, to a byte");
        Assert.AreEqual(before.Mask is null, after.Mask is null, $"{before.Name}: its mask");
        if (before.Mask is { } mask)
        {
            Assert.AreEqual(mask.Enabled, after.Mask!.Enabled);
            var all = new SKRectI(0, 0, mask.Coverage.Width, mask.Coverage.Height);
            CollectionAssert.AreEqual(mask.Coverage.ReadValues(all), after.Mask.Coverage.ReadValues(all), $"{before.Name}: the mask's coverage, value for value");
        }
        if (before is PixelLayer { Pixels: var pixels })
        {
            var area = pixels.Bounds;
            Assert.AreEqual(area, ((PixelLayer)after).Pixels.Bounds, $"{before.Name}: where it is painted");
            if (!area.IsEmpty) CollectionAssert.AreEqual(pixels.Read(area), ((PixelLayer)after).Pixels.Read(area), $"{before.Name}: its pixels");
        }
        if (before is GroupLayer group)
        {
            Assert.AreEqual(group.Isolated, ((GroupLayer)after).Isolated, $"{before.Name}: isolated or pass-through");
            foreach (var (b, a) in group.Children.Zip(((GroupLayer)after).Children)) SameLayer(b, a);
        }
    }

    // ── What Nexaflow writes ────────────────────────────────────────────────

    [TestMethod]
    [DynamicData(nameof(EveryColour))]
    public async Task EveryLayerKind_AndEveryAttribute_ReadsBackAsItWasWritten(PictureColour colour, bool large)
    {
        const int w = 300, h = 40;
        var red = new PixelLayer
        {
            Name = "Red — ünïcode", Pixels = Painted(colour, w, h, colour.FromSrgb(new SKColorF(0.9f, 0.1f, 0.2f, 0.75f)), 10, 5, 290, 30), Opacity = 0.6,
            Blend = new LayerBlend("krita:linear_burn"), AlphaLocked = true, Mask = new LayerMask(Rect(w, h, 12, 0, 200, 40, colour.Depth), Enabled: false),
        };
        var screened = new PixelLayer { Name = "Screen", Pixels = Painted(colour, w, h, new SKColorF(0.2f, 0.4f, 0.6f, 1), 0, 0, 50, 40), Blend = new LayerBlend("svg:screen") };
        var plus = new PixelLayer { Name = "Plus", Pixels = Painted(colour, w, h, new SKColorF(0.1f, 0.1f, 0.1f, 1), 0, 0, 20, 20), Blend = new LayerBlend("svg:plus"), Locked = true };
        var group = new GroupLayer { Name = "Group", Children = [screened, plus], Isolated = false };
        var hidden = new PixelLayer { Name = "Empty", Visible = false, Pixels = TiledPixels.EmptyIn(colour.Tiles) };
        var written = new LayeredPicture
        {
            Width = w, Height = h, Dpi = 150, Colour = colour, Background = LayerBackground.White,
            Layers = [red, group, hidden], ActiveId = screened.Id, Selection = TiledCoverage.Clear(w, h),
        };

        var path = Path.Combine(_dir, large ? "layers.psb" : "layers.psd");
        await Write(written, path, large);
        var read = (await Format(large).ReadAsync(path)).Picture;

        Assert.AreEqual((w, h, 150.0, colour), (read.Width, read.Height, read.Dpi, read.Colour));
        Assert.AreEqual(LayerBackground.White, read.Background, "the background, from the layer that holds it");
        Assert.AreEqual(screened.Id, read.ActiveId, "the active layer, as Photoshop selects it");
        Assert.AreEqual(3, read.Layers.Length, "the background is the picture's, not a layer");
        foreach (var (before, after) in written.Layers.Zip(read.Layers)) SameLayer(before, after);
    }

    [TestMethod]
    public async Task PictureAndMarkupLayers_ReadBackAsTheyWere_AndPixelsChangedUnderThemWin()
    {
        var photo = Path.Combine(_dir, "photo.png");
        using (var bitmap = Flat(40, 30, new SKColor(20, 120, 200))) File.WriteAllBytes(photo, EncodePng(bitmap));
        var picture = new PictureLayer
        {
            Name = "Photo", Source = new PictureSource(photo, null),
            Recipe = ImageRecipe.Identity with { Adjustments = RecipeAdjustments.Identity with { Exposure = 0.5 } },
            Placement = LayerPlacement.At(5, 4, 1.5, 30),
        };
        var markup = new MarkupLayer { Name = "Notes", Elements = [new MarkupArrow { From = new(1, 1), To = new(30, 20), Color = 0xFF00FF00 }] };
        var written = new LayeredPicture { Width = 64, Height = 48, Layers = [picture, markup], Selection = TiledCoverage.Clear(64, 48) };
        var path = Path.Combine(_dir, "pictures.psd");
        await Write(written, path);

        var read = (await Format().ReadAsync(path)).Picture;
        var readPicture = (PictureLayer)read.Layers[0];
        Assert.AreEqual(picture.Recipe, readPicture.Recipe, "the picture layer's recipe, from Nexaflow's block");
        Assert.AreEqual(picture.Placement, readPicture.Placement);
        CollectionAssert.AreEqual(File.ReadAllBytes(photo), File.ReadAllBytes(readPicture.Source.Path), "its source, as it was");
        Assert.AreEqual(1, ((MarkupLayer)read.Layers[1]).Elements.Length, "the markup layer's annotations");

        // Another application paints over the markup layer's pixels: they no longer hash as written, and win.
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        var document = PhotoshopReader.Read(stream);
        var channel = document.Layers[^1].Channels.First(c => c.Id == 0);
        stream.Position = channel.Offset + channel.Length - 1;
        stream.WriteByte(0x5A);
        stream.Close();
        var changed = (await Format().ReadAsync(path)).Picture;
        Assert.IsInstanceOfType<PixelLayer>(changed.Layers[1], "the pixels as they are now");
    }

    [TestMethod]
    public async Task AClippingRun_ReadsAsAGroupTheBaseHeads_DrawnAsPhotoshopDraws_AndIsWrittenBackAsARun()
    {
        const int w = 8, h = 4;
        var colour = PictureColour.Rgb8;
        // Beneath everything, blue across; the base, red over the left half; clipped to it, green everywhere.
        var under = new PhotoshopLayerToWrite { PascalName = "Under", Rect = new PhotoshopRect(0, 0, h, w), Channels = Solid(w, h, 0, 0, 255, 255) };
        var baseLayer = new PhotoshopLayerToWrite { PascalName = "Base", Rect = new PhotoshopRect(0, 0, h, w / 2), Channels = Solid(w / 2, h, 255, 0, 0, 255), Opacity = 128 };
        var clipped = new PhotoshopLayerToWrite { PascalName = "Clipped", Rect = new PhotoshopRect(0, 0, h, w), Channels = Solid(w, h, 0, 255, 0, 255), Clipping = 1 };
        var path = Path.Combine(_dir, "clip.psd");
        WritePhotoshop(path, colour, w, h, [under, baseLayer, clipped]);

        var read = (await Format().ReadAsync(path)).Picture;
        Assert.AreEqual(2, read.Layers.Length);
        var run = (GroupLayer)read.Layers[1];
        Assert.AreEqual(("Base", 128 / 255.0, true), (run.Name, run.Opacity, run.Isolated), "the run carries the base's name and opacity");
        Assert.IsFalse(run.Children[0].ClipsToBelow);
        Assert.IsTrue(run.Children[1].ClipsToBelow, "the clipped layer clips to the base inside it");

        using var compositor = new LayerCompositor(_services.Markup, cache: false);
        using var image = compositor.Render(read, 1, new NoPictures());
        using var bitmap = SKBitmap.FromImage(image);
        var left = bitmap.GetPixel(0, 0);
        Assert.IsTrue(left.Green > 120 && left.Blue > 120 && left.Red < 10, $"green clipped to the base, at the base's opacity, over blue: {left}");
        Assert.AreEqual(new SKColor(0, 0, 255), bitmap.GetPixel(w - 1, 0), "nothing of the clipped layer past its base");

        var again = Path.Combine(_dir, "clip-again.psd");
        await Write(read, again);
        using var stream = File.OpenRead(again);
        var records = PhotoshopReader.Read(stream).Layers;
        CollectionAssert.AreEqual(new byte[] { 0, 0, 1 }, records.Select(r => r.Clipping).ToArray(), "written back as a base and its clipped layer, no group");
    }

    [TestMethod]
    public async Task WhatPhotoshopWroteAndNexaflowDoesNotDraw_IsKept_TheTextUntilItIsPaintedOn()
    {
        const int w = 6, h = 6;
        var text = PhotoshopTaggedBlock.Of("TySh", new byte[] { 1, 2, 3, 4 });
        var effects = PhotoshopTaggedBlock.Of("lfx2", new byte[] { 9, 9, 9, 9 });
        var levels = PhotoshopTaggedBlock.Of("levl", new byte[] { 0, 2, 0, 0 });
        var textLayer = new PhotoshopLayerToWrite { PascalName = "Title", Rect = new PhotoshopRect(0, 0, h, w), Channels = Solid(w, h, 10, 20, 30, 255), Blocks = [text, effects] };
        var adjustment = new PhotoshopLayerToWrite { PascalName = "Levels 1", Channels = [new(-1, [0, 0]), new(0, [0, 0]), new(1, [0, 0]), new(2, [0, 0])], Blocks = [levels] };
        var path = Path.Combine(_dir, "kept.psd");
        WritePhotoshop(path, PictureColour.Rgb8, w, h, [textLayer, adjustment]);

        var read = await Format().ReadAsync(path);
        Assert.IsTrue(read.HoldsUndrawable, "effects and an adjustment layer: the file opens on its saved look");
        Assert.IsInstanceOfType<UndrawableLayer>(read.Picture.Layers[1]);
        CollectionAssert.Contains(read.Picture.Layers[0].Kept.Undrawn.ToArray(), "effects");

        var untouched = Path.Combine(_dir, "untouched.psd");
        await Write(read.Picture, untouched);
        var keys = KeysOf(untouched);
        CollectionAssert.IsSubsetOf(new[] { "TySh", "lfx2" }, keys[0], "the text and its effects, written back while the pixels are as read");
        CollectionAssert.Contains(keys[1], "levl", "the adjustment layer, whole");

        var painted = read.Picture.WithLayer(read.Picture.Layers[0].Id, l => ((PixelLayer)l) with
        {
            Pixels = ((PixelLayer)l).Pixels.Filled(TiledCoverage.Covered(w, h), new SKColorF(1, 1, 1, 1)),
        });
        var paintedPath = Path.Combine(_dir, "painted.psd");
        await Write(painted, paintedPath);
        keys = KeysOf(paintedPath);
        CollectionAssert.DoesNotContain(keys[0], "TySh", "painted on, the layer is pixels: its text no longer describes it");
        CollectionAssert.Contains(keys[0], "lfx2", "its effects still apply to it");
    }

    [TestMethod]
    public async Task AnUntouchedLayer_IsWrittenBackWithTheFilesOwnChannels_APaintedOneCompressedAnew()
    {
        const int w = 4, h = 4;
        // Uncompressed channels, which Nexaflow never writes, holding a faint red whose straight colour premultiplying rounds away.
        var channels = new (short Id, byte Value)[] { (-1, 3), (0, 200), (1, 10), (2, 90) }
            .Select(c => new PhotoshopChannelToWrite(c.Id, [0, 0, .. Enumerable.Repeat(c.Value, w * h)])).ToArray();
        var path = Path.Combine(_dir, "asread.psd");
        WritePhotoshop(path, PictureColour.Rgb8, w, h, [new PhotoshopLayerToWrite { PascalName = "Faint", Rect = new PhotoshopRect(0, 0, h, w), Channels = [.. channels] }]);
        var read = await Format().ReadAsync(path);

        var untouched = Path.Combine(_dir, "asread-again.psd");
        await Write(read.Picture, untouched);
        var written = ChannelsOf(untouched);
        foreach (var channel in channels) CollectionAssert.AreEqual(channel.Encoded, written[channel.Id], $"channel {channel.Id}: as the file held it, every bit");

        var wider = Path.Combine(_dir, "asread.psb");
        await Write(read.Picture, wider, large: true);
        Assert.AreEqual((byte)PhotoshopCompression.Rle, ChannelsOf(wider)[0][1], "a PSB's run-length rows count wider: compressed anew");

        var painted = read.Picture.WithLayer(read.Picture.Layers[0].Id, l => ((PixelLayer)l) with
        {
            Pixels = ((PixelLayer)l).Pixels.Filled(TiledCoverage.Covered(w, h), new SKColorF(1, 1, 1, 1)),
        });
        var paintedPath = Path.Combine(_dir, "asread-painted.psd");
        await Write(painted, paintedPath);
        Assert.AreEqual((byte)PhotoshopCompression.Rle, ChannelsOf(paintedPath)[0][1], "painted on: compressed anew");
    }

    [TestMethod]
    public async Task AFlatFile_OpensAsItsMergedImage_OneLockedBackgroundLayer_AtItsOwnDepth()
    {
        const int w = 3, h = 2;
        const ushort grey = 0x1234; // between two 8-bit steps
        var path = Path.Combine(_dir, "flat.psd");
        using (var stream = File.Create(path))
            PhotoshopWriter.Write(stream, new PhotoshopDocumentToWrite
            {
                Header = new PhotoshopFileHeader(false, 1, w, h, 16, PhotoshopColourMode.Grayscale),
                Layers = [],
                MergedChannels = 1,
                Merged = (_, _, into) => MemoryMarshal.Cast<byte, ushort>(into).Fill(grey),
            });

        var read = await Format().ReadAsync(path);
        Assert.AreEqual(Grey(ChannelDepth.U16), read.Picture.Colour);
        var background = (PixelLayer)read.Picture.Layers.Single();
        Assert.AreEqual("Background", background.Name);
        Assert.IsTrue(background.Locked, "Photoshop's Background is locked");
        Assert.AreEqual(background.Id, read.Picture.ActiveId);
        var samples = MemoryMarshal.Cast<byte, ushort>(background.Pixels.Read(new SKRectI(0, 0, w, h)));
        for (int p = 0; p < w * h; p++)
        {
            Assert.AreEqual(grey, samples[p * 2], $"pixel {p}: the merged image's grey, every bit");
            Assert.AreEqual(ushort.MaxValue, samples[p * 2 + 1], $"pixel {p}: wholly opaque");
        }
    }

    [TestMethod]
    public async Task ACmykFile_IsRefused_SayingWhatIsEdited()
    {
        var path = Path.Combine(_dir, "cmyk.psd");
        using (var stream = File.Create(path))
            PhotoshopWriter.Write(stream, new PhotoshopDocumentToWrite
            {
                Header = new PhotoshopFileHeader(false, 4, 2, 2, 8, PhotoshopColourMode.Cmyk),
                MergedChannels = 4, Merged = (_, _, into) => into.Clear(),
            });
        var refused = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Format().ReadAsync(path));
        StringAssert.Contains(refused.Message, "RGB and grayscale");
    }

    // ── The viewer ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheViewer_ShowsTheMergedImage_OrTheLayersWhereThereIsNone()
    {
        var colour = Rgb(ChannelDepth.U16);
        var written = new LayeredPicture
        {
            Width = 20, Height = 10, Colour = colour,
            Layers = [new PixelLayer { Pixels = Painted(colour, 20, 10, new SKColorF(0.25f, 0.5f, 0.75f, 1), 0, 0, 20, 10) }],
            Selection = TiledCoverage.Clear(20, 10),
        };
        var path = Path.Combine(_dir, "merged.psd");
        await Write(written, path);
        using var decoded = _services.Decoders.Decode(File.ReadAllBytes(path), null);
        Assert.AreEqual(ImageContainer.Photoshop, decoded.Container);
        Assert.IsTrue(decoded.IsHighBitDepth);
        using var shown = SKBitmap.FromImage(decoded.Pixels);
        var pixel = shown.GetPixel(5, 5);
        Assert.IsTrue(Math.Abs(pixel.Red - 64) <= 1 && Math.Abs(pixel.Blue - 191) <= 1, $"the merged image: {pixel}");

        // Saved without Maximize Compatibility: a blank merged image, and the version resource saying so.
        var blank = Path.Combine(_dir, "blank.psd");
        var version = new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 };
        WritePhotoshop(blank, PictureColour.Rgb8, 4, 4, [new PhotoshopLayerToWrite { Rect = new PhotoshopRect(0, 0, 4, 4), Channels = Solid(4, 4, 200, 0, 0, 255) }],
                       resources: [new PhotoshopResource(1057, "", version)], mergedValue: 255);
        using var layers = _services.Decoders.Decode(File.ReadAllBytes(blank), null);
        using var drawn = SKBitmap.FromImage(layers.Pixels);
        Assert.AreEqual(new SKColor(200, 0, 0), drawn.GetPixel(1, 1), "its layers composited, not the white it saved");
    }

    // ── Files as Photoshop writes them ──────────────────────────────────────

    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }

    /// <summary>An 8-bit layer's channels, every pixel the one colour: transparency, then red, green and blue.</summary>
    private static System.Collections.Immutable.ImmutableArray<PhotoshopChannelToWrite> Solid(int width, int height, byte r, byte g, byte b, byte a)
        => [.. new (short Id, byte Value)[] { (-1, a), (0, r), (1, g), (2, b) }
               .Select(c => new PhotoshopChannelToWrite(c.Id, PhotoshopChannels.Encode(Enumerable.Repeat(c.Value, width * height).ToArray(), width, height, 1, false)))];

    private static void WritePhotoshop(string path, PictureColour colour, int width, int height, PhotoshopLayerToWrite[] layers,
                                       PhotoshopResource[]? resources = null, byte mergedValue = 0)
    {
        using var stream = File.Create(path);
        PhotoshopWriter.Write(stream, new PhotoshopDocumentToWrite
        {
            Header = new PhotoshopFileHeader(false, 4, width, height, colour.Bits, PhotoshopColourMode.Rgb),
            Resources = [.. resources ?? []],
            Layers = [.. layers],
            MergedHasTransparency = true,
            MergedChannels = 4,
            Merged = (_, _, into) => into.Fill(mergedValue),
        });
    }

    /// <summary>Each layer record's tagged block keys, bottom first.</summary>
    private static List<string[]> KeysOf(string path)
    {
        using var stream = File.OpenRead(path);
        return [.. PhotoshopReader.Read(stream).Layers.Select(l => l.Blocks.Select(b => b.Key).ToArray())];
    }

    /// <summary>The first layer's channels in a written file, by id, as compressed — their compression first.</summary>
    private static Dictionary<short, byte[]> ChannelsOf(string path)
    {
        using var stream = File.OpenRead(path);
        var layer = PhotoshopReader.Read(stream).Layers[0];
        return layer.Channels.ToDictionary(c => c.Id, c =>
        {
            var bytes = new byte[c.Length];
            stream.Position = c.Offset;
            stream.ReadExactly(bytes);
            return bytes;
        });
    }
}
