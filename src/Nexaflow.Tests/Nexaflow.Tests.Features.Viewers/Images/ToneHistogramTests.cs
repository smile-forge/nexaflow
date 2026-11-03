using System.IO;
using System.Runtime.InteropServices;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The histogram at the head of Adjust: a frame's tones counted in its own colour and at its own depth — an 8-bit photo from
/// its pixels, a deeper picture as half floats — where it clips, what is marked on the picture, and the editor reading only
/// while the histogram is shown.
/// </summary>
[TestClass]
[CoversNode("images-edit-histogram")]
public class ToneHistogramTests
{
    private static SKImage EightBit(params SKColor[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(pixels.Length, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int x = 0; x < pixels.Length; x++) bitmap.SetPixel(x, 0, pixels[x]);
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>A row of half-float pixels, unpremultiplied, in <paramref name="space"/>.</summary>
    private static SKImage Deep(SKColorSpace space, params (float R, float G, float B)[] pixels)
    {
        var halves = new Half[pixels.Length * 4];
        for (int x = 0; x < pixels.Length; x++)
        {
            halves[4 * x] = (Half)pixels[x].R;
            halves[4 * x + 1] = (Half)pixels[x].G;
            halves[4 * x + 2] = (Half)pixels[x].B;
            halves[4 * x + 3] = (Half)1f;
        }
        using var bitmap = new SKBitmap(new SKImageInfo(pixels.Length, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, space));
        var bytes = MemoryMarshal.AsBytes(halves.AsSpan()).ToArray();
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return SKImage.FromBitmap(bitmap);
    }

    [TestMethod]
    public void AnEightBitFrame_IsCountedByChannelAndLuminance_ItsSeeThroughLeftOut()
    {
        using var picture = EightBit(new SKColor(0, 0, 0), new SKColor(255, 255, 255), new SKColor(200, 100, 50), SKColors.Transparent);
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        Assert.AreEqual(3, tones.Pixels, "the transparent pixel is no part of what is seen");
        Assert.AreEqual(1, tones.Red[200]);
        Assert.AreEqual(1, tones.Green[100]);
        Assert.AreEqual(1, tones.Blue[50]);
        Assert.AreEqual(1, tones.Luminance[(54 * 200 + 183 * 100 + 19 * 50 + 128) >> 8], "luminance weighed as Rec. 709 weighs it");
        Assert.AreEqual(1, tones.Luminance[0]);
        Assert.AreEqual(1, tones.Luminance[255]);
        Assert.AreEqual(ToneChannels.All, tones.ShadowsClipped, "the black pixel is one in three: well past the share that lights the mark");
        Assert.AreEqual(ToneChannels.All, tones.HighlightsClipped);
        Assert.AreEqual(1, tones.ShadowPixels);
        Assert.AreEqual(1, tones.HighlightPixels);
    }

    [TestMethod]
    public void AHalfSeeThroughPixel_IsCountedAtItsOwnColour()
    {
        using var picture = EightBit(new SKColor(200, 100, 50, 128));
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        Assert.AreEqual(1, tones.Pixels);
        Assert.AreEqual(1, tones.Red[199] + tones.Red[200] + tones.Red[201], "premultiplied, it is read back at its own red, not at half of it");
        Assert.AreEqual(1, tones.Green[99] + tones.Green[100] + tones.Green[101]);
    }

    [TestMethod]
    public void OneStrayPixel_DoesNotLightTheMark()
    {
        var pixels = Enumerable.Repeat(new SKColor(120, 120, 120), 9999).Append(new SKColor(255, 255, 255)).ToArray();
        using var picture = EightBit(pixels);
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        Assert.AreEqual(1, tones.HighlightPixels, "it is counted");
        Assert.AreEqual(ToneChannels.None, tones.HighlightsClipped, "one in ten thousand is short of the share that clips");
    }

    [TestMethod]
    public void ADeepPicture_IsReadAtItsOwnDepth_ATone_JustShortOfWhite_DoesNotClip()
    {
        using var srgb = SKColorSpace.CreateSrgb();
        using var picture = Deep(srgb, (0.999f, 0.999f, 0.999f), (0.5f, 0.5f, 0.5f), (1.5f, 0.2f, 0.2f), (0f, 0.4f, 0.4f));
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        Assert.AreEqual(4, tones.Pixels);
        Assert.AreEqual(1, tones.HighlightPixels, "only the red beyond white clips: 0.999 is short of it, though 8 bits would round it onto white");
        Assert.AreEqual(ToneChannels.Red, tones.HighlightsClipped);
        Assert.AreEqual(ToneChannels.Red, tones.ShadowsClipped);
        Assert.AreEqual(1, tones.Red[128], "a half-float 0.5 at level 128");
        Assert.AreEqual(2, tones.Red[255], "0.999 and 1.5 both lie at the top level; only the one beyond white clips");

        using var narrowed = new SKBitmap(new SKImageInfo(4, 1, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        Assert.IsTrue(picture.ReadPixels(narrowed.Info, narrowed.GetPixels(), narrowed.RowBytes, 0, 0));
        Assert.AreEqual(255, narrowed.GetPixel(0, 0).Red, "the screen's 8-bit copy, by contrast, has it at white");
    }

    [TestMethod]
    public void ALinearPicture_IsReadOnTheSrgbCurve()
    {
        using var linear = SKColorSpace.CreateSrgbLinear();
        using var picture = Deep(linear, (0.5f, 0.5f, 0.5f));
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        // Linear 0.5 is about 0.735 on the sRGB curve: level 188, where a person reads a light grey.
        Assert.AreEqual(1, tones.Red[187] + tones.Red[188] + tones.Red[189], "read on the curve tones are read on, not at linear level 128");
    }

    [TestMethod]
    public void AWideGamutColour_IsReadInItsOwnGamut_NotClippedToSrgb()
    {
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul, p3));
        bitmap.SetPixel(0, 0, new SKColor(247, 13, 13));
        using var picture = SKImage.FromBitmap(bitmap);
        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        Assert.AreEqual(ToneChannels.None, tones.HighlightsClipped, "a deep Display P3 red is inside its own gamut");
        Assert.AreEqual(ToneChannels.None, tones.ShadowsClipped);

        using var srgb = SKColorSpace.CreateSrgb();
        using var narrowed = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        Assert.IsTrue(picture.ReadPixels(narrowed.Info, narrowed.GetPixels(), narrowed.RowBytes, 0, 0));
        var shown = narrowed.GetPixel(0, 0);
        Assert.IsTrue(shown.Red == 255 || shown.Green == 0, "the screen's sRGB copy, by contrast, has it clipped");
    }

    [TestMethod]
    public void WhatClips_IsMarkedOnlyForTheEndsAsked()
    {
        using var picture = EightBit(new SKColor(0, 0, 0), new SKColor(255, 255, 255), new SKColor(255, 0, 0), new SKColor(120, 120, 120));
        var reader = new ToneReader();

        var highlights = reader.Read(picture, ClipEnds.Highlights).Clipped!;
        Assert.AreEqual(ClipEnds.Highlights, highlights.Ends);
        CollectionAssert.AreEqual(new byte[] { 0, ClippedPixels.Highlight, ClippedPixels.Highlight, 0 }, highlights.Marks,
                                  "the black pixel's shadows are not asked for");

        var both = reader.Read(picture, ClipEnds.Shadows | ClipEnds.Highlights).Clipped!;
        CollectionAssert.AreEqual(new byte[] { ClippedPixels.Shadow, ClippedPixels.Highlight, ClippedPixels.Shadow | ClippedPixels.Highlight, 0 },
                                  both.Marks, "pure red clips at both ends: green and blue at black, red at white");
        Assert.AreSame(highlights, both, "the marks are reused from frame to frame");

        Assert.IsNull(reader.Read(picture, ClipEnds.None).Clipped, "nothing is marked where nothing is asked");
    }

    [TestMethod]
    public void ATallFrame_CountedInBands_CountsAsOne()
    {
        const int width = 97, height = 701;
        var random = new Random(7);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var picture = SKImage.FromBitmap(bitmap);

        var tones = new ToneReader().Read(picture, ClipEnds.None).Histogram;

        var red = new int[ToneHistogram.Levels];
        var luminance = new int[ToneHistogram.Levels];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                red[c.Red]++;
                luminance[(54 * c.Red + 183 * c.Green + 19 * c.Blue + 128) >> 8]++;
            }
        Assert.AreEqual(width * height, tones.Pixels);
        CollectionAssert.AreEqual(red, tones.Red.ToArray(), "every band's count lands once in the whole");
        CollectionAssert.AreEqual(luminance, tones.Luminance.ToArray());
    }

    [TestMethod]
    public void ThePanel_AsksForTones_OnlyWhileShown_AndMarksOnlyWhatIsPointedAtOrKept()
    {
        int frames = 0;
        var tones = new PictureTones(() => frames++);
        Assert.AreEqual(new ToneAsk(false), tones.Ask, "put away, it costs a frame nothing");

        tones.IsShown = true;
        Assert.AreEqual(new ToneAsk(true, ClipEnds.None), tones.Ask);
        Assert.AreEqual(1, frames, "shown, a frame is drawn to read");

        tones.PointsAtHighlights = true;
        tones.KeepsShadows = true;
        Assert.AreEqual(ClipEnds.Shadows | ClipEnds.Highlights, tones.Marked);
        tones.PointsAtHighlights = false;
        Assert.AreEqual(ClipEnds.Shadows, tones.Marked, "pointing away, only what is kept stays marked");

        tones.IsShown = false;
        Assert.AreEqual(new ToneAsk(false), tones.Ask, "put away, nothing is marked either");
        Assert.IsFalse(tones.ShowsShadows);
    }

    [TestMethod]
    public void TheStatus_NamesTheChannelsThatClip()
    {
        string separator = Str.Get("Images.Edit.Histogram.ListSeparator");
        Assert.AreEqual(Str.Get("Images.Edit.Histogram.Red") + separator + Str.Get("Images.Edit.Histogram.Blue"),
                        PictureTones.ChannelNames(ToneChannels.Red | ToneChannels.Blue), "only the channels that clip, red first");
        Assert.AreEqual(3, PictureTones.ChannelNames(ToneChannels.All).Split(separator).Length);
    }

    // ── The editor, end to end ────────────────────────────────────────────

    private static IShellServices Shell(ViewerUiThread ui)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => ui.Run(call.Arg<Action>()));
        ViewerUiThread.RunQueuedTasksAtOnce(shell);
        return shell;
    }

    private static bool Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (done()) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [TestMethod]
    public void TheEditor_ReadsEachFrameItDraws_WhileTheHistogramShows_AndTintsWhatClips()
    {
        var dir = TempDirectory();
        using var ui = new ViewerUiThread();
        try
        {
            var photo = CopyOfSample(dir, OrientedPhotoSample.FileName);
            var vm = new ArtistViewModel(photo, Shell(ui), new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1))));
            Assert.IsTrue(Until(() => vm.Canvas is not null), "the photo was never presented");
            Assert.IsNull(vm.Tones.Histogram, "hidden, no frame is read");

            ui.Invoke(() => vm.Tones.IsShown = true);
            Assert.IsTrue(Until(() => vm.Tones.Histogram is not null), "shown, the next frame is read");

            ui.Invoke(() => vm.Editor.Exposure = 3);
            Assert.IsTrue(Until(() => vm.Tones.HighlightsClip), "three stops up, the highlights clip");
            var (status, clipped) = ui.Invoke(() => (vm.Tones.HighlightsStatus, vm.Tones.Histogram!.HighlightsClipped));
            Assert.AreEqual(Str.Format("Images.Edit.Histogram.HighlightsClip", PictureTones.ChannelNames(clipped)), status, "the mark says which channels clip");

            ui.Invoke(() => vm.Tones.PointsAtHighlights = true);
            Assert.IsTrue(Until(() => ui.Invoke(() => vm.Tones.ShowsHighlights)), "pointed at, what clips is marked on the next frame");
            var mask = ui.Invoke(() => (vm.Tones.HighlightMask!.PixelWidth, vm.Tones.HighlightMask.PixelHeight));
            var frame = ui.Invoke(() => (vm.CurrentImage!.PixelWidth, vm.CurrentImage.PixelHeight));
            Assert.AreEqual(frame, mask, "the mask is the frame's own size, so it lies on it pixel for pixel");

            ui.Invoke(() => vm.Tones.PointsAtHighlights = false);
            Assert.IsFalse(ui.Invoke(() => vm.Tones.ShowsHighlights), "pointing away hides it at once, not a frame later");
            ui.Run(vm.Dispose).Wait();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ASixteenBitPicture_IsReadAtItsOwnDepth_ThroughTheEditor()
    {
        var dir = TempDirectory();
        using var ui = new ViewerUiThread();
        try
        {
            // A 16-bit picture a hair short of white: 8 bits would round it onto white and call it clipped.
            var services = new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1)));
            var path = Path.Combine(dir, "near-white-16.tif");
            using (var srgb = SKColorSpace.CreateSrgb())
            using (var picture = Deep(srgb, Enumerable.Repeat((0.999f, 0.999f, 0.999f), 64).ToArray()))
                File.WriteAllBytes(path, services.Encoders.Encode(picture, ImageContainer.Tiff, new EncodeOptions(90), MetadataBlocks.None));

            var vm = new ArtistViewModel(path, Shell(ui), services);
            Assert.IsTrue(Until(() => vm.Canvas is not null), "the picture was never presented");
            Assert.IsTrue(vm.CurrentSession?.Proxy?.IsHighBitDepth == true, "decoded as the 16-bit picture it is");

            ui.Invoke(() => vm.Tones.IsShown = true);
            Assert.IsTrue(Until(() => vm.Tones.Histogram is not null));
            Assert.IsFalse(ui.Invoke(() => vm.Tones.HighlightsClip), "read at its own depth, it is short of white");
            ui.Run(vm.Dispose).Wait();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
