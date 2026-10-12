namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// A picture's metadata as container-neutral blocks, so it can move from one format to another: EXIF as its TIFF
/// structure, XMP as its packet, the ICC profile as its bytes — plus the few blocks only one container has a place
/// for, carried when the output is that container.
/// </summary>
public sealed record MetadataBlocks
{
    public static MetadataBlocks None { get; } = new();

    /// <summary>EXIF: a TIFF header and directories (no <c>Exif\0\0</c> prefix).</summary>
    public byte[]? Exif { get; init; }

    /// <summary>The XMP packet, UTF-8.</summary>
    public byte[]? Xmp { get; init; }

    /// <summary>The ICC colour profile. Kept even when the rest is stripped, so colours stay right.</summary>
    public byte[]? Icc { get; init; }

    /// <summary>A JPEG's Photoshop resource block (APP13), where its IPTC record lives; JPEG output only.</summary>
    public byte[]? PhotoshopResources { get; init; }

    /// <summary>A PNG's textual chunks (tEXt, zTXt, iTXt other than XMP), whole; PNG output only.</summary>
    public IReadOnlyList<byte[]> PngText { get; init; } = [];

    public bool IsEmpty => Exif is null && Xmp is null && Icc is null && PhotoshopResources is null && PngText.Count == 0;

    /// <summary>
    /// The blocks as they should describe the saved picture: EXIF says it is upright and
    /// <paramref name="width"/>×<paramref name="height"/>, and drops its thumbnail of the old pixels.
    /// </summary>
    public MetadataBlocks ForSavedPicture(int width, int height) =>
        Exif is null ? this : this with { Exif = ExifBlock.Upright(Exif, width, height) };

    /// <summary>Only what is needed to show the picture right: the colour profile.</summary>
    public MetadataBlocks Stripped() => new() { Icc = Icc };
}
