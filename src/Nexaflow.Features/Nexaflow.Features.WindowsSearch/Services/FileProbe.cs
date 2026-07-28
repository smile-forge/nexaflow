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

    /// <summary>
    /// This file as something a <see cref="Nexaflow.Search.SearchCondition"/> can be evaluated against,
    /// so a property constraint parsed from AQS is answered during a walk instead of being taken on
    /// trust. Only the properties a walk genuinely observes are reported; everything else is declined,
    /// which the evaluator turns into "undecidable" rather than a match.
    /// </summary>
    public Nexaflow.Search.ISearchSubject AsSearchSubject() => new Subject(this);

    private sealed class Subject(FileProbe probe) : Nexaflow.Search.ISearchSubject
    {
        public bool TryGetProperty(string property, out object? value)
        {
            // Windows names the same thing several ways (System.FileName / System.ItemNameDisplay), and
            // AQS picks whichever the schema prefers — so match on all the spellings we can answer.
            switch (property.ToLowerInvariant())
            {
                case "system.filename":
                case "system.itemname":
                case "system.itemnamedisplay":
                case "system.itempathdisplay":
                    value = probe.Name; return true;

                case "system.fileextension":
                    value = System.IO.Path.GetExtension(probe.Name); return true;

                case "system.size":
                    // A folder has no size in the sense the query means; answering 0 would make
                    // "size:<1kb" quietly true for every folder on the disk.
                    value = probe.Size;
                    return !probe.IsDirectory;

                case "system.datemodified":
                    value = probe.Modified; return true;

                case "system.itemtype":
                    value = probe.IsDirectory ? "Directory" : System.IO.Path.GetExtension(probe.Name);
                    return true;

                // Everything else — System.Kind, System.Author, System.Search.Contents — needs the
                // indexer or the file's contents. Declining is the honest answer.
                default:
                    value = null; return false;
            }
        }
    }
}
