using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.Handlers;

/// <summary>
/// Read backend for Brotli single-stream files (<c>.br</c>), via in-box <see cref="BrotliStream"/>.
/// A <c>.tar.br</c> surfaces its inner <c>.tar</c>, which the VFS descends into via nesting — the same
/// shape the Modern provider uses for <c>.tar.zst</c>.
/// </summary>
public sealed class BrotliArchiveHandler : IArchiveHandler
{
    public string Name => "Brotli";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName)
    {
        var lower = (fileName ?? string.Empty).ToLowerInvariant();
        if (lower.EndsWith(".tar.br", StringComparison.Ordinal)) return true;
        return Path.GetExtension(lower) == ".br";
    }

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new Session(container, fileName);

    private sealed class Session : IArchiveSession
    {
        private readonly Stream _container;

        public Session(Stream container, string fileName)
        {
            _container = container;
            var inner = StripExtension(Path.GetFileName(fileName));
            long compressed = container.CanSeek ? container.Length : 0;
            Entries = [new VirtualEntry(inner, false, 0, compressed, DateTime.Now, Crc: 0, "Brotli")];
        }

        public IReadOnlyList<VirtualEntry> Entries { get; }

        public Stream OpenEntry(string entryPath)
        {
            if (_container.CanSeek) _container.Position = 0;
            return new BrotliStream(_container, CompressionMode.Decompress, leaveOpen: true);
        }

        public void Dispose() => _container.Dispose();
    }

    private static string StripExtension(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".tar.br", StringComparison.Ordinal)) return fileName[..^3];   // ".tar.br" → ".tar"
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
