using System.IO;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Verifies the generated image fixtures materialise and decode as real images — so the image
/// viewer's manual testing (and the UI <see cref="SampleFileViewerTests"/>) always has a folder
/// of openable pictures. Unit-category: decodes the bytes, no window required.
/// </summary>
[TestClass]
public class ImageSamplesTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void ImageSamples_AreGenerated_AndDecode()
    {
        var files = TestSampleData.Files("images");

        // More than the carousel's 20-dot window so the paged indicator is exercisable.
        Assert.IsTrue(files.Count > 20, $"expected >20 image samples, got {files.Count}.");

        foreach (var path in files)
        {
            Assert.IsTrue(File.Exists(path), $"sample not generated: {path}");

            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.IsTrue(frame.PixelWidth > 0 && frame.PixelHeight > 0, $"did not decode as an image: {path}");
        }
    }
}
