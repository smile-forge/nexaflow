using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace Nexaflow.Media.Photoshop;

/// <summary>
/// Reads a Photoshop file — PSD or PSB — as Adobe's file format specification lays it out: header, colour-mode data, image
/// resources, the layer and mask section (a 16- or 32-bit file's layers in its <c>Lr16</c> or <c>Lr32</c> block), and the
/// merged image. Only structure is read; every channel's samples are left in the file, their places noted, and read through
/// <see cref="PhotoshopChannels"/> when they are wanted.
/// </summary>
public static class PhotoshopReader
{
    /// <summary>The keys whose blocks, in a PSB, give their length in 8 bytes.</summary>
    private static readonly HashSet<string> LongKeys = ["LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD"];

    /// <summary>The keys of the blocks that hold a file's layers in place of its layer info.</summary>
    private static readonly HashSet<string> LayerInfoKeys = ["Lr16", "Lr32", "Layr"];

    /// <summary>Whether <paramref name="header"/> — a file's first bytes — opens a PSD or a PSB.</summary>
    public static bool IsPhotoshop(ReadOnlySpan<byte> header)
        => header.Length >= 6 && header[..4].SequenceEqual("8BPS"u8) && BinaryPrimitives.ReadUInt16BigEndian(header[4..]) is 1 or 2;

    /// <summary>The structure of the file <paramref name="stream"/> holds, which must be seekable and stay open while its
    /// channels are read.</summary>
    /// <exception cref="InvalidDataException">It is not a Photoshop file, or one cut short.</exception>
    public static PhotoshopDocument Read(Stream stream)
    {
        try
        {
            return ReadDocument(stream);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The Photoshop file ends before its structure does.", ex);
        }
    }

    private static PhotoshopDocument ReadDocument(Stream stream)
    {
        stream.Position = 0;
        Span<byte> signature = stackalloc byte[4];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual("8BPS"u8)) throw new InvalidDataException("Not a Photoshop file.");
        ushort version = U16(stream);
        if (version is not 1 and not 2) throw new InvalidDataException($"No Photoshop file is version {version}.");
        bool large = version == 2;
        Skip(stream, 6);
        int channels = U16(stream);
        int height = (int)U32(stream), width = (int)U32(stream);
        int depth = U16(stream);
        var mode = (PhotoshopColourMode)U16(stream);
        var header = new PhotoshopFileHeader(large, channels, width, height, depth, mode);

        var colourModeData = Bytes(stream, U32(stream));
        var resources = ReadResources(stream, U32(stream));

        long sectionLength = large ? (long)U64(stream) : U32(stream);
        long sectionEnd = stream.Position + sectionLength;
        var layers = ImmutableArray<PhotoshopLayerRecord>.Empty;
        bool mergedHasTransparency = false;
        ReadOnlyMemory<byte> globalMask = default;
        var globalBlocks = ImmutableArray.CreateBuilder<PhotoshopTaggedBlock>();
        if (sectionLength > 0)
        {
            long infoLength = large ? (long)U64(stream) : U32(stream);
            long infoEnd = stream.Position + infoLength;
            if (infoLength > 0) (layers, mergedHasTransparency) = ReadLayerInfo(stream, infoEnd, large);
            stream.Position = infoEnd;
            if (stream.Position + 4 <= sectionEnd)
            {
                uint maskLength = U32(stream);
                globalMask = Bytes(stream, maskLength);
            }
            while (NextBlock(stream, sectionEnd))
            {
                var (blockSignature, key, length) = BlockHead(stream, large);
                long dataEnd = stream.Position + length;
                if (LayerInfoKeys.Contains(key) && layers.IsEmpty)
                    (layers, mergedHasTransparency) = ReadLayerInfo(stream, dataEnd, large);
                else if (!LayerInfoKeys.Contains(key))
                    globalBlocks.Add(new PhotoshopTaggedBlock(blockSignature, key, Bytes(stream, length)));
                stream.Position = dataEnd;
            }
        }
        stream.Position = sectionEnd;

        return new PhotoshopDocument
        {
            Header = header,
            ColourModeData = colourModeData,
            Resources = resources,
            Layers = layers,
            MergedHasTransparency = mergedHasTransparency,
            GlobalLayerMask = globalMask,
            GlobalBlocks = globalBlocks.ToImmutable(),
            MergedOffset = sectionEnd,
        };
    }

    // ── Image resources ─────────────────────────────────────────────────────

    private static ImmutableArray<PhotoshopResource> ReadResources(Stream stream, long length)
    {
        long end = stream.Position + length;
        var resources = ImmutableArray.CreateBuilder<PhotoshopResource>();
        Span<byte> signature = stackalloc byte[4];
        while (stream.Position + 12 <= end)
        {
            stream.ReadExactly(signature);
            ushort id = U16(stream);
            var name = PascalString(stream, 2);
            uint size = U32(stream);
            var data = Bytes(stream, size);
            if ((size & 1) == 1) Skip(stream, 1);
            resources.Add(new PhotoshopResource(id, name, data));
        }
        stream.Position = end;
        return resources.ToImmutable();
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    /// <summary>The layer records of a layer info section ending at <paramref name="end"/>, their channels' places noted.</summary>
    private static (ImmutableArray<PhotoshopLayerRecord> Layers, bool MergedHasTransparency) ReadLayerInfo(Stream stream, long end, bool large)
    {
        short count = (short)U16(stream);
        bool transparency = count < 0;
        int layers = Math.Abs((int)count);
        var records = new List<PhotoshopLayerRecord>(layers);
        for (int i = 0; i < layers; i++) records.Add(ReadRecord(stream, large));

        // The channels' samples follow every record, layer by layer, channel by channel.
        long at = stream.Position;
        var placed = ImmutableArray.CreateBuilder<PhotoshopLayerRecord>(layers);
        foreach (var record in records)
        {
            var channels = ImmutableArray.CreateBuilder<PhotoshopChannelData>(record.Channels.Length);
            foreach (var channel in record.Channels)
            {
                channels.Add(channel with { Offset = at });
                at += channel.Length;
            }
            placed.Add(record with { Channels = channels.MoveToImmutable() });
        }
        if (at > end) throw new InvalidDataException("A layer's channels run past their section.");
        return (placed.MoveToImmutable(), transparency);
    }

    private static PhotoshopLayerRecord ReadRecord(Stream stream, bool large)
    {
        var rect = new PhotoshopRect((int)U32(stream), (int)U32(stream), (int)U32(stream), (int)U32(stream));
        int channelCount = U16(stream);
        var channels = ImmutableArray.CreateBuilder<PhotoshopChannelData>(channelCount);
        for (int c = 0; c < channelCount; c++)
        {
            short id = (short)U16(stream);
            long length = large ? (long)U64(stream) : U32(stream);
            channels.Add(new PhotoshopChannelData(id, 0, length));
        }
        Skip(stream, 4);   // the blend mode's signature, 8BIM
        var blendKey = Key(stream);
        byte opacity = U8(stream), clipping = U8(stream), flags = U8(stream);
        Skip(stream, 1);
        uint extra = U32(stream);
        long extraEnd = stream.Position + extra;

        PhotoshopLayerMask? mask = null;
        uint maskSize = U32(stream);
        if (maskSize >= 18)
        {
            long maskEnd = stream.Position + maskSize;
            var maskRect = new PhotoshopRect((int)U32(stream), (int)U32(stream), (int)U32(stream), (int)U32(stream));
            byte colour = U8(stream), maskFlags = U8(stream);
            mask = new PhotoshopLayerMask(maskRect, colour, maskFlags) { Rest = Bytes(stream, maskEnd - stream.Position) };
        }
        else Skip(stream, maskSize);

        var ranges = Bytes(stream, U32(stream));
        var name = PascalString(stream, 4);
        var blocks = ImmutableArray.CreateBuilder<PhotoshopTaggedBlock>();
        while (NextBlock(stream, extraEnd))
        {
            var (signature, key, length) = BlockHead(stream, large);
            long dataEnd = stream.Position + length;
            blocks.Add(new PhotoshopTaggedBlock(signature, key, Bytes(stream, length)));
            stream.Position = dataEnd;
        }
        stream.Position = extraEnd;

        return new PhotoshopLayerRecord
        {
            Rect = rect, Channels = channels.MoveToImmutable(), BlendKey = blendKey, Opacity = opacity, Clipping = clipping, Flags = flags,
            Mask = mask, BlendingRanges = ranges, PascalName = name, Blocks = blocks.ToImmutable(),
        };
    }

    // ── Tagged blocks ───────────────────────────────────────────────────────

    /// <summary>
    /// Whether another tagged block starts before <paramref name="end"/>, the stream left on its signature. Writers pad a
    /// block's data to 2 or 4 bytes and do not all count the padding in its length, so up to three bytes of padding are passed
    /// over to find the next signature.
    /// </summary>
    private static bool NextBlock(Stream stream, long end)
    {
        Span<byte> signature = stackalloc byte[4];
        for (int pad = 0; pad < 4; pad++)
        {
            long at = stream.Position;
            if (at + 12 > end) return false;
            stream.ReadExactly(signature);
            stream.Position = at;
            if (signature.SequenceEqual("8BIM"u8) || signature.SequenceEqual("8B64"u8)) return true;
            stream.Position = at + 1;
        }
        return false;
    }

    private static (string Signature, string Key, long Length) BlockHead(Stream stream, bool large)
    {
        var signature = Key(stream);
        var key = Key(stream);
        long length = large && (signature == "8B64" || LongKeys.Contains(key)) ? (long)U64(stream) : U32(stream);
        return (signature, key, length);
    }

    // ── Bytes ───────────────────────────────────────────────────────────────

    private static byte U8(Stream stream)
    {
        int value = stream.ReadByte();
        return value < 0 ? throw new EndOfStreamException() : (byte)value;
    }

    private static ushort U16(Stream stream) => PhotoshopChannels.ReadUInt16(stream);

    private static uint U32(Stream stream) => PhotoshopChannels.ReadUInt32(stream);

    private static ulong U64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private static string Key(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return Encoding.Latin1.GetString(bytes);
    }

    private static ReadOnlyMemory<byte> Bytes(Stream stream, long length)
    {
        if (length <= 0) return ReadOnlyMemory<byte>.Empty;
        if (stream.Position + length > stream.Length) throw new EndOfStreamException();
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void Skip(Stream stream, long count) => stream.Position += count;

    /// <summary>A Pascal string — a length byte, then that many bytes — its whole padded to a multiple of <paramref name="padding"/>.</summary>
    private static string PascalString(Stream stream, int padding)
    {
        int length = U8(stream);
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        int total = 1 + length;
        if (total % padding != 0) Skip(stream, padding - total % padding);
        return Encoding.Latin1.GetString(bytes);
    }
}
