using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A picture inserted into a document becomes a page the size of its neighbour, the picture fitted and centred and
/// standing as its EXIF orientation says. A JPEG is written as it is; any other picture as a PNG, transparency kept.
/// </summary>
[TestClass]
[CoversNode("pdf-page-insert-image")]
public class PdfImagePageWriterTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void APicture_IsFittedToThePage_AndCentred()
    {
        var wide = PdfImagePageWriter.Fit(new Size(200, 100), new Size(600, 800));
        Assert.AreEqual(new Rect(0, 250, 600, 300), wide);

        var small = PdfImagePageWriter.Fit(new Size(10, 20), new Size(600, 800));
        Assert.AreEqual(new Rect(100, 0, 400, 800), small, "a picture smaller than the page is scaled up to it");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EveryOrientation_DrawsThePictureOverTheWholeBox()
    {
        var box = new Rect(10, 20, 100, 50);
        var corners = new[] { new PdfPoint(10, 20), new PdfPoint(110, 20), new PdfPoint(10, 70), new PdfPoint(110, 70) };

        for (var orientation = 1; orientation <= 8; orientation++)
        {
            var placement = PdfImagePageWriter.Placement(orientation, box);
            var drawn = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }
                .Select(corner => placement.Transform(new PdfPoint(corner.Item1, corner.Item2)))
                .ToHashSet();
            CollectionAssert.AreEquivalent(corners, drawn.ToArray(), $"orientation {orientation}");
        }

        Assert.AreEqual(new PdfPoint(10, 20), PdfImagePageWriter.Placement(1, box).Transform(new PdfPoint(0, 0)), "upright: its bottom left at the box's");
        Assert.AreEqual(new PdfPoint(10, 70), PdfImagePageWriter.Placement(6, box).Transform(new PdfPoint(0, 0)),
            "turned a quarter clockwise: its bottom left at the box's top left");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AJpeg_IsWrittenAsItIs()
    {
        var jpeg = Encoded(new JpegBitmapEncoder(), Picture(PixelFormats.Bgr24));

        using var page = Written(jpeg, ".jpg");

        var image = page.GetPage(1).GetImages().Single();
        CollectionAssert.AreEqual(jpeg, image.RawMemory.ToArray());
        Assert.AreEqual(612, page.GetPage(1).Width, 0.01, "the page is the size given");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void APngWithTransparency_KeepsIt()
    {
        var png = Encoded(new PngBitmapEncoder(), Picture(PixelFormats.Bgra32));

        using var page = Written(png, ".png");

        var image = page.GetPage(1).GetImages().Single();
        Assert.IsTrue(image.ImageDictionary.ContainsKey(NameToken.Smask), "the transparency is carried as a soft mask");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AFileThatIsNoPicture_IsRefused()
    {
        var path = TestTemp.CreateFile(".png");
        File.WriteAllText(path, "not a picture");

        Assert.ThrowsExactly<NotSupportedException>(() => PdfImagePageWriter.Write(path, new Size(612, 792), new MemoryStream(), CancellationToken.None));
    }

    private static PdfDocument Written(byte[] picture, string extension)
    {
        var path = TestTemp.CreateFile(extension);
        File.WriteAllBytes(path, picture);
        using var output = new MemoryStream();
        PdfImagePageWriter.Write(path, new Size(612, 792), output, CancellationToken.None);
        return PdfDocument.Open(output.ToArray());
    }

    private static BitmapSource Picture(PixelFormat format)
    {
        const int width = 40, height = 20;
        var stride = width * ((format.BitsPerPixel + 7) / 8);
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i % 3 == 0 ? 200 : 60);
        var picture = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        picture.Freeze();
        return picture;
    }

    private static byte[] Encoded(BitmapEncoder encoder, BitmapSource picture)
    {
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
