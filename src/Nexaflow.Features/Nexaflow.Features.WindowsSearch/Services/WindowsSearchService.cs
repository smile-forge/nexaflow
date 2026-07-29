using System.Data.OleDb;
using System.IO;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>Where a result set actually came from — the two cover different files and behave differently,
/// so anything reporting counts to the user has to name which one produced them.</summary>
public enum SearchOrigin
{
    /// <summary>The Windows Search index. Covers indexed locations and can match file contents.</summary>
    Index,

    /// <summary>A live walk of the folder tree, started by the user when the index had no answer. Sees
    /// every file and reads their contents, at the cost of being slow.</summary>
    FolderScan,

    /// <summary>The index couldn't be reached at all — a different claim from "it found nothing", and the
    /// difference decides what the banner should offer.</summary>
    IndexUnavailable,
}

/// <param name="Entries">The rows found.</param>
/// <param name="Origin">Which mechanism produced them.</param>
public sealed record SearchResults(IReadOnlyList<SearchResultEntry> Entries, SearchOrigin Origin);

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
    /// Searches the Windows Search index for <paramref name="parsed"/> under
    /// <paramref name="rootPath"/>. Returns up to <paramref name="maxResults"/> entries
    /// sorted by most-recently modified first.
    /// Throws <see cref="OperationCanceledException"/> if the token fires.
    /// Throws <see cref="OleDbException"/> if the Windows Search service is unavailable.
    /// <para>
    /// Takes a parsed query, never a raw string: a query has to be parsed once, into terms, so the same
    /// parse can drive both the index and the folder-walk fallback below.
    /// </para>
    /// </summary>
    public static Task<IReadOnlyList<SearchResultEntry>> SearchAsync(
        ParsedQuery parsed,
        string rootPath,
        CancellationToken ct,
        int maxResults = 500)
        => Task.Run(() => Search(parsed, rootPath, maxResults, allowWalk: true, ct).Entries, ct);

    /// <summary>
    /// As <see cref="SearchAsync(ParsedQuery,string,CancellationToken,int)"/>, but also reports whether the
    /// rows came from the index or from a live folder scan. The two can return very different sets, so a UI
    /// telling the user how many files were returned has to say which produced them.
    /// </summary>
    public static Task<SearchResults> SearchWithOriginAsync(
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
        => (await SearchAcrossWithOriginAsync(parsed, roots, ct, maxResults)).Entries;

    /// <summary>
    /// Every drive at once, reporting where the rows came from. The origin matters even here: if the
    /// indexer is down, every drive comes back empty, and "no results" would be indistinguishable from a
    /// genuine miss across the whole machine.
    /// </summary>
    public static async Task<SearchResults> SearchAcrossWithOriginAsync(
        ParsedQuery parsed,
        IEnumerable<string> roots,
        CancellationToken ct,
        int maxResults = 500)
    {
        var tasks   = roots.Select(r =>
            Task.Run(() => Search(parsed, r, maxResults, allowWalk: true, ct), ct));
        var results = await Task.WhenAll(tasks);

        var entries = results
            .SelectMany(r => r.Entries)
            .OrderByDescending(e => e.Modified)
            .Take(maxResults)
            .ToList();

        // Unavailable only when NO drive could be asked — one working drive means the search really did
        // run, and reporting it as broken would be worse than saying nothing.
        var origin = results.Length > 0 && results.All(r => r.Origin == SearchOrigin.IndexUnavailable)
            ? SearchOrigin.IndexUnavailable
            : SearchOrigin.Index;

        return new SearchResults(entries, origin);
    }

    /// <summary>
    /// Runs a scoped search against the index. Everything goes to the index, globs included — it is an
    /// index, and enumerating a directory tree to answer <c>*.txt</c> is slower than asking it.
    /// <para>
    /// A folder scan is never started from here. It reads every file in the tree and can take minutes, so
    /// it is offered to the user rather than entered silently off the back of a keystroke — see
    /// <see cref="WalkAsync"/> and the banner that invites it.
    /// </para>
    /// </summary>
    private static SearchResults Search(
        ParsedQuery parsed, string rootPath, int maxResults, bool allowWalk, CancellationToken ct)
    {
        try
        {
            return new(SearchIndex(parsed, rootPath, maxResults, ct), SearchOrigin.Index);
        }
        catch (OleDbException)
        {
            // The indexer is unavailable, not empty. Reported rather than thrown, because an exception
            // here dead-ends at a "service unavailable" message — while the folder scan can still answer
            // the question, and is exactly what the caller should offer.
            return new([], SearchOrigin.IndexUnavailable);
        }
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
    /// Walks the tree under <paramref name="rootPath"/>, reading files as it goes, and reports each match
    /// through <paramref name="onMatch"/> the moment it is found.
    /// <para>
    /// Streaming is not a nicety here. This reads every candidate file in a directory tree and can run for
    /// minutes; returning a list at the end would leave the user staring at nothing while it did. Each hit
    /// arrives settled — the name and the contents have both been judged — so unlike the index path there
    /// is no second verification pass to wait for.
    /// </para>
    /// <para>
    /// Name-side terms are applied first, and a file ruled out by its name is never opened. That is what
    /// makes a glob or a property constraint worth typing: <c>*.txt urgent</c> reads the .txt files, not
    /// the whole tree.
    /// </para>
    /// </summary>
    public static Task<int> WalkAsync(
        SearchRequest request,
        string rootPath,
        int maxResults,
        Action<SearchResultEntry> onMatch,
        CancellationToken ct)
        => Task.Run(() => Walk(request, rootPath, maxResults, onMatch, ct), ct);

    /// <summary>Cap on how much of any one file the scan will read, matching the verifier's own limit so
    /// the two paths agree about what "found in this file" means.</summary>
    private const long WalkReadCap = 4L * 1024 * 1024;

    private static async Task<int> Walk(
        SearchRequest request,
        string rootPath,
        int maxResults,
        Action<SearchResultEntry> onMatch,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(rootPath)) return 0;

        var extractor = new PlainTextExtractor();

        var found   = 0;
        var rootLen = rootPath.Length;

        foreach (var info in new DirectoryInfo(rootPath).EnumerateFileSystemInfos("*", WalkOptions))
        {
            ct.ThrowIfCancellationRequested();

            FileProbe probe;
            try   { probe = new FileProbe(info); }
            catch { continue; }                       // vanished mid-walk — skip

            if (!await Accepts(request, probe, info, extractor, ct)) continue;

            var absDir = System.IO.Path.GetDirectoryName(info.FullName) ?? string.Empty;
            var relDir = absDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                ? absDir[rootLen..].TrimStart('\\', '/', ' ')
                : absDir;

            onMatch(new SearchResultEntry
            {
                FilePath  = info.FullName,
                FileName  = info.Name,
                Directory = relDir,
                SizeBytes = probe.IsDirectory ? null : probe.Size,
                Modified  = probe.Modified,
                Kind      = probe.IsDirectory ? "folder" : string.Empty,
                State     = SearchHitState.Verified,   // decided here and now, not a candidate
            });

            if (++found >= maxResults) break;
        }

        return found;
    }

    /// <summary>
    /// Whether this file satisfies the whole query, opening it only when the name can't settle the matter.
    /// </summary>
    private static async Task<bool> Accepts(
        SearchRequest request, FileProbe probe, FileSystemInfo info,
        PlainTextExtractor extractor, CancellationToken ct)
    {
        var subject = probe.AsSearchSubject();

        // A term the name (or a property) already answers costs nothing. One it definitively fails ends
        // the question — no point reading a file that a glob has ruled out.
        var undecided = new List<SearchTerm>();
        foreach (var term in request.Terms)
        {
            switch (term.Evaluate(subject, probe.Name))
            {
                case true:  continue;
                case false when term.NameOnly || term.Kind == SearchTermKind.Structured:
                    return false;                     // nothing inside the file can rescue this
                default:
                    undecided.Add(term);
                    break;
            }
        }

        if (undecided.Count == 0) return true;

        // Folders have no contents to search, so a term the name didn't satisfy stays unsatisfied.
        if (probe.IsDirectory || info is not FileInfo file) return false;

        var extracted = await extractor.ExtractAsync(file.FullName, WalkReadCap, ct);
        if (extracted is null) return false;          // unreadable: not a match we can claim

        return undecided.All(t => t.Matches(extracted.Text, isName: false));
    }
}
