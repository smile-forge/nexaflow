using System.Collections.Immutable;

namespace Nexaflow.Media.Photoshop;

/// <summary>The colour mode a Photoshop file's header names.</summary>
public enum PhotoshopColourMode : ushort
{
    Bitmap = 0,
    Grayscale = 1,
    Indexed = 2,
    Rgb = 3,
    Cmyk = 4,
    Multichannel = 7,
    Duotone = 8,
    Lab = 9,
}

/// <summary>How a channel's samples are stored.</summary>
public enum PhotoshopCompression : ushort
{
    Raw = 0,
    Rle = 1,
    Zip = 2,
    ZipPredicted = 3,
}

/// <summary>
/// A Photoshop file's header: PSD, or PSB — the large document format, whose lengths are 64-bit and whose sides reach
/// 300,000 pixels — and the picture's channels, size, bits a channel and colour mode.
/// </summary>
public sealed record PhotoshopFileHeader(bool IsLarge, int Channels, int Width, int Height, int Depth, PhotoshopColourMode Mode)
{
    /// <summary>The longest side a PSD holds; past it, a PSB.</summary>
    public const int PsdMaxSide = 30000;

    /// <summary>The longest side a PSB holds.</summary>
    public const int PsbMaxSide = 300000;

    /// <summary>The bytes of one sample: 1, 2 or 4; a 1-bit picture's row is its own count.</summary>
    public int SampleBytes => Depth switch { 16 => 2, 32 => 4, _ => 1 };
}

/// <summary>One image resource, as written: its id, its name and its data.</summary>
public sealed record PhotoshopResource(ushort Id, string Name, ReadOnlyMemory<byte> Data)
{
    /// <summary>The resolution: horizontal and vertical, in pixels an inch or a centimetre.</summary>
    public const ushort ResolutionInfo = 1005;

    /// <summary>The layer active when the file was saved, by its index — the old form.</summary>
    public const ushort LayerState = 1024;

    /// <summary>The ICC profile.</summary>
    public const ushort IccProfile = 1039;

    /// <summary>The layers selected when the file was saved, by their ids.</summary>
    public const ushort LayerSelectionIds = 1069;

    /// <summary>The thumbnail, as a JPEG.</summary>
    public const ushort Thumbnail = 1036;
}

/// <summary>
/// One tagged block of additional information — a layer's or the file's — as written: its signature (<c>8BIM</c> or
/// <c>8B64</c>), its four-character key, and its data.
/// </summary>
public sealed record PhotoshopTaggedBlock(string Signature, string Key, ReadOnlyMemory<byte> Data)
{
    /// <summary>A block in Photoshop's own signature.</summary>
    public static PhotoshopTaggedBlock Of(string key, ReadOnlyMemory<byte> data) => new("8BIM", key, data);

    public bool Equals(PhotoshopTaggedBlock? other)
        => other is not null && Signature == other.Signature && Key == other.Key && Data.Span.SequenceEqual(other.Data.Span);

    public override int GetHashCode() => HashCode.Combine(Signature, Key, Data.Length);
}

/// <summary>A rectangle of a Photoshop file — a layer's or a mask's — top, left, bottom and right, in pixels.</summary>
public readonly record struct PhotoshopRect(int Top, int Left, int Bottom, int Right)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsEmpty => Width == 0 || Height == 0;
}

/// <summary>
/// A layer's user mask as its record describes it: where its pixels lie, the value everywhere else, and its flags — disabled,
/// inverted, positioned relative to its layer — with the bytes it was read from kept so a mask with parameters Nexaflow does
/// not use is written back as it was.
/// </summary>
public sealed record PhotoshopLayerMask(PhotoshopRect Rect, byte DefaultColour, byte Flags)
{
    public const byte RelativeFlag = 1, DisabledFlag = 2, InvertFlag = 4;

    public bool Disabled => (Flags & DisabledFlag) != 0;

    /// <summary>The bytes of the mask's record past its rectangle, colour and flags — parameters, the real mask — as read.</summary>
    public ReadOnlyMemory<byte> Rest { get; init; }

    public bool Equals(PhotoshopLayerMask? other)
        => other is not null && Rect == other.Rect && DefaultColour == other.DefaultColour && Flags == other.Flags && Rest.Span.SequenceEqual(other.Rest.Span);

    public override int GetHashCode() => HashCode.Combine(Rect, DefaultColour, Flags);
}

/// <summary>
/// One channel of a layer as it lies in the file read: its id — -1 the transparency, 0 and up the colour, -2 the user mask,
/// -3 the real user mask — and where its compressed samples lie.
/// </summary>
public readonly record struct PhotoshopChannelData(short Id, long Offset, long Length);

/// <summary>
/// One layer record of a Photoshop file — a layer, or one end of a group — as read: everything it says of itself, its
/// channels' places in the file, and every tagged block it carries, so what is not understood is written back as it was.
/// </summary>
public sealed record PhotoshopLayerRecord
{
    public PhotoshopRect Rect { get; init; }

    public ImmutableArray<PhotoshopChannelData> Channels { get; init; } = [];

    /// <summary>The blend key: <c>norm</c>, <c>mul </c>, <c>pass</c> for a pass-through group, …</summary>
    public string BlendKey { get; init; } = "norm";

    public byte Opacity { get; init; } = 255;

    /// <summary>0 for a base layer, 1 for one clipped to the layer beneath.</summary>
    public byte Clipping { get; init; }

    /// <summary>Bit 0 transparency protected, bit 1 hidden, bit 3 bit 4 is meaningful, bit 4 pixels irrelevant to the look.</summary>
    public byte Flags { get; init; }

    public const byte TransparencyProtectedFlag = 1, HiddenFlag = 2;

    public PhotoshopLayerMask? Mask { get; init; }

    /// <summary>The blending ranges, as written.</summary>
    public ReadOnlyMemory<byte> BlendingRanges { get; init; }

    /// <summary>The name in the record's own Pascal string — what <c>luni</c>'s Unicode name is preferred to.</summary>
    public string PascalName { get; init; } = "";

    public ImmutableArray<PhotoshopTaggedBlock> Blocks { get; init; } = [];

    /// <summary>The block of <paramref name="key"/>, the first where there are several; null where there is none.</summary>
    public PhotoshopTaggedBlock? Block(string key)
    {
        foreach (var block in Blocks)
            if (block.Key == key) return block;
        return null;
    }
}

/// <summary>
/// A Photoshop file as read: its header, colour-mode data, image resources, layer records bottom first, global layer mask
/// information and tagged blocks, and where its merged image lies — every channel's samples left in the file, read on
/// demand, so a large document is never held whole.
/// </summary>
public sealed record PhotoshopDocument
{
    public required PhotoshopFileHeader Header { get; init; }

    public ReadOnlyMemory<byte> ColourModeData { get; init; }

    public ImmutableArray<PhotoshopResource> Resources { get; init; } = [];

    /// <summary>The layer records, bottom first, as the file lists them — group ends among them.</summary>
    public ImmutableArray<PhotoshopLayerRecord> Layers { get; init; } = [];

    /// <summary>Whether the merged image's first extra channel is its transparency (a negative layer count).</summary>
    public bool MergedHasTransparency { get; init; }

    public ReadOnlyMemory<byte> GlobalLayerMask { get; init; }

    /// <summary>The file's own tagged blocks, past its layers — those holding a 16- or 32-bit file's layers not among them.</summary>
    public ImmutableArray<PhotoshopTaggedBlock> GlobalBlocks { get; init; } = [];

    /// <summary>Where the merged image's data starts: its compression, then its channels.</summary>
    public long MergedOffset { get; init; }

    /// <summary>The resource of <paramref name="id"/>; null where there is none.</summary>
    public PhotoshopResource? Resource(ushort id)
    {
        foreach (var resource in Resources)
            if (resource.Id == id) return resource;
        return null;
    }
}
