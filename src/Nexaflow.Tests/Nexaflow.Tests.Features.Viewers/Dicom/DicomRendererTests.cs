using System.IO;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The rendering pipeline: fo-dicom's core RawImage → a BGRA32 <see cref="WriteableBitmap"/>, with the
/// right dimensions, frame count and a window/level that actually changes pixels.
/// </summary>
[TestClass]
[CoversNode("dicom")]
public class DicomRendererTests
{
    private static string SampleDcm => TestSampleData.Path("dicom", "ct.dcm");

    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "nexa-dicom-render-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [TestMethod]
    [CoversNode("dicom-frame-render")]
    public void Render_SingleFrame_ProducesBgra32Bitmap_OfExpectedSize()
    {
        var r = new DicomRenderer(SampleDcm);
        Assert.AreEqual(1, r.Frames);
        Assert.AreEqual(4, r.Width);
        Assert.AreEqual(4, r.Height);

        var bmp = r.Render(0);
        Assert.AreEqual(4, bmp.PixelWidth);
        Assert.AreEqual(4, bmp.PixelHeight);
    }

    [TestMethod]
    [CoversNode("dicom-window-level")]
    public void ChangingWindow_ChangesRenderedPixels()
    {
        var r = new DicomRenderer(SampleDcm);

        r.WindowWidth = 256; r.WindowCenter = 128;
        var wide = ToBytes(r.Render(0));

        r.WindowWidth = 20; r.WindowCenter = 128;   // a much narrower window remaps contrast
        var narrow = ToBytes(r.Render(0));

        CollectionAssert.AreNotEqual(wide, narrow, "narrowing the window should change the pixels");
    }

    [TestMethod]
    [CoversNode("dicom-invert")]
    public void Invert_ProducesPhotographicNegative_NotSolidWhite()
    {
        var r = new DicomRenderer(SampleDcm) { Invert = false };
        r.WindowWidth = 256; r.WindowCenter = 128;
        var normal = ToBytes(r.Render(0));

        r.Invert = true;
        var inverted = ToBytes(r.Render(0));

        // Every colour channel must be the negative of normal (alpha untouched) — not washed to 255.
        for (var i = 0; i + 3 < normal.Length; i += 4)
            for (var c = 0; c < 3; c++)
                Assert.AreEqual(255 - normal[i + c], inverted[i + c], $"channel {c} at {i} must invert");
    }

    [TestMethod]
    [CoversNode("dicom-cine-slider")]
    public void MultiFrame_ReportsFrameCount_AndRendersEachFrame()
    {
        var path = DicomTestFiles.WriteImage(_tmp, "cine.dcm", "PX", "C^D", "1.7.1", "1.7.1.1", "1.7.1.1.1", frames: 3);
        var r = new DicomRenderer(path);

        Assert.AreEqual(3, r.Frames);
        // Frames are solid greys 30/90/150 stored; with CT rescale (−1024) they sit near −994/−934/−874 HU,
        // so window over that range to tell them apart.
        r.WindowCenter = -934; r.WindowWidth = 300;
        var f0 = ToBytes(r.Render(0));
        var f1 = ToBytes(r.Render(1));
        Assert.AreEqual(f0.Length, f1.Length);
        CollectionAssert.AreNotEqual(f0, f1, "different frames carry different pixels");
    }

    private static byte[] ToBytes(BitmapSource bmp)
    {
        var stride = bmp.PixelWidth * 4;
        var buffer = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(buffer, stride, 0);
        return buffer;
    }
}
