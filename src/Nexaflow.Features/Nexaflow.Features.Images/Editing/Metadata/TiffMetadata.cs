using Nexaflow.Features.Images.Editing.Codecs;
using System.Buffers.Binary;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// A TIFF's metadata is part of its own image directory: descriptive tags beside the pixel layout, EXIF and GPS
/// directories hanging off it, and XMP (tag 700), IPTC (33723), Photoshop resources (34377) and the ICC profile
/// (34675) as tags of their own. Writing is the TIFF encoder's, which builds the directory itself.
/// </summary>
public sealed class TiffMetadata : IMetadataReader
{
    private const ushort XmpTag = 700, IptcTag = 33723, PhotoshopTag = 34377, IccTag = 34675;
    private const ushort IptcResource = 0x0404;

    public bool Reads(ImageContainer container) => container == ImageContainer.Tiff;

    public MetadataBlocks Read(byte[] file)
    {
        if (TiffReader.Open(file) is not { } reader) return MetadataBlocks.None;
        var directory = reader.ReadDirectory(reader.FirstDirectoryOffset, out _);
        if (directory is null) return MetadataBlocks.None;

        var iptc = directory.Find(IptcTag)?.Value;
        return new MetadataBlocks
        {
            Exif = ExifBlock.FromTiffFile(file),
            Xmp  = directory.Find(XmpTag)?.Value,
            Icc  = directory.Find(IccTag)?.Value,
            PhotoshopResources = directory.Find(PhotoshopTag)?.Value ?? (iptc is null ? null : PhotoshopResourceFor(iptc)),
        };
    }

    /// <summary>
    /// An IPTC record wrapped as the one Photoshop resource a JPEG's APP13 holds it in: <c>8BIM</c>, resource 0x0404,
    /// an empty name, the length, the record, padded to even.
    /// </summary>
    internal static byte[] PhotoshopResourceFor(byte[] iptc)
    {
        var block = new byte[12 + iptc.Length + (iptc.Length & 1)];
        "8BIM"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(4), IptcResource);
        // bytes 6–7: an empty Pascal name, padded to even
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(8), (uint)iptc.Length);
        iptc.CopyTo(block, 12);
        return block;
    }
}
