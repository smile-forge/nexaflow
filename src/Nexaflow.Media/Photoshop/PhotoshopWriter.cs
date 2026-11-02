using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace Nexaflow.Media.Photoshop;

/// <summary>One channel of a layer to write: its id and its samples already compressed, compression first
/// (<see cref="PhotoshopChannels.Encode"/>).</summary>
public sealed record PhotoshopChannelToWrite(short Id, byte[] Encoded);

/// <summary>One layer record to write — a layer, or one end of a group — its channels compressed, its tagged blocks as given.</summary>
public sealed record PhotoshopLayerToWrite
{
    public PhotoshopRect Rect { get; init; }
    public ImmutableArray<PhotoshopChannelToWrite> Channels { get; init; } = [];
    public string BlendKey { get; init; } = "norm";
    public byte Opacity { get; init; } = 255;
    public byte Clipping { get; init; }
    public byte Flags { get; init; }
    public PhotoshopLayerMask? Mask { get; init; }
    public ReadOnlyMemory<byte> BlendingRanges { get; init; }
    public string PascalName { get; init; } = "";
    public ImmutableArray<PhotoshopTaggedBlock> Blocks { get; init; } = [];
}

/// <summary>A Photoshop file to write: its header, colour-mode data, resources, layers bottom first, the file's own blocks,
/// and its merged image as planar channels in the machine's order.</summary>
public sealed record PhotoshopDocumentToWrite
{
    public required PhotoshopFileHeader Header { get; init; }
    public ReadOnlyMemory<byte> ColourModeData { get; init; }
    public ImmutableArray<PhotoshopResource> Resources { get; init; } = [];
    public ImmutableArray<PhotoshopLayerToWrite> Layers { get; init; } = [];
    public bool MergedHasTransparency { get; init; }
    public ReadOnlyMemory<byte> GlobalLayerMask { get; init; }
    public ImmutableArray<PhotoshopTaggedBlock> GlobalBlocks { get; init; } = [];

    /// <summary>The merged image's channels — colour, then transparency — each its rows of samples.</summary>
    public required IReadOnlyList<byte[]> Merged { get; init; }
}

/// <summary>
/// Writes a Photoshop file — PSD, or PSB where its header says — front to back in one pass: every length is counted from what
/// is to be written before it is, so nothing is held twice and nothing is gone back over. An 8-bit file's layers go in its
/// layer info; a 16- or 32-bit file's in its <c>Lr16</c> or <c>Lr32</c> block, as Photoshop writes them. Every tagged
/// block's data is padded to four bytes, the padding counted in its length.
/// </summary>
public static class PhotoshopWriter
{
    private static readonly HashSet<string> LongKeys = ["LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD"];

    public static void Write(Stream output, PhotoshopDocumentToWrite document)
    {
        var header = document.Header;
        bool large = header.IsLarge;
        output.Write("8BPS"u8);
        U16(output, (ushort)(large ? 2 : 1));
        output.Write(new byte[6]);
        U16(output, (ushort)header.Channels);
        U32(output, (uint)header.Height);
        U32(output, (uint)header.Width);
        U16(output, (ushort)header.Depth);
        U16(output, (ushort)header.Mode);

        U32(output, (uint)document.ColourModeData.Length);
        output.Write(document.ColourModeData.Span);

        U32(output, (uint)document.Resources.Sum(ResourceSize));
        foreach (var resource in document.Resources) WriteResource(output, resource);

        // The layers in the layer info for 8 bits; for more, in a block of their own among the file's.
        bool inBlock = header.Depth > 8;
        long info = LayerInfoSize(document, large);
        long paddedInfo = Padded(info, 4);
        var blocks = document.GlobalBlocks;
        long blocksSize = blocks.Sum(b => BlockSize(b.Signature, b.Key, b.Data.Length, large))
                          + (inBlock ? BlockSize("8BIM", header.Depth == 32 ? "Lr32" : "Lr16", paddedInfo, large) : 0);
        long section = (large ? 8 : 4) + (inBlock ? 0 : paddedInfo) + 4 + document.GlobalLayerMask.Length + blocksSize;
        Length(output, section, large);
        Length(output, inBlock ? 0 : paddedInfo, large);
        if (!inBlock) WriteLayerInfo(output, document, large, paddedInfo - info);
        U32(output, (uint)document.GlobalLayerMask.Length);
        output.Write(document.GlobalLayerMask.Span);
        if (inBlock)
        {
            BlockHead(output, "8BIM", header.Depth == 32 ? "Lr32" : "Lr16", paddedInfo, large);
            WriteLayerInfo(output, document, large, paddedInfo - info);
        }
        foreach (var block in blocks) WriteBlock(output, block, large);

        PhotoshopChannels.WriteMerged(output, document.Merged, header.Width, header.Height, header.SampleBytes, large);
    }

    // ── Layer info ──────────────────────────────────────────────────────────

    private static long LayerInfoSize(PhotoshopDocumentToWrite document, bool large)
        => 2 + document.Layers.Sum(l => RecordSize(l, large) + l.Channels.Sum(c => (long)c.Encoded.Length));

    private static void WriteLayerInfo(Stream output, PhotoshopDocumentToWrite document, bool large, long padding)
    {
        short count = (short)document.Layers.Length;
        U16(output, (ushort)(document.MergedHasTransparency ? -count : count));
        foreach (var layer in document.Layers) WriteRecord(output, layer, large);
        foreach (var layer in document.Layers)
            foreach (var channel in layer.Channels) output.Write(channel.Encoded);
        output.Write(new byte[padding]);
    }

    private static long RecordSize(PhotoshopLayerToWrite layer, bool large)
        => 16 + 2 + layer.Channels.Length * (2 + (large ? 8 : 4)) + 4 + 4 + 4 + 4 + ExtraSize(layer, large);

    private static long ExtraSize(PhotoshopLayerToWrite layer, bool large)
        => 4 + MaskSize(layer.Mask) + 4 + layer.BlendingRanges.Length + PascalSize(layer.PascalName)
           + layer.Blocks.Sum(b => BlockSize(b.Signature, b.Key, b.Data.Length, large));

    private static int MaskSize(PhotoshopLayerMask? mask) => mask is null ? 0 : 18 + mask.Rest.Length;

    private static void WriteRecord(Stream output, PhotoshopLayerToWrite layer, bool large)
    {
        Rect(output, layer.Rect);
        U16(output, (ushort)layer.Channels.Length);
        foreach (var channel in layer.Channels)
        {
            U16(output, (ushort)channel.Id);
            Length(output, channel.Encoded.Length, large);
        }
        output.Write("8BIM"u8);
        output.Write(Encoding.Latin1.GetBytes(layer.BlendKey.PadRight(4)[..4]));
        output.WriteByte(layer.Opacity);
        output.WriteByte(layer.Clipping);
        output.WriteByte(layer.Flags);
        output.WriteByte(0);
        U32(output, (uint)ExtraSize(layer, large));

        U32(output, (uint)MaskSize(layer.Mask));
        if (layer.Mask is { } mask)
        {
            Rect(output, mask.Rect);
            output.WriteByte(mask.DefaultColour);
            output.WriteByte(mask.Flags);
            output.Write(mask.Rest.Span);
        }
        U32(output, (uint)layer.BlendingRanges.Length);
        output.Write(layer.BlendingRanges.Span);
        WritePascal(output, layer.PascalName);
        foreach (var block in layer.Blocks) WriteBlock(output, block, large);
    }

    // ── Resources and blocks ────────────────────────────────────────────────

    private static long ResourceSize(PhotoshopResource resource)
        => 4 + 2 + Padded(1 + Name(resource.Name).Length, 2) + 4 + Padded(resource.Data.Length, 2);

    private static void WriteResource(Stream output, PhotoshopResource resource)
    {
        output.Write("8BIM"u8);
        U16(output, resource.Id);
        var name = Name(resource.Name);
        output.WriteByte((byte)name.Length);
        output.Write(name);
        if ((1 + name.Length) % 2 != 0) output.WriteByte(0);
        U32(output, (uint)resource.Data.Length);
        output.Write(resource.Data.Span);
        if (resource.Data.Length % 2 != 0) output.WriteByte(0);
    }

    private static long BlockSize(string signature, string key, long dataLength, bool large)
        => 8 + (LongLength(signature, key, large) ? 8 : 4) + Padded(dataLength, 4);

    private static bool LongLength(string signature, string key, bool large) => large && (signature == "8B64" || LongKeys.Contains(key));

    private static void BlockHead(Stream output, string signature, string key, long paddedLength, bool large)
    {
        output.Write(Encoding.Latin1.GetBytes(signature));
        output.Write(Encoding.Latin1.GetBytes(key));
        if (LongLength(signature, key, large)) U64(output, (ulong)paddedLength);
        else U32(output, checked((uint)paddedLength));
    }

    private static void WriteBlock(Stream output, PhotoshopTaggedBlock block, bool large)
    {
        long padded = Padded(block.Data.Length, 4);
        BlockHead(output, block.Signature, block.Key, padded, large);
        output.Write(block.Data.Span);
        output.Write(new byte[padded - block.Data.Length]);
    }

    // ── Bytes ───────────────────────────────────────────────────────────────

    private static long Padded(long length, int to) => (length + to - 1) / to * to;

    private static byte[] Name(string name)
    {
        var bytes = Encoding.Latin1.GetBytes(name);
        return bytes.Length > 255 ? bytes[..255] : bytes;
    }

    private static long PascalSize(string name) => Padded(1 + Name(name).Length, 4);

    private static void WritePascal(Stream output, string name)
    {
        var bytes = Name(name);
        output.WriteByte((byte)bytes.Length);
        output.Write(bytes);
        output.Write(new byte[PascalSize(name) - 1 - bytes.Length]);
    }

    private static void Rect(Stream output, PhotoshopRect rect)
    {
        U32(output, (uint)rect.Top);
        U32(output, (uint)rect.Left);
        U32(output, (uint)rect.Bottom);
        U32(output, (uint)rect.Right);
    }

    private static void Length(Stream output, long length, bool large)
    {
        if (large) U64(output, (ulong)length);
        else U32(output, checked((uint)length));
    }

    private static void U16(Stream output, ushort value) => PhotoshopChannels.WriteUInt16(output, value);

    private static void U32(Stream output, uint value) => PhotoshopChannels.WriteUInt32(output, value);

    private static void U64(Stream output, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        output.Write(bytes);
    }
}
