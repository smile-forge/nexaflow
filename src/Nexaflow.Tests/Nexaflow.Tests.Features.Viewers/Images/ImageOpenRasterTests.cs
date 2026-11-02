using System.IO;
using System.IO.Compression;
using System.Text;
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
using Nexaflow.IO.Common.OpenRaster;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// OpenRaster: everything Nexaflow writes reads back the same — every layer kind, attribute, blend, mask, clip and extension —
/// as a package any reader opens (<c>mimetype</c> first and stored, a merged image, a thumbnail no larger than the format
/// allows); what another application wrote is read as it wrote it, what Nexaflow does not understand kept and written back;
/// pixels changed under Nexaflow's description win; and a stack merely shaped like a masked layer stays a group.
/// <para>
/// No other application runs here, so the files "from Krita" are made by hand to what Krita's exporter writes
/// (<c>kis_open_raster_stack_save_visitor.cpp</c>): inherit alpha as <c>svg:src-atop</c> or <c>alpha-preserve</c>,
/// pass-through groups as <c>isolation="auto"</c>, locks, its own blends as <c>krita:</c>, a filter layer, version 0.0.1, no
/// resolution.
/// </para>
/// </summary>
[TestClass]
[CoversNode("images-ora")]
public class ImageOpenRasterTests
{
    private string _dir = "";
    private VirtualFileSystem _vfs = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-ora-").FullName;
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

    private OpenRasterFormat Format => new(_vfs);

    private static TiledCoverage Rect(int width, int height, double left, double top, double right, double bottom)
        => RegionRaster.Shape(new RegionRectangle(left, top, right, bottom), width, height, 1);

    private static TiledPixels Painted(int width, int height, SKColor colour, double left, double top, double right, double bottom)
        => TiledPixels.Empty.Filled(Rect(width, height, left, top, right, bottom), colour);

    private async Task Write(LayeredPicture picture, string path)
    {
        using var renderer = await LayeredRenderer.PrepareAsync(picture, _ => MadeByTools.None, _services);
        await Format.WriteAsync(picture, path, renderer);
    }

    // ── What Nexaflow writes ────────────────────────────────────────────────

    [TestMethod]
    public async Task EveryLayerKind_AndEveryAttribute_ReadsBackAsItWasWritten()
    {
        var photo = Path.Combine(_dir, "photo.png");
        using (var bitmap = Flat(40, 30, new SKColor(20, 120, 200))) File.WriteAllBytes(photo, EncodePng(bitmap));

        var markup = new MarkupLayer { Name = "Notes", Elements = [new MarkupArrow { From = new(1, 1), To = new(30, 20), Color = 0xFF00FF00 }] };
        var clipped = new PixelLayer { Name = "Shade", Pixels = Painted(64, 48, SKColors.Yellow, 0, 0, 64, 48), ClipsToBelow = true, Blend = new LayerBlend("svg:screen") };
        var group = new GroupLayer { Name = "Group", Children = [clipped, markup], Isolated = false, Locked = true, Mask = new LayerMask(Rect(64, 48, 0, 0, 32, 48), Enabled: false, Linked: false) };
        var picture = new PictureLayer
        {
            Name = "Photo",
            Source = new PictureSource(photo, null),
            Recipe = ImageRecipe.Identity with { Adjustments = RecipeAdjustments.Identity with { Exposure = 0.5 } },
            Placement = LayerPlacement.FromMatrix(SKMatrix.CreateScale(-1.5f, 0.5f).PostConcat(SKMatrix.CreateRotationDegrees(30)).PostConcat(SKMatrix.CreateTranslation(5, 4))),
            Kept = KeptFromFile.Of(new OpenRasterKept { Attributes = [new OpenRasterAttribute("urn:elsewhere", "thing", "kept")] }),
        };
        var red = new PixelLayer
        {
            Name = "Red", Pixels = Painted(64, 48, SKColors.Red, 10, 10, 30, 20), Opacity = 0.6, Blend = new LayerBlend("svg:multiply"),
            AlphaLocked = true, Mask = new LayerMask(Rect(64, 48, 12, 0, 64, 48)),
        };
        var hidden = new PixelLayer { Name = "Empty", Visible = false };
        var written = new LayeredPicture
        {
            Width = 64, Height = 48, Dpi = 150, Background = LayerBackground.White,
            Layers = [picture, red, group, hidden], ActiveId = markup.Id, Selection = TiledCoverage.Clear(64, 48),
        };

        var path = Path.Combine(_dir, "layers.ora");
        await Write(written, path);
        var read = (await Format.ReadAsync(path)).Picture;

        Assert.AreEqual((64, 48, 150.0), (read.Width, read.Height, read.Dpi));
        Assert.AreEqual(LayerBackground.White, read.Background);
        Assert.AreEqual(markup.Id, read.ActiveId, "the active layer, as Krita marks it");
        CollectionAssert.AreEqual(written.All.Select(l => l.Id).ToArray(), read.All.Select(l => l.Id).ToArray(), "every layer, in its place");
        foreach (var (before, after) in written.All.Zip(read.All)) SameLayer(before, after);

        var readPicture = (PictureLayer)read.Layers[0];
        Assert.AreEqual(picture.Recipe, readPicture.Recipe, "the picture layer's recipe, from its extension file");
        Assert.AreEqual(picture.Placement, readPicture.Placement);
        CollectionAssert.AreEqual(File.ReadAllBytes(photo), _vfs.ReadAllBytes(readPicture.Source.Path), "its source, the original file as it was");
        Assert.AreEqual(1, ((MarkupLayer)((GroupLayer)read.Layers[2]).Children[1]).Elements.Length, "the markup layer's annotations");
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public async Task SixteenBitColourAndGrey_ReadBackValueForValue_MasksAndAll()
    {
        foreach (var colour in new[] { new PictureColour(ColourModel.Rgb, ChannelDepth.U16), new PictureColour(ColourModel.Grey, ChannelDepth.U16) })
        {
            var format = colour.Tiles;
            var samples = new ushort[300 * format.Channels];
            for (int i = 0; i < 300; i++)
                for (int c = 0; c < format.Channels; c++)
                    samples[i * format.Channels + c] = c == format.Channels - 1 ? (ushort)(65535 - i * 3) : (ushort)(i * 211 + c * 7);
            var pixels = TiledPixels.EmptyIn(format).Written(new SKRectI(4, 2, 304, 3), System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()));
            var mask = TiledCoverage.Clear(320, 8, ChannelDepth.U16).WrittenValues(new SKRectI(0, 0, 3, 1), [0.3f, 0.30001f, 1f]);
            var written = new LayeredPicture
            {
                Width = 320, Height = 8, Colour = colour,
                Layers = [new PixelLayer { Name = "Height", Pixels = pixels }, new PixelLayer { Name = "Masked", Pixels = pixels, Mask = new LayerMask(mask) }],
                Selection = TiledCoverage.Clear(320, 8),
            };

            var path = Path.Combine(_dir, $"deep-{colour.Model}.ora");
            await Write(written, path);
            var read = (await Format.ReadAsync(path)).Picture;

            Assert.AreEqual(colour, read.Colour, "the picture opens in the colour it was written in");
            var area = new SKRectI(4, 2, 304, 3);
            CollectionAssert.AreEqual(pixels.Read(area), ((PixelLayer)read.Layers[0]).Pixels.Read(area), $"{colour}: every 16-bit value as it was");
            var readMask = read.Layers[1].Mask!.Coverage;
            Assert.AreEqual(ChannelDepth.U16, readMask.Depth);
            CollectionAssert.AreEqual(mask.ReadValues(new SKRectI(0, 0, 3, 1)), readMask.ReadValues(new SKRectI(0, 0, 3, 1)), $"{colour}: the mask to 16 bits");
        }
    }

    [TestMethod]
    [CoversNode("images-colour-depth")]
    public void OpenRaster_HoldsNoFloatPicture()
    {
        Assert.IsTrue(Format.HoldsColour(new PictureColour(ColourModel.Grey, ChannelDepth.U16)));
        Assert.IsFalse(Format.HoldsColour(new PictureColour(ColourModel.Rgb, ChannelDepth.F32)), "a PNG holds no float: a 32-bit picture is saved as PSD");
    }

    private static void SameLayer(Layer before, Layer after)
    {
        Assert.AreEqual(before.GetType(), after.GetType(), before.Name);
        Assert.AreEqual((before.Name, before.Opacity, before.Visible, before.Blend, before.Locked, before.AlphaLocked, before.ClipsToBelow),
                        (after.Name, after.Opacity, after.Visible, after.Blend, after.Locked, after.AlphaLocked, after.ClipsToBelow), before.Name);
        Assert.AreEqual(before.Kept, after.Kept, $"{before.Name}: what it kept");
        Assert.AreEqual(before.Mask is null, after.Mask is null, $"{before.Name}: its mask");
        if (before.Mask is { } mask)
        {
            Assert.AreEqual((mask.Enabled, mask.Linked), (after.Mask!.Enabled, after.Mask.Linked));
            CollectionAssert.AreEqual(mask.Coverage.ToBytes(), after.Mask.Coverage.ToBytes(), $"{before.Name}: the mask's coverage");
        }
        if (before is PixelLayer { Pixels: var pixels })
        {
            var area = pixels.Bounds;
            Assert.AreEqual(area, ((PixelLayer)after).Pixels.Bounds, $"{before.Name}: where it is painted");
            if (!area.IsEmpty) CollectionAssert.AreEqual(pixels.Read(area), ((PixelLayer)after).Pixels.Read(area), $"{before.Name}: its pixels");
        }
        if (before is GroupLayer group) Assert.AreEqual(group.Isolated, ((GroupLayer)after).Isolated);
    }

    [TestMethod]
    public async Task APackage_HasItsMimetypeFirstAndStored_AMergedImage_AndASmallThumbnail()
    {
        var picture = LayeredPicture.Blank(600, 300, LayerBackground.White, "Layer");
        picture = picture.WithLayer(picture.Layers[0].Id, l => ((PixelLayer)l) with { Pixels = Painted(600, 300, SKColors.Blue, 0, 0, 300, 300) });
        var path = Path.Combine(_dir, "blank.ora");
        await Write(picture, path);

        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual(ImageContainer.OpenRaster, ImageContainers.Sniff(bytes.AsSpan(0, 64)), "mimetype first and stored, so the format is known from the first bytes");
        using var thumbnail = SKBitmap.Decode(_vfs.ReadAllBytes(OpenRasterPackage.EntryPath(path, OpenRasterPackage.ThumbnailPath)));
        Assert.AreEqual((256, 128), (thumbnail.Width, thumbnail.Height), "at most 256 a side, its shape kept");
        using var merged = _services.Decoders.Decode(bytes, maxLongEdge: null);
        Assert.AreEqual(ImageContainer.OpenRaster, merged.Container, "a viewer shows the merged image");
        Assert.AreEqual(new SKSizeI(600, 300), merged.StoredFullSize);
        using var pixels = SKBitmap.FromImage(merged.Pixels);
        Assert.AreEqual(SKColors.Blue, pixels.GetPixel(10, 10));
        Assert.AreEqual(SKColors.White, pixels.GetPixel(500, 10), "the background beneath");

        var small = LayeredPicture.Blank(100, 50, LayerBackground.Transparent, "Layer");
        await Write(small, path);
        using var notUpscaled = SKBitmap.Decode(_vfs.ReadAllBytes(OpenRasterPackage.EntryPath(path, OpenRasterPackage.ThumbnailPath)));
        Assert.AreEqual((100, 50), (notUpscaled.Width, notUpscaled.Height), "a small picture's thumbnail is not upscaled");
    }

    [TestMethod]
    public async Task PixelsChangedUnderNexaflowsDescription_WinOverIt()
    {
        var markup = new MarkupLayer { Name = "Notes", Elements = [new MarkupBox { Corner = new(2, 2), OppositeCorner = new(20, 20) }] };
        var picture = new LayeredPicture { Width = 32, Height = 32, Layers = [markup], ActiveId = markup.Id, Selection = TiledCoverage.Clear(32, 32) };
        var path = Path.Combine(_dir, "edited.ora");
        await Write(picture, path);
        Assert.IsInstanceOfType<MarkupLayer>((await Format.ReadAsync(path)).Picture.Layers[0]);

        // Another application keeps the attributes but paints over the layer's pixels.
        using (var other = Flat(8, 8, SKColors.Purple)) _vfs.Replace(OpenRasterPackage.EntryPath(path, "data/layer1.png"), EncodePng(other));

        var read = (await Format.ReadAsync(path)).Picture.Layers[0];
        Assert.IsInstanceOfType<PixelLayer>(read, "the pixels win: read as a pixel layer");
        Assert.AreEqual(new SKSizeI(8, 8), ((PixelLayer)read).Pixels.Bounds.Size, "its pixels as they now are");
        Assert.AreEqual(markup.Id, read.Id, "still the same layer");
    }

    // ── What others write ───────────────────────────────────────────────────

    private string Package(string name, string stack, params (string Path, byte[] Bytes)[] files)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()) mime.Write(Encoding.ASCII.GetBytes("image/openraster"));
        using (var xml = zip.CreateEntry("stack.xml").Open()) xml.Write(Encoding.UTF8.GetBytes(stack));
        foreach (var (file, bytes) in files)
            using (var entry = zip.CreateEntry(file).Open()) entry.Write(bytes);
        return path;
    }

    private static byte[] Png(int width, int height, SKColor colour)
    {
        using var bitmap = Flat(width, height, colour);
        return EncodePng(bitmap);
    }

    private const string KritaStack = """
        <?xml version="1.0" encoding="UTF-8"?>
        <image w="20" h="10" version="0.0.1" xmlns:krita="http://krita.org/namespaces/svg/krita" krita:custom="1">
         <stack x="0" y="0" name="root">
          <stack isolation="auto" name="Pass" x="0" y="0" opacity="1" visibility="visible">
           <layer src="data/a.png" x="0" y="0" name="Clipped" composite-op="svg:src-atop" edit-locked="true" selected="true" krita:thing="kept"/>
           <layer src="data/b.png" x="0" y="0" name="Base"/>
          </stack>
          <filter type="applications:krita:levels" name="Levels" opacity="1" visibility="visible"/>
          <layer src="data/c.png" x="2" y="1" name="Ex" composite-op="krita:exclusion"/>
          <layer src="data/d.png" x="0" y="0" name="Shaped" alpha-preserve="true" composite-op="svg:multiply"/>
         </stack>
         <krita:extra>hello</krita:extra>
        </image>
        """;

    private string KritaPackage() => Package("krita.ora", KritaStack,
        ("data/a.png", Png(20, 10, SKColors.Red)), ("data/b.png", Png(20, 10, SKColors.Blue)),
        ("data/c.png", Png(4, 4, SKColors.Green)), ("data/d.png", Png(20, 10, SKColors.Gray)),
        ("mergedimage.png", Png(20, 10, SKColors.Orange)), ("Thumbnails/thumbnail.png", Png(20, 10, SKColors.Orange)),
        ("data/unknown.bin", [1, 2, 3]));

    [TestMethod]
    [CoversNode("images-ora")]
    public async Task AFileFromKrita_IsReadAsKritaWroteIt()
    {
        var read = await Format.ReadAsync(KritaPackage());
        var picture = read.Picture;
        Assert.AreEqual(LayeredPicture.DefaultDpi, picture.Dpi, "no xres: 72 dpi, as the format says");
        Assert.IsTrue(read.HoldsUndrawable, "the filter layer no reader can draw");

        var pass = (GroupLayer)picture.Layers[3];
        Assert.IsFalse(pass.Isolated, "isolation=auto: a pass-through group");
        var clipped = pass.Children[1];
        Assert.AreEqual(("Clipped", true, true, LayerBlend.Normal), (clipped.Name, clipped.ClipsToBelow, clipped.Locked, clipped.Blend),
                        "svg:src-atop: Normal, clipped to the layer beneath; edit-locked: locked");
        Assert.AreEqual(clipped.Id, picture.ActiveId, "selected: the active layer");
        Assert.IsInstanceOfType<UndrawableLayer>(picture.Layers[2]);
        Assert.AreEqual("applications:krita:levels", ((UndrawableLayer)picture.Layers[2]).Kind);
        var ex = picture.Layers[1];
        Assert.AreEqual(("krita:exclusion", SKBlendMode.Exclusion), (ex.Blend.Op, ex.Blend.ToSkia()), "kept as written, drawn by Skia's blend of its name");
        Assert.AreEqual(new SKRectI(2, 1, 6, 5), ((PixelLayer)ex).Pixels.Bounds, "at its place");
        var shaped = picture.Layers[0];
        Assert.AreEqual((true, "svg:multiply"), (shaped.ClipsToBelow, shaped.Blend.Op), "alpha-preserve beside another blend: clipped, with that blend");
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public async Task WhatIsNotUnderstood_IsKept_AndWrittenBack()
    {
        var read = (await Format.ReadAsync(KritaPackage())).Picture;
        var pictureKept = read.Kept.Get<OpenRasterPictureKept>()!;
        Assert.IsTrue(pictureKept.Image.Attributes.Any(a => a.LocalName == "custom"), "the image's own attribute");
        Assert.IsTrue(pictureKept.Image.Elements.Any(e => e.Contains("extra")), "the image's own element");
        Assert.IsTrue(pictureKept.Entries.ContainsKey("data/unknown.bin"), "a file no layer names");

        var path = Path.Combine(_dir, "again.ora");
        await Write(read, path);
        var stack = Encoding.UTF8.GetString(_vfs.ReadAllBytes(OpenRasterPackage.EntryPath(path, OpenRasterPackage.StackPath)));
        StringAssert.Contains(stack, "krita:custom=\"1\"");
        StringAssert.Contains(stack, "krita:extra");
        StringAssert.Contains(stack, "krita:thing=\"kept\"");
        StringAssert.Contains(stack, "applications:krita:levels");
        StringAssert.Contains(stack, "isolation=\"auto\"");
        StringAssert.Contains(stack, "composite-op=\"svg:src-atop\"");
        StringAssert.Contains(stack, "edit-locked=\"true\"");
        StringAssert.Contains(stack, "alpha-preserve=\"true\"");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, _vfs.ReadAllBytes(OpenRasterPackage.EntryPath(path, "data/unknown.bin")));
        Assert.IsFalse(stack.Contains("<stack x="), "no offset written on a stack");
    }

    [TestMethod]
    [CoversNode("images-layer-masks")]
    public async Task AStackShapedLikeAMaskedLayer_WithoutNexaflowsRoles_StaysAGroup()
    {
        var path = Package("shaped.ora", """
            <image w="8" h="8" version="0.0.6"><stack>
             <stack isolation="isolate" name="Looks masked">
              <layer src="data/m.png" x="0" y="0" composite-op="svg:dst-in" name="m"/>
              <layer src="data/l.png" x="0" y="0" name="l"/>
             </stack>
            </stack></image>
            """, ("data/m.png", Png(8, 8, SKColors.White)), ("data/l.png", Png(8, 8, SKColors.Red)), ("mergedimage.png", Png(8, 8, SKColors.Red)));
        var picture = (await Format.ReadAsync(path)).Picture;
        Assert.IsInstanceOfType<GroupLayer>(picture.Layers[0]);
        Assert.AreEqual(2, ((GroupLayer)picture.Layers[0]).Children.Length);
        Assert.AreEqual("svg:dst-in", ((GroupLayer)picture.Layers[0]).Children[1].Blend.Op);
    }

    [TestMethod]
    public async Task FilesAsGimpMyPaintAndPintaWriteThem_OpenOnTheirMergedImage()
    {
        // GIMP: a layer per PNG, resolution given; MyPaint: hidden layers and opacity, no resolution; Pinta: plain layers, version 0.0.3.
        var stacks = new[]
        {
            ("gimp.ora", """<image w="10" h="10" xres="300" yres="300" version="0.0.5"><stack><layer src="data/a.png" x="0" y="0" name="Background" opacity="1.000" visibility="visible" composite-op="svg:src-over"/></stack></image>"""),
            ("mypaint.ora", """<image w="10" h="10" version="0.0.5"><stack><layer src="data/a.png" x="0" y="0" name="layer" opacity="0.5" visibility="hidden"/></stack></image>"""),
            ("pinta.ora", """<image w="10" h="10" version="0.0.3"><stack opacity="1" name="root"><layer opacity="1.00" name="Layer 1" visibility="visible" src="data/a.png" x="0" y="0"/></stack></image>"""),
        };
        foreach (var (name, stack) in stacks)
        {
            var path = Package(name, stack, ("data/a.png", Png(10, 10, SKColors.Teal)), ("mergedimage.png", Png(10, 10, SKColors.Teal)));
            var read = await Format.ReadAsync(path);
            Assert.AreEqual(1, read.Picture.Layers.Length, name);
            Assert.IsFalse(read.HoldsUndrawable, name);
            using var merged = _services.Decoders.Decode(File.ReadAllBytes(path), maxLongEdge: null);
            using var pixels = SKBitmap.FromImage(merged.Pixels);
            Assert.AreEqual(SKColors.Teal, pixels.GetPixel(5, 5), $"{name}: its merged image");
        }
    }
}
