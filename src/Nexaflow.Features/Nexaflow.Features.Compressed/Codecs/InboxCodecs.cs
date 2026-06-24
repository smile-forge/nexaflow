using System.IO;
using System.IO.Compression;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.Codecs;

/// <summary>gzip single-stream codec, via in-box <see cref="GZipStream"/> (no extra dependency).</summary>
public sealed class GzipCodec : IStreamCodec
{
    public string Extension => ".gz";
    public string Name => "gzip";
    public bool CanCompress => true;
    public Stream Compress(Stream output) => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
    public Stream Decompress(Stream input) => new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
}

/// <summary>Brotli single-stream codec, via in-box <see cref="BrotliStream"/> (no extra dependency).</summary>
public sealed class BrotliCodec : IStreamCodec
{
    public string Extension => ".br";
    public string Name => "Brotli";
    public bool CanCompress => true;
    public Stream Compress(Stream output) => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true);
    public Stream Decompress(Stream input) => new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true);
}
