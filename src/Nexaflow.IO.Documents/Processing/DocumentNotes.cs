using System.Collections.Concurrent;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// What a whole-document job has worked out and keeps, so doing it again asks only of what changed: the gist, each section's
/// summary, each passage's translation — each on a shelf of its own, under a key made of everything it was worked out from
/// (<see cref="DocumentKeys"/>).
/// </summary>
public interface IDocumentNotes
{
    /// <summary>The note kept on <paramref name="shelf"/> under <paramref name="key"/>, or null where none is.</summary>
    Task<string?> ReadAsync(string shelf, string key, CancellationToken ct);

    /// <summary>Keeps <paramref name="text"/> on <paramref name="shelf"/> under <paramref name="key"/>.</summary>
    Task WriteAsync(string shelf, string key, string text, CancellationToken ct);
}

/// <summary>
/// Notes kept in files: a folder per document, a folder per shelf inside it, a file per note. A shelf keeps only what the
/// last run of a job asked for (<see cref="Settle"/>), so an edited document leaves nothing stale behind it, and the store is
/// held under a size (<see cref="Bound"/>), the notes of the documents worked on longest ago let go first.
/// </summary>
public sealed class DocumentNotesFolder : IDocumentNotes
{
    /// <summary>The most the store keeps, across every document.</summary>
    public const long Most = 64L * 1024 * 1024;

    private readonly string _root;
    private readonly string _folder;
    private readonly ConcurrentDictionary<(string Shelf, string Key), byte> _used = new();

    /// <param name="root">The store: the folder every document's notes are kept in.</param>
    /// <param name="document">The document these notes are about, by its full path.</param>
    public DocumentNotesFolder(string root, string document)
    {
        _root = root;
        _folder = Path.Combine(root, DocumentKeys.Of(Path.GetFullPath(document).ToUpperInvariant()));
    }

    public async Task<string?> ReadAsync(string shelf, string key, CancellationToken ct)
    {
        _used[(shelf, key)] = 0;

        var file = FileOf(shelf, key);
        if (!File.Exists(file)) return null;

        try { return await File.ReadAllTextAsync(file, ct); }
        catch (IOException) { return null; }
    }

    public async Task WriteAsync(string shelf, string key, string text, CancellationToken ct)
    {
        _used[(shelf, key)] = 0;

        var file = FileOf(shelf, key);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        // Written beside it and moved into place, so a note is never read half-written.
        var writing = file + ".writing";
        await File.WriteAllTextAsync(writing, text, ct);
        File.Move(writing, file, overwrite: true);
    }

    /// <summary>
    /// Lets go of every note on <paramref name="shelves"/> this run did not ask for — what an edit to the document made
    /// stale — marks the document as worked on now, and holds the store to its size.
    /// </summary>
    public void Settle(params string[] shelves)
    {
        foreach (var shelf in shelves)
        {
            var folder = Path.Combine(_folder, shelf);
            if (!Directory.Exists(folder)) continue;

            foreach (var file in Directory.EnumerateFiles(folder))
                if (!_used.ContainsKey((shelf, Path.GetFileName(file))))
                    TryDelete(file);
        }

        if (Directory.Exists(_folder)) Directory.SetLastWriteTimeUtc(_folder, DateTime.UtcNow);

        Bound(_root, Most);
    }

    /// <summary>Holds the store at <paramref name="root"/> under <paramref name="most"/> bytes, letting go of whole documents' notes, those worked on longest ago first.</summary>
    public static void Bound(string root, long most)
    {
        if (!Directory.Exists(root)) return;

        var documents = Directory.EnumerateDirectories(root)
            .Select(folder => (Folder: folder, Bytes: Bytes(folder), Worked: Directory.GetLastWriteTimeUtc(folder)))
            .OrderBy(document => document.Worked)
            .ToList();

        var total = documents.Sum(document => document.Bytes);

        foreach (var document in documents)
        {
            if (total <= most) return;

            try
            {
                Directory.Delete(document.Folder, recursive: true);
                total -= document.Bytes;
            }
            catch (IOException) { /* in use by a job still running: the next to settle lets it go */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static long Bytes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string FileOf(string shelf, string key) => Path.Combine(_folder, shelf, key);
}
