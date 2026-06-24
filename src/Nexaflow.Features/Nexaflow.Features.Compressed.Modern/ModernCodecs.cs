using System.IO;
using K4os.Compression.LZ4.Streams;
using Nexaflow.IO.Common;
using ZstdSharp;

namespace Nexaflow.Features.Compressed.Modern;

/// <summary>Zstandard single-stream codec (ZstdSharp.Port). Powers <c>.zst</c> / <c>.tar.zst</c> writes.</summary>
public sealed class ZstdCodec : IStreamCodec
{
    public string Extension => ".zst";
    public string Name => "Zstandard";
    public bool CanCompress => true;
    public Stream Compress(Stream output) => new CompressionStream(output);          // leaves the base stream open
    public Stream Decompress(Stream input) => new DecompressionStream(input, leaveOpen: true);
}

/// <summary>LZ4 single-stream codec (K4os.Compression.LZ4). Powers <c>.lz4</c> / <c>.tar.lz4</c> writes.</summary>
public sealed class Lz4Codec : IStreamCodec
{
    public string Extension => ".lz4";
    public string Name => "LZ4";
    public bool CanCompress => true;
    public Stream Compress(Stream output) => LZ4Stream.Encode(output, leaveOpen: true);
    public Stream Decompress(Stream input) => LZ4Stream.Decode(input, leaveOpen: true);
}
