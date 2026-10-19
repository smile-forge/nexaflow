using Nexaflow.Media.Jpeg.Internal;

namespace Nexaflow.Media.Jpeg;

/// <summary>
/// Decodes JPEG — baseline, extended, progressive and lossless, Huffman or arithmetic coded — to the samples
/// libjpeg-turbo decodes it to, bit for bit.
/// </summary>
public static class JpegDecoder
{
    /// <summary>Reads the markers up to the first scan: what the image is, without decoding it.</summary>
    /// <exception cref="JpegException">The data is not JPEG, or its markers are damaged past reading.</exception>
    public static JpegHeader Identify(ReadOnlyMemory<byte> data)
    {
        var decompressor = new Decompressor(data, parallel: false);
        decompressor.ReadHeader();
        return decompressor.Header();
    }

    /// <summary>Decodes the whole image.</summary>
    /// <exception cref="JpegException">The data is not JPEG, or is damaged past decoding, or is a kind libjpeg-turbo's
    /// 8-bit decoder refuses: 12-bit, 16-bit, or arithmetic-coded lossless.</exception>
    public static JpegImage Decode(ReadOnlyMemory<byte> data, JpegDecodeOptions? options = null)
    {
        options ??= JpegDecodeOptions.Default;
        var decompressor = new Decompressor(data, options.Parallel);
        decompressor.ReadHeader();
        if (options.SourceColourSpace is { } colourSpace) decompressor.JpegColorSpace = colourSpace;
        return decompressor.Decompress();
    }
}
