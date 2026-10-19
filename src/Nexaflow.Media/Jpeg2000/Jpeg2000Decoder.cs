using System.Runtime.InteropServices;

namespace Nexaflow.Media.Jpeg2000;

/// <summary>
/// Decodes JPEG 2000 — a JP2 file of boxes or a bare codestream, told apart by their first bytes — into its
/// components, at full resolution.
/// </summary>
public static class Jpeg2000Decoder
{
    /// <summary>Decodes the whole image.</summary>
    /// <exception cref="Jpeg2000Exception">The data is not JPEG 2000, or is damaged past decoding.</exception>
    public static Jpeg2000Image Decode(ReadOnlyMemory<byte> data, Jpeg2000DecodeOptions? options = null)
    {
        options ??= Jpeg2000DecodeOptions.Default;

        // The decoder reports why it failed only to an event manager; the last error it reported is the one that
        // stopped it.
        string? error = null;
        var info = new CompressionInfo(FormatOf(data.Span))
        {
            EventManager = new EventMgr((message, _) => error = message, null, null),
        };
        var parameters = new DecompressionParameters
        {
            flags = options.ApplyPalette ? 0 : DecompressionParameters.DPARAMETERS.IGNORE_PCLR_CMAP_CDEF_FLAG,
            DisableMultiThreading = !options.Parallel,
        };

        using var input = Readable(data);
        try
        {
            if (!info.SetupDecoder(info.OpenCIO(input), parameters)) throw Failed("set up the decoder", error);
            if (!info.ReadHeader(out var image) || image is null) throw Failed("read the header", error);
            if (!info.Decode(image)) throw Failed("decode the image", error);
            if (!info.EndDecompress()) throw Failed("end the decode", error);

            return new Jpeg2000Image(image);
        }
        catch (EndOfStreamException)
        {
            // A marker segment the data ends inside: the reader runs off the end where libopenjp2 checks the length.
            throw new Jpeg2000Exception("the data ends before the image does");
        }
    }

    /// <summary>A bare codestream starts with its SOC marker; a JP2 file with its 12-byte signature box.</summary>
    private static CodecFormat FormatOf(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0x4F && data[2] == 0xFF && data[3] == 0x51)
            return CodecFormat.Jpeg2K;

        ReadOnlySpan<byte> signature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
        if (data.StartsWith(signature))
            return CodecFormat.Jpeg2P;

        throw new Jpeg2000Exception("not JPEG 2000: neither a JP2 signature box nor a codestream's SOC marker");
    }

    /// <summary>The bytes as a stream, over the caller's array when there is one, so nothing is copied.</summary>
    private static MemoryStream Readable(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);

    private static Jpeg2000Exception Failed(string step, string? error) =>
        new(error is null ? $"could not {step}" : $"could not {step}: {error.Trim()}");
}
