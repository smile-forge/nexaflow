using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.IO.Common;
using K4os.Compression.LZ4.Streams;
using ZstdSharp;

namespace Nexaflow.Features.Compressed.Modern;

/// <summary>
/// Read backend for the modern single-stream compressors Zstandard (<c>.zst</c>) and LZ4 (<c>.lz4</c>).
/// Each file decompresses to a single inner file named by stripping the codec extension; a
/// <c>.tar.zst</c> therefore surfaces its inner <c>.tar</c>, which the VFS descends into via nesting.
/// </summary>
public sealed class ModernCompressionHandler : IArchiveHandler
{
    public string Name => "Zstd/LZ4";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName)
    {
        var lower = (fileName ?? string.Empty).ToLowerInvariant();
        if (lower.EndsWith(".tar.zst", StringComparison.Ordinal) || lower.EndsWith(".tzst", StringComparison.Ordinal))
            return true;
        return Path.GetExtension(lower) is ".zst" or ".zstd" or ".lz4";
    }

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new Session(container, fileName);

    private sealed class Session : IArchiveSession
    {
        private readonly Stream _container;
        private readonly bool _isLz4;
        private readonly string _innerName;

        public Session(Stream container, string fileName)
        {
            _container = container;
            _isLz4 = fileName.EndsWith(".lz4", StringComparison.OrdinalIgnoreCase);
            _innerName = StripExtension(Path.GetFileName(fileName));
            long compressed = container.CanSeek ? container.Length : 0;
            Entries = [new VirtualEntry(_innerName, false, 0, compressed, DateTime.Now, Crc: 0, _isLz4 ? "LZ4" : "Zstd")];
        }

        public IReadOnlyList<VirtualEntry> Entries { get; }

        public Stream OpenEntry(string entryPath)
        {
            if (_container.CanSeek) _container.Position = 0;
            return _isLz4
                ? LZ4Stream.Decode(_container, leaveOpen: true)
                : new DecompressionStream(_container, leaveOpen: true);
        }

        public void Dispose() => _container.Dispose();
    }

    private static string StripExtension(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".tar.zst", StringComparison.Ordinal)) return fileName[..^4];        // drop ".zst" → ".tar"
        if (lower.EndsWith(".tzst", StringComparison.Ordinal)) return fileName[..^5] + ".tar";
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
