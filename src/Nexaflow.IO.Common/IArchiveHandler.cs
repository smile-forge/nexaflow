using System.Collections.Generic;
using System.IO;

namespace Nexaflow.IO.Common;

/// <summary>What a given <see cref="IArchiveHandler"/> can do with its formats.</summary>
[System.Flags]
public enum ArchiveCapabilities
{
    None    = 0,
    List    = 1 << 0,   // enumerate entries
    Extract = 1 << 1,   // open an entry for read
    Create  = 1 << 2,   // write a brand-new archive
    Modify  = 1 << 3,   // rewrite an archive with an entry added/removed/replaced
    Encrypt = 1 << 4,   // whole-archive password (AES) on write
}

/// <summary>Options for opening an existing archive.</summary>
public sealed class ArchiveOpenOptions
{
    public string? Password { get; init; }
    public static ArchiveOpenOptions Default { get; } = new();
}

/// <summary>Options for writing/creating an archive.</summary>
public sealed class ArchiveWriteOptions
{
    /// <summary>Advisory compression level, 0 (store) .. 9 (max). Null = handler default.</summary>
    public int? CompressionLevel { get; init; }
    /// <summary>When set, the archive is written with whole-archive AES encryption.</summary>
    public string? Password { get; init; }
    public string? Comment { get; init; }
    public static ArchiveWriteOptions Default { get; } = new();
}

/// <summary>One entry to write when (re)building an archive. Content is provided lazily so a rebuild
/// never has to hold every entry in memory at once.</summary>
public sealed class ArchiveWriteEntry
{
    /// <summary>In-archive path, forward-slash separated (e.g. <c>"docs/readme.md"</c>).</summary>
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public System.DateTime Modified { get; init; } = System.DateTime.Now;
    /// <summary>Opens the entry's content for reading. Null for directories.</summary>
    public System.Func<Stream>? OpenContent { get; init; }

    /// <summary>Uncompressed byte length when known, so a write can report determinate progress.
    /// 0 means unknown, which reports as an item count rather than a byte total.</summary>
    public long Length { get; init; }
}

/// <summary>A read session over one opened archive. Disposing releases the underlying container stream.</summary>
public interface IArchiveSession : System.IDisposable
{
    /// <summary>Every entry in the archive as a flat list, paths forward-slash separated.</summary>
    IReadOnlyList<VirtualEntry> Entries { get; }

    /// <summary>Optional archive-level comment, or null.</summary>
    string? Comment => null;

    /// <summary>Whether the archive is encrypted (entries require the password to extract).</summary>
    bool IsEncrypted => false;

    /// <summary>Opens one entry for reading by its full in-archive path (forward-slash separated).
    /// The returned stream is owned by the caller. Throws if the entry is missing or a directory.</summary>
    Stream OpenEntry(string entryPath);

    /// <summary>
    /// True when this session answers directory listings and per-path stats <b>lazily</b> — via
    /// <see cref="ListChildren"/>/<see cref="StatEntry"/> — instead of the full <see cref="Entries"/>
    /// list. Set by containers whose full recursive enumeration is expensive (e.g. a disk image's
    /// filesystem): the VFS then browses one directory at a time and never materialises
    /// <see cref="Entries"/> for navigation. Ordinary archives leave this <c>false</c> and are unaffected.
    /// </summary>
    bool SupportsLazyBrowse => false;

    /// <summary>Direct children of one in-container directory (<c>""</c> = the container root),
    /// forward-slash separated. Only consulted when <see cref="SupportsLazyBrowse"/> is true; each call
    /// reads a single directory. The default returns <see cref="Entries"/> (never reached for eager
    /// sessions, which the VFS filters itself).</summary>
    IReadOnlyList<VirtualEntry> ListChildren(string dirPath) => Entries;

    /// <summary>Metadata for one in-container path (forward-slash separated), or null if it does not
    /// exist. Only consulted when <see cref="SupportsLazyBrowse"/> is true.</summary>
    VirtualEntry? StatEntry(string entryPath) => null;
}

/// <summary>
/// An extensible compression-module: a backend for one family of archive formats. Any assembly can
/// implement this; Core discovers implementations by reflection at startup and registers them into
/// <see cref="VirtualFileSystem.Instance"/>. Stateless — one instance serves every archive of its formats.
/// </summary>
public interface IArchiveHandler
{
    /// <summary>Short backend name shown in the UI (e.g. <c>"Zip"</c>, <c>"7-Zip"</c>).</summary>
    string Name { get; }

    ArchiveCapabilities Capabilities { get; }

    /// <summary>True when this handler recognises <paramref name="fileName"/> as one of its formats.
    /// Implementations must be compound-extension aware (e.g. <c>.tar.gz</c>, not just <c>.gz</c>).</summary>
    bool CanHandle(string fileName);

    /// <summary>Opens <paramref name="container"/> (a seekable stream of the archive bytes) for reading.
    /// The session owns the stream and disposes it. <paramref name="fileName"/> disambiguates formats a
    /// single handler serves (e.g. tar vs tar.gz).</summary>
    IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null);

    /// <summary>Whether this handler can <b>write</b> the specific archive <paramref name="fileName"/>.
    /// Defaults to the <see cref="ArchiveCapabilities.Create"/> capability; override when a handler reads
    /// more formats than it writes (SharpCompress reads 7z/rar/xz but writes only the tar family).</summary>
    bool CanWrite(string fileName) => Capabilities.HasFlag(ArchiveCapabilities.Create);

    /// <summary>Writes a new archive of this handler's format to <paramref name="target"/> from
    /// <paramref name="entries"/>. Only meaningful when <see cref="Capabilities"/> includes
    /// <see cref="ArchiveCapabilities.Create"/>/<see cref="ArchiveCapabilities.Modify"/>.</summary>
    void Write(Stream target, string fileName, IReadOnlyList<ArchiveWriteEntry> entries,
               ArchiveWriteOptions? options = null)
        => throw new System.NotSupportedException($"{Name} cannot write archives.");
}
