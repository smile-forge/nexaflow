using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.Lzw;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace Nexaflow.Features.Compressed.SharpCompress;

/// <summary>
/// Read-only archive backend covering the formats SharpCompress handles that the in-box zip handler
/// does not: tar, 7-Zip, RAR, and the single-stream compressors (gzip/bzip2/xz/Unix compress). Compressed tars
/// (<c>.tar.gz</c> etc.) surface their inner <c>.tar</c>, which the VFS then descends into via the tar
/// handler. Listing + extraction only — these formats are not rewritten in place.
/// </summary>
public sealed class SharpCompressHandler : IArchiveHandler
{
    private static readonly string[] Compound =
        [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.lzma", ".tar.z", ".tgz", ".tbz2", ".tbz", ".txz", ".taz"];

    private static readonly string[] Simple =
        [".tar", ".7z", ".rar", ".gz", ".bz2", ".xz", ".z", ".lz", ".lzma"];

    public string Name => "SharpCompress";

    // Create covers tar / tar.gz / tar.bz2 only (Write throws for 7z/rar, which SharpCompress can't write).
    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.List | ArchiveCapabilities.Extract | ArchiveCapabilities.Create;

    // ArchiveFactory can't open these: it has no archive for bzip2 / xz / lzip / lzma / Unix compress (.Z), and a
    // gzipped tar it recognises as a tar, then cannot read compressed. So they are decompressed explicitly
    // into a single inner stream that the VFS nests into. A plain .gz still goes through ArchiveFactory,
    // whose gzip archive carries the original size and time.
    private static readonly RawCodec[] RawCodecs =
    [
        new("bzip2", [".tar.bz2", ".tbz2", ".tbz", ".bz2"],
            s => BZip2Stream.Create(s, CompressionMode.Decompress, decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false)),
        new("gzip", [".tar.gz", ".tgz"],
            s => new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true)),
        new("xz", [".tar.xz", ".txz", ".xz"], s => new XZStream(s)),
        new("lzip", [".tar.lz", ".lz"], s => LZipStream.Create(s, CompressionMode.Decompress, leaveOpen: true)),
        new("lzma", [".tar.lzma", ".lzma"], LzmaAloneStream),
        new("lzw", [".tar.z", ".taz", ".z"], s => new LzwStream(s) { IsStreamOwner = false }),
    ];

    /// <summary>The raw codec whose extension ends <paramref name="lowerFileName"/>, or null.</summary>
    private static RawCodec? RawCodecFor(string lowerFileName)
        => RawCodecs.FirstOrDefault(c => c.Extensions.Any(e => lowerFileName.EndsWith(e, StringComparison.Ordinal)));

    /// <summary>Decodes an <c>.lzma</c> ("LZMA alone") stream: five bytes of coder properties, the decoded
    /// length as eight little-endian bytes (all ones when the writer did not know it, the data then ending in
    /// an end marker), then the LZMA data. Leaves <paramref name="input"/> open.</summary>
    private static Stream LzmaAloneStream(Stream input)
    {
        Span<byte> header = stackalloc byte[13];
        input.ReadExactly(header);
        long decodedLength = BinaryPrimitives.ReadInt64LittleEndian(header[5..]);
        return LzmaStream.Create(header[..5].ToArray(), input, inputSize: -1, outputSize: decodedLength, leaveOpen: true);
    }
    public bool CanHandle(string fileName)
    {
        var lower = (fileName ?? string.Empty).ToLowerInvariant();
        if (Compound.Any(c => lower.EndsWith(c, StringComparison.Ordinal))) return true;
        return Simple.Contains(Path.GetExtension(lower));
    }

    public bool CanWrite(string fileName)
    {
        var lower = (fileName ?? string.Empty).ToLowerInvariant();
        // SharpCompress writes only the tar family (WriterFactory: Tar + None/GZip/BZip2). Not 7z/rar/xz.
        return lower.EndsWith(".tar", StringComparison.Ordinal)
            || lower.EndsWith(".tar.gz", StringComparison.Ordinal) || lower.EndsWith(".tgz", StringComparison.Ordinal)
            || lower.EndsWith(".tar.bz2", StringComparison.Ordinal) || lower.EndsWith(".tbz2", StringComparison.Ordinal)
            || lower.EndsWith(".tbz", StringComparison.Ordinal);
    }

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
    {
        fileName ??= string.Empty;
        var lower = fileName.ToLowerInvariant();
        return RawCodecFor(lower) is { } codec
            ? new RawCodecSession(container, fileName, codec)
            : new Session(container, fileName, options?.Password);
    }

    public void Write(Stream target, string fileName, IReadOnlyList<ArchiveWriteEntry> entries,
                      ArchiveWriteOptions? options = null)
    {
        var (type, compression) = WriteFormat(fileName);
        using var writer = WriterFactory.OpenWriter(target, type, new WriterOptions(compression) { LeaveStreamOpen = true });
        foreach (var e in entries)
        {
            if (e.IsDirectory || e.OpenContent is null) continue;
            using var s = e.OpenContent();
            writer.Write(e.Path.Replace('\\', '/'), s, e.Modified);
        }
    }

    private static (ArchiveType Type, CompressionType Compression) WriteFormat(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz", StringComparison.Ordinal) || lower.EndsWith(".tgz", StringComparison.Ordinal))
            return (ArchiveType.Tar, CompressionType.GZip);
        if (lower.EndsWith(".tar.bz2", StringComparison.Ordinal) || lower.EndsWith(".tbz2", StringComparison.Ordinal)
            || lower.EndsWith(".tbz", StringComparison.Ordinal))
            return (ArchiveType.Tar, CompressionType.BZip2);
        if (lower.EndsWith(".tar", StringComparison.Ordinal))
            return (ArchiveType.Tar, CompressionType.None);
        throw new System.NotSupportedException($"SharpCompress cannot create '{Path.GetExtension(fileName)}' archives.");
    }

    private sealed class Session : IArchiveSession
    {
        private readonly IArchive _archive;
        private readonly string _fallback;

        public Session(Stream container, string fileName, string? password)
        {
            _fallback = StripExtension(Path.GetFileName(fileName));
            _archive = ArchiveFactory.OpenArchive(container, new ReaderOptions { Password = password, LeaveStreamOpen = false });
            Entries = _archive.Entries
                .Select(e => new VirtualEntry(
                    KeyOf(e, _fallback),
                    e.IsDirectory,
                    Math.Max(0, e.Size),
                    e.CompressedSize > 0 ? e.CompressedSize : Math.Max(0, e.Size),
                    e.LastModifiedTime ?? DateTime.Now,
                    Crc: 0,
                    _archive.Type.ToString()))
                .Where(v => v.Name.Length > 0 && v.Name != "." && !IsTarMetadata(v.Name))
                .ToList();
        }

        public IReadOnlyList<VirtualEntry> Entries { get; }

        public bool IsEncrypted => _archive.Entries.Any(e => e.IsEncrypted);

        public Stream OpenEntry(string entryPath)
        {
            var norm = entryPath.Replace('\\', '/').Trim('/');
            var entry = _archive.Entries.FirstOrDefault(e => !e.IsDirectory &&
                            string.Equals(KeyOf(e, _fallback).TrimEnd('/'), norm, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Entry '{entryPath}' not found.");
            return entry.OpenEntryStream();   // forward-only; the VFS copies it to a seekable temp
        }

        public void Dispose() => _archive.Dispose();

        /// <summary>Hides tar PAX/GNU extended-header pseudo-entries that some writers emit alongside the
        /// real files (e.g. <c>PaxHeaders/…</c>, <c>pax_global_header</c>).</summary>
        private static bool IsTarMetadata(string name)
            => name.Contains("PaxHeader", StringComparison.OrdinalIgnoreCase)
            || name.Contains("pax_global_header", StringComparison.OrdinalIgnoreCase);

        private static string KeyOf(IArchiveEntry e, string fallback)
        {
            var key = string.IsNullOrEmpty(e.Key) ? fallback : e.Key!.Replace('\\', '/').TrimStart('/');
            return key.StartsWith("./", StringComparison.Ordinal) ? key[2..] : key;   // some tars prefix "./"
        }
    }

    /// <summary>One inner stream for a single-stream compressor ArchiveFactory can't open. The inner
    /// entry is the decompressed payload — a <c>.tar</c> for <c>.tar.bz2</c>, which the VFS nests into.</summary>
    private sealed class RawCodecSession : IArchiveSession
    {
        private readonly Stream _container;
        private readonly RawCodec _codec;

        public RawCodecSession(Stream container, string fileName, RawCodec codec)
        {
            _container = container;
            _codec = codec;
            var inner = StripExtension(Path.GetFileName(fileName));
            long compressed = container.CanSeek ? container.Length : 0;
            Entries = [new VirtualEntry(inner, false, 0, compressed, DateTime.Now, Crc: 0, codec.Label)];
        }

        public IReadOnlyList<VirtualEntry> Entries { get; }

        public Stream OpenEntry(string entryPath)
        {
            if (_container.CanSeek) _container.Position = 0;
            return _codec.Decode(_container);
        }

        public void Dispose() => _container.Dispose();
    }

    /// <summary>A single-stream compressor read without ArchiveFactory: the label its entry carries, its
    /// extensions (lower-case, compound forms included) and how to decode it. Decoding leaves the container
    /// open, since the session owns it and an entry can be opened more than once.</summary>
    private sealed record RawCodec(string Label, string[] Extensions, Func<Stream, Stream> Decode);

    /// <summary>For a single-stream compressor, the inner name is the container name minus its codec
    /// extension (compound forms like <c>.tar.gz</c> → <c>.tar</c>).</summary>
    private static string StripExtension(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        foreach (var c in Compound)
            if (lower.EndsWith(c, StringComparison.Ordinal))
                return fileName[..^c.Length] + ".tar";
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
