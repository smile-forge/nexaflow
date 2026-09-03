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
/// <para>
/// It is also transparent over <b>mounts</b>: a path under <c>::{mountId}</c> maps by string
/// substitution onto a real directory (see <see cref="RegisterMount"/>), so a mounted location browses
/// and reads exactly like the directory behind it without ever naming it. Because the mapping happens
/// before every other rule, a mounted path may itself descend into an archive
/// (<c>::docs\a.zip\x.cs</c>) and behaves identically to the real equivalent.
/// </para>
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

    /// <summary>True if a registered handler recognises <paramref name="fileName"/> by name alone (no
    /// existence check) — used to spot a nested archive entry inside another archive.</summary>
    bool IsContainerName(string fileName);

    /// <summary>Length in bytes of the (real or in-archive) file at the path.</summary>
    long GetLength(string path);

    /// <summary>Metadata for the path, or null if it does not resolve.</summary>
    VirtualEntry? GetEntryInfo(string path);

    /// <summary>Lists the children of a real or virtual directory (or an archive's top level).</summary>
    IReadOnlyList<VirtualEntry> EnumerateEntries(string path);

    /// <summary>Opens the file for reading. The returned stream is seekable (real files directly;
    /// in-archive entries via a materialised temp copy).</summary>
    Stream OpenRead(string path);

    /// <summary>As <see cref="OpenRead(string)"/>, with the <see cref="FileStream"/> tuning a reader that
    /// scans a huge file off the UI thread needs — a large buffer and overlapped IO. Read-only, so the
    /// materialised temp of an in-archive entry can never be written to behind the archive's back; writers
    /// go through <see cref="OpenReadWrite"/> or <see cref="Replace"/>.</summary>
    Stream OpenRead(string path, int bufferSize, bool useAsync);

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

    /// <summary>Creates a new archive at <paramref name="archivePath"/> from the contents of
    /// <paramref name="sourceDir"/> (recursively). The format is chosen by the archive file's extension;
    /// throws if no registered handler can create it.</summary>
    void CreateArchive(string archivePath, string sourceDir);

    /// <summary>Creates a new archive at <paramref name="archivePath"/> holding every item in
    /// <paramref name="sourcePaths"/> — the several items of a multi-selection, in one archive. Each source
    /// keeps its own name: a file is stored at the root under its file name, a directory recursively beneath
    /// a folder of that name, so same-named entries from two different folders cannot collide. The format is
    /// chosen by the archive file's extension; throws if no registered handler can create it.</summary>
    void CreateArchive(string archivePath, IReadOnlyList<string> sourcePaths);

    /// <summary>True if a registered handler can <b>create</b> an archive of the given file name's format.</summary>
    bool CanCreate(string archiveFileName);

    /// <summary>Rebuilds the archive's contents into <paramref name="targetPath"/> — a different
    /// extension to convert formats, or the same path with new <paramref name="options"/> (e.g. a
    /// compression level) to recompress.</summary>
    void Repackage(string sourcePath, string targetPath, ArchiveWriteOptions? options = null);

    /// <summary>Adds files into the archive, rewriting it. Each item is a source file on disk and the
    /// entry path it should take inside the archive; an existing entry of that path is replaced.</summary>
    void AddFiles(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files);

    /// <summary>Registers a compression backend. Called once per handler at startup.</summary>
    void RegisterHandler(IArchiveHandler handler);

    // ── Mounts ───────────────────────────────────────────────────────────────

    /// <summary>Every registered mount, innermost (longest <see cref="VirtualMount.RealRoot"/>) first.</summary>
    IReadOnlyList<VirtualMount> Mounts { get; }

    /// <summary>Adds the mount, replacing any with the same id. Idempotent: re-registering an identical
    /// mount changes nothing and raises no event, so a caller may refresh unconditionally.
    /// Throws <see cref="System.ArgumentException"/> if the id is empty or not path-segment-safe.</summary>
    void RegisterMount(VirtualMount mount);

    /// <summary>Removes the mount with this id, if present. Cheap — a pass-through mount owns no
    /// materialised temp files and no open archive sessions.</summary>
    void UnregisterMount(string id);

    /// <summary>Raised when the mount set changes, so open browsers can re-query. May fire on any thread.</summary>
    event System.Action? MountsChanged;

    /// <summary>How this path's bytes are obtained — the safety classification an action consults
    /// before handing the path to an external process.</summary>
    VirtualBacking GetBacking(string path);

    /// <summary>The real on-disk path for a real or mounted path, or null when the path only exists
    /// inside an archive (use <see cref="MaterializeFile"/> to get a temp copy of those) or names a
    /// mount that is not registered.</summary>
    string? TryResolveReal(string path);

    /// <summary>The inverse of mount resolution: the <c>::{id}\…</c> form of a real path that lies under
    /// a mount, or null when it does not. Used to keep a real path from surfacing once the user is
    /// browsing through a mount.</summary>
    string? TryToVirtual(string realPath);

    /// <summary>The containing directory, or null at a mount root or a drive root (i.e. "the level above
    /// is This PC"). Use instead of <see cref="Directory.GetParent"/>, which cannot see mounts.</summary>
    string? GetParentPath(string path);

    /// <summary>The leaf name to show for this path — a mount root yields the mount's friendly
    /// <see cref="VirtualMount.Label"/> rather than its id.</summary>
    string GetDisplayName(string path);

    /// <summary>The breadcrumb trail for the path, root-first, as (label, navigable path) pairs. The
    /// first pair is the mount root or the drive root; no "This PC" crumb is included — that belongs to
    /// the caller, which alone knows whether it is browsing rooted or from This PC.</summary>
    IReadOnlyList<(string Label, string Path)> GetBreadcrumbs(string path);
}
