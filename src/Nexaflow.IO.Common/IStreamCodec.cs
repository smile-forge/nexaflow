using System.IO;

namespace Nexaflow.IO.Common;

/// <summary>
/// A single-stream (de)compressor — gzip, brotli, zstd, lz4, bzip2, …. Unlike an
/// <see cref="IArchiveHandler"/> it carries no directory of its own; the VFS composes it with a tar
/// to produce <c>.tar.zst</c> / <c>.tar.lz4</c> / <c>.tar.br</c> and uses it directly for a
/// single-file <c>.zst</c> target. Discovered by reflection alongside handlers and registered with
/// <see cref="IVirtualFileSystem"/>.
/// </summary>
public interface IStreamCodec
{
    /// <summary>The codec's file extension, lower-case and dotted (e.g. <c>".zst"</c>).</summary>
    string Extension { get; }

    /// <summary>A short human label (e.g. <c>"Zstandard"</c>).</summary>
    string Name { get; }

    /// <summary>False for decode-only codecs (the VFS won't offer them as write/convert targets).</summary>
    bool CanCompress { get; }

    /// <summary>Wraps <paramref name="output"/> in a compressing write stream. Disposing the returned
    /// stream finishes the codec frame; it must leave <paramref name="output"/> open.</summary>
    Stream Compress(Stream output);

    /// <summary>Wraps <paramref name="input"/> in a decompressing read stream (leaves it open).</summary>
    Stream Decompress(Stream input);
}
