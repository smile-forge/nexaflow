using System.Data.OleDb;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Queries the Windows Search index via OLE DB.
/// Requires the Windows Search service to be running (enabled by default on Windows 10/11).
/// All database work runs on a Task.Run thread — OleDbConnection is not thread-safe and
/// must be created and consumed on the same thread.
/// </summary>
public static class WindowsSearchService
{
    private const string ConnectionString =
        "Provider=Search.CollatorDSO.1;Extended Properties='Application=Windows'";

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
        => Task.Run(() => Search(parsed, rootPath, maxResults, ct), ct);

    private static IReadOnlyList<SearchResultEntry> Search(
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
}
