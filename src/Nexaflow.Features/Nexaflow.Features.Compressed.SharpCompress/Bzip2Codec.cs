using System.IO;
using Nexaflow.IO.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Nexaflow.Features.Compressed.SharpCompress;

/// <summary>bzip2 single-stream codec (SharpCompress). Powers <c>.bz2</c> / <c>.tar.bz2</c> writes.
/// (xz stays decode-only — SharpCompress has no xz writer — so it is not offered as a codec.)</summary>
public sealed class Bzip2Codec : IStreamCodec
{
    public string Extension => ".bz2";
    public string Name => "bzip2";
    public bool CanCompress => true;
    public Stream Compress(Stream output) => BZip2Stream.Create(output, CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false);
    public Stream Decompress(Stream input) => BZip2Stream.Create(input, CompressionMode.Decompress, decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false);
}
