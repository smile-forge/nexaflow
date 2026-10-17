using System.Text;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using System.IO;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// What a save writes: the picture upright at its edited size, and the metadata that came with it — EXIF patched to
/// say so, its thumbnail cut loose, GPS and XMP kept — or stripped down to the colour profile. Checked for every
/// container that can carry it, from the generated sample photo stored on its side (<see cref="OrientedPhotoSample"/>).
/// </summary>
[TestClass]
public class ImageExportTests
{
    private static readonly ImageEditingServices Services = new();

    /// <summary>A copy of the generated photo stored on its side, with EXIF, GPS and XMP.</summary>
    private static string SourceJpeg(string dir) => CopyOfSample(dir, OrientedPhotoSample.FileName);

    private static MetadataBlocks Read(byte[] file) =>
        Services.Metadata.Read(file, ImageContainers.Sniff(file.AsSpan(0, 16)));

    [TestMethod]
    [CoversNode("images-metadata-transfer")]
    [CoversNode("images-bake")]
    public void ASavedJpeg_IsUpright_AndItsExifSaysSo()
    {
        var dir = TempDirectory();
        try
        {
            var bytes = Services.Exporter.Export(SourceJpeg(dir), ImageRecipe.Identity, ImageContainer.Jpeg);

            using var decoded = Services.Decoders.Decode(bytes, maxLongEdge: null);
            Assert.AreEqual(SKEncodedOrigin.TopLeft, decoded.Origin, "the orientation is baked into the pixels");
            Assert.AreEqual(new SKSizeI(32, 64), decoded.StoredFullSize, "stored upright");

            var exif = Read(bytes).Exif!;
            Assert.AreEqual(1u, Tag(exif, 0x0112), "Orientation reset");
            Assert.AreEqual(32u, Tag(exif, 0xA002, via: 0x8769), "PixelXDimension follows the saved picture");
            Assert.AreEqual(64u, Tag(exif, 0xA003, via: 0x8769));
            Assert.AreEqual(OrientedPhotoSample.Make, AsciiTag(exif, 0x010F), "the camera is kept");
            Assert.IsNotNull(Tag(exif, 0x0001, via: 0x8825), "GPS is kept");
            Assert.AreEqual(0u, NextDirectory(exif), "the thumbnail of the old pixels is cut loose");
            Assert.AreEqual(OrientedPhotoSample.Xmp, Encoding.UTF8.GetString(Read(bytes).Xmp!));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-edit-export")]
    [CoversNode("images-metadata-transfer")]
    public void StrippingMetadata_LeavesNothingButThePicture()
    {
        var dir = TempDirectory();
        try
        {
            var recipe = new ImageRecipe { Export = new RecipeExport { StripMetadata = true } };
            var bytes = Services.Exporter.Export(SourceJpeg(dir), recipe, ImageContainer.Jpeg);

            var blocks = Read(bytes);
            Assert.IsNull(blocks.Exif);
            Assert.IsNull(blocks.Xmp);
            Assert.IsNull(blocks.PhotoshopResources);
            using var decoded = Services.Decoders.Decode(bytes, null);
            Assert.AreEqual(new SKSizeI(32, 64), decoded.StoredFullSize, "still upright: the orientation was applied, not dropped");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-metadata-transfer")]
    [CoversNode("images-encode")]
    public void ACopyInAnotherFormat_CarriesTheMetadataThatFormatCanHold()
    {
        var dir = TempDirectory();
        try
        {
            var source = SourceJpeg(dir);
            foreach (var target in new[] { ImageContainer.Png, ImageContainer.Webp, ImageContainer.Tiff })
            {
                var bytes = Services.Exporter.Export(source, ImageRecipe.Identity, target);
                Assert.AreEqual(target, ImageContainers.Sniff(bytes.AsSpan(0, 16)), $"{target} written");

                var blocks = Read(bytes);
                Assert.IsNotNull(blocks.Exif, $"{target} keeps EXIF");
                Assert.AreEqual(OrientedPhotoSample.Make, AsciiTag(blocks.Exif!, 0x010F), $"{target} keeps the camera");
                Assert.AreNotEqual(6u, Tag(blocks.Exif!, 0x0112) ?? 1u, $"{target} does not claim the old orientation");
                Assert.IsNotNull(Tag(blocks.Exif!, 0x0001, via: 0x8825), $"{target} keeps GPS");
                Assert.AreEqual(OrientedPhotoSample.Xmp, Encoding.UTF8.GetString(blocks.Xmp!), $"{target} keeps XMP");

                using var decoded = Services.Decoders.Decode(bytes, null);
                Assert.AreEqual(new SKSize(32, 64), decoded.SourceSize, $"{target} shows upright");
            }

            Assert.IsFalse(Services.Encoders.CarriesMetadata(ImageContainer.Bmp), "BMP has nowhere to put it, and says so");
            var bmp = Services.Exporter.Export(source, ImageRecipe.Identity, ImageContainer.Bmp);
            Assert.AreEqual(ImageContainer.Bmp, ImageContainers.Sniff(bmp.AsSpan(0, 16)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-encode")]
    public void ASixteenBitPicture_IsSavedAtSixteenBits()
    {
        var wide = new SKBitmap(new SKImageInfo(8, 8, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        wide.Erase(new SKColor(100, 150, 200));
        using var picture = SKImage.FromBitmap(wide);

        var png = Services.Encoders.Encode(picture, ImageContainer.Png, new EncodeOptions(90), MetadataBlocks.None);
        Assert.AreEqual(16, png[24], "PNG bit depth (IHDR byte 24)");

        var tiff = Services.Encoders.Encode(picture, ImageContainer.Tiff, new EncodeOptions(90), MetadataBlocks.None);
        using var decoded = Services.Decoders.Decode(tiff, null);
        Assert.IsTrue(decoded.IsHighBitDepth, "the TIFF reads back as more than 8 bits a channel");
    }

    [TestMethod]
    [CoversNode("images-bake")]
    public void ReplacingTheOriginal_LeavesOnlyTheNewFile()
    {
        var dir = TempDirectory();
        try
        {
            var path = Path.Combine(dir, "photo.jpg");
            File.WriteAllBytes(path, [1, 2, 3]);
            ImageExporter.ReplaceFile(path, [4, 5, 6, 7]);

            CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(dir).Length, "no swap file left behind");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-edit-save")]
    public void WhatCanBeSavedInPlace_IsKnownBeforeAsking()
    {
        var exporter = Services.Exporter;
        Assert.IsTrue(exporter.Capability(ImageContainer.Jpeg, isAnimated: false, ImageContainer.Jpeg).CanSaveInPlace);
        Assert.IsTrue(exporter.Capability(ImageContainer.Tiff, isAnimated: false, ImageContainer.Tiff).CanSaveInPlace);
        Assert.IsFalse(exporter.Capability(ImageContainer.Ico, isAnimated: false, ImageContainer.Ico).CanSaveInPlace, "ICO is written by nothing here");
        Assert.IsFalse(exporter.Capability(ImageContainer.Gif, isAnimated: true, ImageContainer.Gif).CanSaveInPlace, "an animation would lose its frames");
        Assert.IsFalse(exporter.Capability(ImageContainer.Png, isAnimated: false, ImageContainer.Gif).KeepsMetadata);
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void WhatHoldsTransparency_IsKnownBeforeAsking()
    {
        var exporter = Services.Exporter;
        Assert.IsTrue(exporter.Capability(ImageContainer.Png, isAnimated: false, ImageContainer.Png).KeepsTransparency);
        Assert.IsTrue(exporter.Capability(ImageContainer.Webp, isAnimated: false, ImageContainer.Webp).KeepsTransparency);
        Assert.IsTrue(exporter.Capability(ImageContainer.Tiff, isAnimated: false, ImageContainer.Tiff).KeepsTransparency);
        Assert.IsFalse(exporter.Capability(ImageContainer.Jpeg, isAnimated: false, ImageContainer.Jpeg).KeepsTransparency, "JPEG lays it on white");
        Assert.IsFalse(exporter.Capability(ImageContainer.Bmp, isAnimated: false, ImageContainer.Bmp).KeepsTransparency);
    }
}
