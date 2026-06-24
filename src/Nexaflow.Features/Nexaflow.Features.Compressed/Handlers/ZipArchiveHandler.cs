using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.Handlers;

/// <summary>
/// In-box archive backend for the ZIP family — plain <c>.zip</c> plus the many zip-based document /
/// package formats (Office, OpenDocument, ebook, Android, Java, NuGet, …). Backed entirely by
/// <see cref="System.IO.Compression.ZipArchive"/>; no external dependency. Read + create + rewrite;
/// encryption is left to the SecureZip provider.
/// </summary>
public sealed class ZipArchiveHandler : IArchiveHandler
{
    private static readonly HashSet<string> Extensions = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".jar", ".war", ".ear", ".apk", ".aar", ".kmz", ".epub",
        ".odt", ".ods", ".odp", ".odg", ".docx", ".xlsx", ".pptx",
        ".nupkg", ".vsix", ".appx", ".xpi", ".whl", ".crx",
    };

    public string Name => "Zip";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.List | ArchiveCapabilities.Extract |
        ArchiveCapabilities.Create | ArchiveCapabilities.Modify;

    public bool CanHandle(string fileName) => Extensions.Contains(Path.GetExtension(fileName));

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new ZipSession(container);

    public void Write(Stream target, string fileName, IReadOnlyList<ArchiveWriteEntry> entries,
                      ArchiveWriteOptions? options = null)
    {
        var level = (options?.CompressionLevel) switch
        {
            0       => CompressionLevel.NoCompression,
            <= 3    => CompressionLevel.Fastest,
            >= 7    => CompressionLevel.SmallestSize,
            _       => CompressionLevel.Optimal,
        };

        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var e in entries)
        {
            if (e.IsDirectory || e.OpenContent is null) continue;  // zip directories are implicit
            var entry = zip.CreateEntry(NormalizeForWrite(e.Path), level);
            entry.LastWriteTime = e.Modified == default ? System.DateTimeOffset.Now : e.Modified;
            using var src = e.OpenContent();
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
    }

    private static string NormalizeForWrite(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed class ZipSession : IArchiveSession
    {
        private readonly ZipArchive _zip;
        public ZipSession(Stream container)
        {
            _zip = new ZipArchive(container, ZipArchiveMode.Read, leaveOpen: false);
            Entries = _zip.Entries.Select(ToEntry).ToList();
        }

        public IReadOnlyList<VirtualEntry> Entries { get; }

        public Stream OpenEntry(string entryPath)
        {
            var norm = entryPath.Replace('\\', '/').TrimStart('/');
            var entry = _zip.GetEntry(norm)
                ?? _zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/'), norm, System.StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Zip entry not found: {entryPath}");
            return entry.Open();   // forward-only; the VFS copies it to a seekable temp
        }

        public void Dispose() => _zip.Dispose();

        private static VirtualEntry ToEntry(ZipArchiveEntry e)
        {
            var full = e.FullName.Replace('\\', '/');
            bool isDir = full.EndsWith('/') || e.Name.Length == 0;
            string method = e.CompressedLength < e.Length ? "Deflate" : "Stored";
            return new VirtualEntry(full.TrimEnd('/'), isDir, e.Length, e.CompressedLength,
                                    e.LastWriteTime.LocalDateTime, Crc: 0, method);
        }
    }
}
