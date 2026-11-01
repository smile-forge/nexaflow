using System.IO;
using System.IO.Compression;
using Nexaflow.IO.Common.OpenRaster;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// An OpenRaster package as one picture: its <c>mergedimage.png</c>, the whole picture as the application that wrote it drew
/// it — what the viewer, the carousel, the album and previews show, without reading a layer.
/// </summary>
public sealed class OpenRasterDecoder(IImageDecoder png) : IImageDecoder
{
    public bool Decodes(ImageContainer container) => container == ImageContainer.OpenRaster;

    public DecodedImage Decode(byte[] data, ImageContainer container, int? maxLongEdge)
    {
        byte[] merged;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
            var entry = zip.GetEntry(OpenRasterPackage.MergedImagePath) ?? throw new ImageDecodeException("This OpenRaster picture holds no merged image.");
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            merged = copy.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new ImageDecodeException($"This OpenRaster picture does not read: {ex.Message}");
        }
        var picture = png.Decode(merged, ImageContainer.Png, maxLongEdge);
        // The pixels change hands: the decoded PNG is not disposed, its image now the package's.
        return new DecodedImage(picture.Pixels, picture.Origin, picture.StoredFullSize, ImageContainer.OpenRaster, isAnimated: false, picture.IsHighBitDepth);
    }
}
