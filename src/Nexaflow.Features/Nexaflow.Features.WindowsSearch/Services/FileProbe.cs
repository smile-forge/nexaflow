using System.IO;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// The slice of a file/folder a live filesystem walk can see — name, size, last-write.
/// Used by <see cref="ParsedQuery.Matches"/> so a query can be evaluated off-index
/// (globs, and the fallback when a location isn't in the Windows Search index).
/// </summary>
public sealed class FileProbe
{
    public string   Name        { get; }
    public long     Size        { get; }
    public DateTime Modified    { get; }
    public bool     IsDirectory { get; }

    public FileProbe(string name, long size, DateTime modified, bool isDirectory = false)
    {
        Name        = name;
        Size        = size;
        Modified    = modified;
        IsDirectory = isDirectory;
    }

    /// <summary>Reads the metadata cached on <paramref name="info"/> by the directory
    /// enumeration (no extra disk hit on Windows).</summary>
    public FileProbe(FileSystemInfo info)
    {
        Name        = info.Name;
        IsDirectory = info is DirectoryInfo;
        Size        = info is FileInfo f ? f.Length : 0L;
        Modified    = info.LastWriteTime;
    }
}
