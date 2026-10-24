namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One file or folder as a location lists it. It carries what a remote file system reports and
/// <see cref="VirtualEntry"/> has no room for: POSIX mode, owner and group, a symlink target, the hidden and read-only
/// flags, and a content hash when the server offers one. <see cref="VirtualEntry"/> stays the archive model; an
/// archive entry listed through the async VFS carries its archive facts in <see cref="Archive"/>.
/// <para>
/// From an <see cref="ILocationSession"/>, <see cref="Name"/> is the name exactly as the server holds it. Through
/// <see cref="IVirtualFileSystem"/> it is the <b>path segment</b> — the name escaped by <see cref="LocationPath"/> so
/// that it combines into a path with <c>\</c> — and <see cref="IVirtualFileSystem.GetDisplayName"/> gives it back as
/// people read it. For a name with nothing to escape the two are the same.
/// </para>
/// </summary>
/// <param name="Modified">Last write time, local, as <see cref="VirtualEntry.Modified"/> is.</param>
public sealed record LocationEntry(string Name, bool IsDirectory, long Size, System.DateTime Modified)
{
    public bool IsHidden { get; init; }

    public bool IsReadOnly { get; init; }

    /// <summary>Where a symbolic link points, or null when the entry is not one.</summary>
    public string? SymlinkTarget { get; init; }

    /// <summary>POSIX permission bits, or null where the location has none (<see cref="LocationCapabilities.Permissions"/>).</summary>
    public System.IO.UnixFileMode? Mode { get; init; }

    public string? Owner { get; init; }

    public string? Group { get; init; }

    /// <summary>A hash of the content in the server's own notation (<c>sha256:…</c>), or null when it offers none.</summary>
    public string? ContentHash { get; init; }

    /// <summary>The archive facts of an entry inside an archive, or null for any other entry.</summary>
    public ArchiveEntryDetail? Archive { get; init; }

    public bool IsSymlink => SymlinkTarget is not null;

    /// <summary>The entry for a real file or folder. Reads only what the enumeration that produced
    /// <paramref name="info"/> already holds, except a reparse point's link target.</summary>
    public static LocationEntry FromFileSystemInfo(System.IO.FileSystemInfo info)
    {
        var attributes = info.Attributes;
        bool isDirectory = (attributes & System.IO.FileAttributes.Directory) != 0;
        return new LocationEntry(info.Name, isDirectory, isDirectory ? 0 : ((System.IO.FileInfo)info).Length, info.LastWriteTime)
        {
            IsHidden      = (attributes & System.IO.FileAttributes.Hidden) != 0,
            IsReadOnly    = (attributes & System.IO.FileAttributes.ReadOnly) != 0,
            SymlinkTarget = (attributes & System.IO.FileAttributes.ReparsePoint) != 0 ? LinkTargetOf(info) : null,
        };
    }

    /// <summary>The entry for a <see cref="VirtualEntry"/> inside an archive, its archive facts kept.</summary>
    public static LocationEntry FromArchive(VirtualEntry entry)
        => new(entry.Name, entry.IsDirectory, entry.Size, entry.Modified)
        {
            Archive = new ArchiveEntryDetail(entry.CompressedSize, entry.Crc, entry.Method),
        };

    // A reparse point that is not a link (a cloud placeholder, a dedup stub) has no target; one the process may not
    // read is reported as not a link rather than failing the whole listing.
    private static string? LinkTargetOf(System.IO.FileSystemInfo info)
    {
        try { return info.LinkTarget; }
        catch (System.IO.IOException) { return null; }
        catch (System.UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// True when <paramref name="other"/> describes the same content of the same file: the size and modified time
    /// agree, and so do the hashes when both carry one. A write that names the entry it read refuses to commit over
    /// a file that no longer matches (<see cref="LocationError.Conflict"/>).
    /// </summary>
    public bool IsSameVersion(LocationEntry other)
        => IsDirectory == other.IsDirectory
           && Size == other.Size
           && Modified == other.Modified
           && (ContentHash is null || other.ContentHash is null
               || string.Equals(ContentHash, other.ContentHash, System.StringComparison.OrdinalIgnoreCase));
}

/// <summary>The facts only an archive entry has.</summary>
/// <param name="CompressedSize">Bytes the entry takes inside the archive; equal to the size when stored.</param>
/// <param name="Crc">The entry's CRC-32, or 0 when the format keeps none.</param>
/// <param name="Method">The compression method's display name, or null when unknown.</param>
public sealed record ArchiveEntryDetail(long CompressedSize, uint Crc, string? Method);
