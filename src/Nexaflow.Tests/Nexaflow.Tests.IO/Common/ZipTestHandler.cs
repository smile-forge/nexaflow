using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// A read-only <c>.zip</c> backend built straight on <see cref="ZipArchive"/>.
/// <para>
/// The VFS tests need <i>a</i> container format to descend into, not the Compressed feature's
/// production handler — referencing that would put a feature assembly behind a library test and
/// undo the point of this project. BCL zip is the smallest thing that is still a real archive:
/// the mount rules it exercises (split point, backing, resolution) are format-agnostic.
/// </para>
/// </summary>
internal sealed class ZipTestHandler : IArchiveHandler
{
    public string Name => "Zip (test)";
    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName)
        => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new Session(new ZipArchive(container, ZipArchiveMode.Read, leaveOpen: false));

    private sealed class Session(ZipArchive archive) : IArchiveSession
    {
        public IReadOnlyList<VirtualEntry> Entries { get; } = archive.Entries
            .Select(e => new VirtualEntry(
                e.FullName.Replace('\\', '/'),
                IsDirectory: e.FullName.EndsWith('/'),
                e.Length,
                e.CompressedLength,
                e.LastWriteTime.DateTime,
                e.Crc32,
                "Deflate"))
            .ToList();

        public Stream OpenEntry(string entryPath)
        {
            var entry = archive.GetEntry(entryPath.Replace('\\', '/'))
                        ?? throw new FileNotFoundException($"No entry '{entryPath}' in the archive.");
            return entry.Open();
        }

        public void Dispose() => archive.Dispose();
    }
}
