using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace Nexaflow.Features.Compressed.SharpCompress;

/// <summary>
/// Read-only archive backend covering the formats SharpCompress handles that the in-box zip handler
/// does not: tar, 7-Zip, RAR, and the single-stream compressors (gzip/bzip2/xz). Compressed tars
/// (<c>.tar.gz</c> etc.) surface their inner <c>.tar</c>, which the VFS then descends into via the tar
/// handler. Listing + extraction only — these formats are not rewritten in place.
/// </summary>
public sealed class SharpCompressHandler : IArchiveHandler
{
    private static readonly string[] Compound =
        [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tgz", ".tbz2", ".tbz", ".txz"];

    private static readonly string[] Simple =
        [".tar", ".7z", ".rar", ".gz", ".bz2", ".xz", ".lz", ".lzma"];

    public string Name => "SharpCompress";

    // Create covers tar / tar.gz / tar.bz2 only (Write throws for 7z/rar, which SharpCompress can't write).
    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.List | ArchiveCapabilities.Extract | ArchiveCapabilities.Create;

    public bool CanHandle(string fileName)
    {
        var lower = (fileName ?? string.Empty).ToLowerInvariant();
        if (Compound.Any(c => lower.EndsWith(c, StringComparison.Ordinal))) return true;
        return Simple.Contains(Path.GetExtension(lower));
    }

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new Session(container, fileName, options?.Password);

    public void Write(Stream target, string fileName, IReadOnlyList<ArchiveWriteEntry> entries,
                      ArchiveWriteOptions? options = null)
    {
        var (type, compression) = WriteFormat(fileName);
        using var writer = WriterFactory.Open(target, type, new WriterOptions(compression) { LeaveStreamOpen = true });
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
            _archive = ArchiveFactory.Open(container, new ReaderOptions { Password = password, LeaveStreamOpen = false });
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
