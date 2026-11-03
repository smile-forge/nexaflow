using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Media.Photoshop;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// What a Photoshop file held for the picture as a whole that Nexaflow does not write afresh: its colour-mode data, the image
/// resources besides resolution, active layers and profile, its global layer mask and its own tagged blocks — written back
/// as they were.
/// </summary>
public sealed record PhotoshopPictureKept(ReadOnlyMemory<byte> ColourModeData, ImmutableArray<PhotoshopResource> Resources,
                                          ReadOnlyMemory<byte> GlobalLayerMask, ImmutableArray<PhotoshopTaggedBlock> Blocks)
{
    public bool Equals(PhotoshopPictureKept? other)
        => other is not null && ColourModeData.Span.SequenceEqual(other.ColourModeData.Span) && GlobalLayerMask.Span.SequenceEqual(other.GlobalLayerMask.Span)
           && Blocks.SequenceEqual(other.Blocks)
           && Resources.Length == other.Resources.Length
           && Resources.Zip(other.Resources).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.Data.Span.SequenceEqual(p.Second.Data.Span));

    public override int GetHashCode() => HashCode.Combine(Resources.Length, Blocks.Length);
}

/// <summary>
/// What a Photoshop layer record held that Nexaflow does not draw or write afresh, kept so a save writes it back:
/// <list type="bullet">
/// <item>its tagged blocks Nexaflow does not read — effects, a vector mask, a colour label — written back always;</item>
/// <item>the blocks its pixels were made from — a text layer's text, a smart object, a shape — written back only while the
/// pixels are still <see cref="ReadPixels"/>, the very tiles read: once painted on, the layer is plain pixels, as Photoshop
/// makes it on rasterizing;</item>
/// <item>its pixels' channels as the file compressed them, written back as they are while the pixels are still
/// <see cref="ReadPixels"/> — so a save compresses only the layers painted on, and an untouched layer keeps every bit;</item>
/// <item>its opacity and fill as written, where fill was folded into <see cref="Layer.Opacity"/>;</item>
/// <item>its mask's flags and parameters, its blending ranges, and a group's open or closed;</item>
/// <item>for a layer Nexaflow cannot draw — an adjustment — its channels as they were compressed.</item>
/// </list>
/// </summary>
public sealed record PhotoshopLayerKept : IUndrawnKept
{
    public ImmutableArray<PhotoshopTaggedBlock> Blocks { get; init; } = [];

    public ImmutableArray<PhotoshopTaggedBlock> PixelBlocks { get; init; } = [];

    /// <summary>The pixels <see cref="PixelBlocks"/> describe; null where there are none.</summary>
    public TiledPixels? ReadPixels { get; init; }

    /// <summary>The channels of <see cref="ReadPixels"/> — transparency and colour — as the file compressed them, the record's
    /// rectangle they cover, and whether the file was a PSB, whose run-length rows count their bytes wider.</summary>
    public (PhotoshopRect Rect, bool Large, ImmutableArray<PhotoshopChannelToWrite> Channels)? ReadChannels { get; init; }

    /// <summary>The opacity and fill bytes as written, and the opacity they were read as — written back while the layer's
    /// opacity is still that.</summary>
    public (byte Opacity, byte Fill, double ReadAs)? OpacityAndFill { get; init; }

    public PhotoshopLayerMask? Mask { get; init; }

    public ReadOnlyMemory<byte> BlendingRanges { get; init; }

    /// <summary>A group's section type: 1 open, 2 closed.</summary>
    public uint SectionType { get; init; }

    /// <summary>The blend key as written, where Nexaflow knows it not.</summary>
    public string? BlendKey { get; init; }

    /// <summary>A layer Nexaflow cannot draw: its record's rectangle and its channels, compressed, as read.</summary>
    public (PhotoshopRect Rect, ImmutableArray<PhotoshopChannelToWrite> Channels)? Undrawable { get; init; }

    public ImmutableArray<string> Undrawn { get; init; } = [];

    /// <summary>The identity of the clipping run this layer heads, which Photoshop has no record of its own for.</summary>
    public Guid? RunId { get; init; }

    public bool Equals(PhotoshopLayerKept? other)
        => other is not null && Blocks.SequenceEqual(other.Blocks) && PixelBlocks.SequenceEqual(other.PixelBlocks)
           && ReferenceEquals(ReadPixels, other.ReadPixels) && Equals(ReadChannels, other.ReadChannels) && OpacityAndFill == other.OpacityAndFill && Equals(Mask, other.Mask)
           && BlendingRanges.Span.SequenceEqual(other.BlendingRanges.Span) && SectionType == other.SectionType && BlendKey == other.BlendKey
           && Undrawn.SequenceEqual(other.Undrawn);

    public override int GetHashCode() => HashCode.Combine(Blocks.Length, PixelBlocks.Length, SectionType);
}

/// <summary>
/// The mark on a group that stands for a Photoshop clipping run: its first layer the base, every other clipped to it, the
/// group's name, opacity, blend and visibility the base's — written back as the base and its clipped layers, not as a group.
/// </summary>
public sealed record PhotoshopClipRun
{
    public static PhotoshopClipRun Mark { get; } = new();
}
