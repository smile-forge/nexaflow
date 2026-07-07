using System.Data.OleDb;
using System.IO;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Queries the Windows Search index via OLE DB, with a live-filesystem fallback for
/// locations the index doesn't cover (e.g. a data folder on a secondary drive).
/// Requires the Windows Search service to be running (enabled by default on Windows 10/11).
/// All database work runs on a Task.Run thread — OleDbConnection is not thread-safe and
/// must be created and consumed on the same thread.
/// </summary>
public static class WindowsSearchService
{
    private const string ConnectionString =
        "Provider=Search.CollatorDSO.1;Extended Properties='Application=Windows'";

    // Recurse the tree; skip what the index also ignores and reparse points (junctions)
    // so a symlink loop can't spin the walk forever.
    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Searches the Windows Search index for <paramref name="query"/> under
    /// <paramref name="rootPath"/>. Returns up to <paramref name="maxResults"/> entries
    /// sorted by most-recently modified first.
    /// Throws <see cref="OperationCanceledException"/> if the token fires.
    /// Throws <see cref="OleDbException"/> if the Windows Search service is unavailable.
    /// </summary>
    public static Task<IReadOnlyList<SearchResultEntry>> SearchAsync(
        string query,
        string rootPath,
        CancellationToken ct,
        int maxResults = 500)
        => SearchAsync(SearchQueryParser.Parse(query), rootPath, ct, maxResults);

    public static Task<IReadOnlyList<SearchResultEntry>> SearchAsync(
        ParsedQuery parsed,
        string rootPath,
        CancellationToken ct,
        int maxResults = 500)
        => Task.Run(() => Search(parsed, rootPath, maxResults, allowWalk: true, ct), ct);

    public static async Task<IReadOnlyList<SearchResultEntry>> SearchAcrossAsync(
        ParsedQuery parsed,
        IEnumerable<string> roots,
        CancellationToken ct,
        int maxResults = 500)
    {
        // "This PC" (all drives) stays index-only: a live walk of entire drives is
        // impractical, and the index is exactly what a whole-machine search is for.
        var tasks   = roots.Select(r =>
            Task.Run(() => Search(parsed, r, maxResults, allowWalk: false, ct), ct));
        var results = await Task.WhenAll(tasks);
        return results
            .SelectMany(r => r)
            .OrderByDescending(e => e.Modified)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Routes a scoped search: filename globs go straight to a filesystem walk (they
    /// never need the index and must work on non-indexed locations); everything else
    /// hits the index and falls back to a walk when the index is empty or unavailable
    /// under a real local directory.
    /// </summary>
    private static IReadOnlyList<SearchResultEntry> Search(
        ParsedQuery parsed, string rootPath, int maxResults, bool allowWalk, CancellationToken ct)
    {
        if (allowWalk && parsed.IsGlob)
            return Walk(parsed, rootPath, maxResults, ct);

        IReadOnlyList<SearchResultEntry> indexed;
        try
        {
            indexed = SearchIndex(parsed, rootPath, maxResults, ct);
        }
        catch (OleDbException) when (allowWalk && Directory.Exists(rootPath))
        {
            return Walk(parsed, rootPath, maxResults, ct);
        }

        if (allowWalk && indexed.Count == 0 && Directory.Exists(rootPath))
            return Walk(parsed, rootPath, maxResults, ct);

        return indexed;
    }

    private static IReadOnlyList<SearchResultEntry> SearchIndex(
        ParsedQuery parsed, string rootPath, int maxResults, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scopeUri = "file:" + rootPath.Replace('\\', '/').TrimEnd('/');

        var sql = $"""
            SELECT System.ItemPathDisplay, System.FileName, System.Size,
                   System.DateModified, System.Kind
            FROM   SystemIndex
            WHERE  SCOPE='{scopeUri}'
              AND  ({parsed.WhereClause})
            ORDER BY System.DateModified DESC
            """;

        var results = new List<SearchResultEntry>();

        using var conn   = new OleDbConnection(ConnectionString);
        conn.Open();
        ct.ThrowIfCancellationRequested();

        using var cmd    = new OleDbCommand(sql, conn) { CommandTimeout = 30 };
        using var reader = cmd.ExecuteReader();

        while (reader != null && reader.Read() && results.Count < maxResults)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var fileName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var size     = reader.IsDBNull(2) ? (long?)null  : Convert.ToInt64(reader.GetValue(2));
            var modified = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
            var kindRaw  = reader.IsDBNull(4) ? null : reader.GetValue(4);
            var kind     = kindRaw is string[] arr ? string.Join(", ", arr)
                         : kindRaw?.ToString() ?? string.Empty;

            var absDir = string.IsNullOrEmpty(fullPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty;
            var relDir = absDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                ? absDir[rootPath.Length..].TrimStart('\\', '/', ' ')
                : absDir;

            results.Add(new SearchResultEntry
            {
                FilePath  = fullPath,
                FileName  = string.IsNullOrEmpty(fileName)
                            ? System.IO.Path.GetFileName(fullPath)
                            : fileName,
                Directory = relDir,
                SizeBytes = size,
                Modified  = modified,
                Kind      = kind
            });
        }

        return results;
    }

    /// <summary>
    /// Walks the filesystem under <paramref name="rootPath"/> and returns entries the
    /// query's <see cref="ParsedQuery.Matches"/> predicate accepts — index-free, so it
    /// works regardless of Windows Search index coverage. Most-recent first.
    /// </summary>
    private static IReadOnlyList<SearchResultEntry> Walk(
        ParsedQuery parsed, string rootPath, int maxResults, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<SearchResultEntry>();
        if (!Directory.Exists(rootPath)) return results;

        var rootLen = rootPath.Length;
        foreach (var info in new DirectoryInfo(rootPath).EnumerateFileSystemInfos("*", WalkOptions))
        {
            ct.ThrowIfCancellationRequested();

            FileProbe probe;
            try   { probe = new FileProbe(info); }
            catch { continue; }                       // vanished mid-walk — skip

            if (!parsed.Matches(probe)) continue;

            var absDir = System.IO.Path.GetDirectoryName(info.FullName) ?? string.Empty;
            var relDir = absDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                ? absDir[rootLen..].TrimStart('\\', '/', ' ')
                : absDir;

            results.Add(new SearchResultEntry
            {
                FilePath  = info.FullName,
                FileName  = info.Name,
                Directory = relDir,
                SizeBytes = probe.IsDirectory ? null : probe.Size,
                Modified  = probe.Modified,
                Kind      = probe.IsDirectory ? "folder" : string.Empty
            });

            if (results.Count >= maxResults) break;
        }

        results.Sort((a, b) => Nullable.Compare(b.Modified, a.Modified));
        return results;
    }
}
