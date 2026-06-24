using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nexaflow.IO.Common;

/// <summary>Top-level facts about one archive, for the Compressed inspector.</summary>
/// <param name="Format">The backend's display name (e.g. <c>"Zip"</c>).</param>
/// <param name="Entries">Every entry, flat, with full forward-slash paths.</param>
public sealed record ArchiveSummary(
    string Format,
    ArchiveCapabilities Capabilities,
    IReadOnlyList<VirtualEntry> Entries,
    string? Comment,
    bool IsEncrypted);

/// <summary>
/// A file-system facade that is transparent over archive boundaries. For a real path every call is a
/// byte-identical pass-through to <see cref="System.IO"/>; for a path that descends into an archive
/// (e.g. <c>D:\a.zip\inner\x.cs</c>, including nesting like <c>a.zip\b.tar\c.txt</c>) the entry is
/// resolved through the registered <see cref="IArchiveHandler"/>s — materialised to a cached temp file
/// so callers always get a seekable stream and an ordinary on-disk path semantics.
/// </summary>
public interface IVirtualFileSystem
{
    /// <summary>True if the path resolves to an existing real or in-archive file or directory.</summary>
    bool Exists(string path);

    /// <summary>True if the path is a directory — a real directory, an archive file (browsable like a
    /// folder), or a directory inside an archive.</summary>
    bool IsDirectory(string path);

    /// <summary>True if the path is an existing real file that a registered handler can open as a folder.</summary>
    bool IsContainer(string path);

    /// <summary>Length in bytes of the (real or in-archive) file at the path.</summary>
    long GetLength(string path);

    /// <summary>Metadata for the path, or null if it does not resolve.</summary>
    VirtualEntry? GetEntryInfo(string path);

    /// <summary>Lists the children of a real or virtual directory (or an archive's top level).</summary>
    IReadOnlyList<VirtualEntry> EnumerateEntries(string path);

    /// <summary>Opens the file for reading. The returned stream is seekable (real files directly;
    /// in-archive entries via a materialised temp copy).</summary>
    Stream OpenRead(string path);

    /// <summary>Opens the file for read-write. Real paths map to a read-write <see cref="FileStream"/>;
    /// in-archive entries materialise to a temp copy whose changes are written back to the archive when
    /// the stream is closed.</summary>
    Stream OpenReadWrite(string path);

    byte[] ReadAllBytes(string path);
    string ReadAllText(string path, Encoding? encoding = null);
    void WriteAllBytes(string path, byte[] bytes);
    void WriteAllText(string path, string contents, Encoding? encoding = null);

    /// <summary>Replaces the file's whole content. Atomic for real paths; for in-archive entries the
    /// owning archive is rewritten with the entry's bytes swapped.</summary>
    void Replace(string path, byte[] newContent);

    /// <summary>Splits a path at its <b>outermost</b> archive boundary: returns the real container file
    /// and the remainder inside it, or <c>(path, null)</c> when the path is not inside any archive. Used
    /// by the shell file-watcher to watch the real container instead of a non-existent inner path.</summary>
    (string RealContainer, string? Inner) SplitOutermostContainer(string path);

    /// <summary>Top-level facts about the archive at <paramref name="containerPath"/> (a real archive
    /// file), or null if it is not a recognised container. Used by the Compressed inspector.</summary>
    ArchiveSummary? DescribeArchive(string containerPath);

    /// <summary>Extracts every file in the archive into <paramref name="destinationDir"/>, recreating its
    /// folder structure. Entries whose path would escape the destination (zip-slip) are skipped.</summary>
    void ExtractAll(string containerPath, string destinationDir);

    /// <summary>Adds files into the archive, rewriting it. Each item is a source file on disk and the
    /// entry path it should take inside the archive; an existing entry of that path is replaced.</summary>
    void AddFiles(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files);

    /// <summary>Registers a compression backend. Called once per handler at startup.</summary>
    void RegisterHandler(IArchiveHandler handler);
}
