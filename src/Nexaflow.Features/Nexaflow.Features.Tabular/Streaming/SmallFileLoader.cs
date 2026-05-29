using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.Streaming;

/// <summary>
/// Loads an entire file (< 100 KB) into memory and serves windowed views from
/// the in-memory list. The list also backs single-column sorting in Small mode.
/// </summary>
public sealed class SmallFileLoader : IRowSource
{
    private readonly List<string[]> _rows;
    private readonly DataShape      _shape;
    public int? KnownRowCount => _rows.Count;
    public IReadOnlyList<string[]> RawRows => _rows;

    public SmallFileLoader(string filePath, Encoding encoding, int bomBytes, DataShape shape)
    {
        _shape = shape;
        _rows  = new List<string[]>(1024);

        using var fs = File.OpenRead(filePath);
        fs.Position  = bomBytes;
        using var sr = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: false);
        int  commentsLeft = shape.LeadingCommentCount;
        bool firstSkipped = false;

        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            if (commentsLeft > 0) { commentsLeft--; continue; }
            if (!firstSkipped && shape.HasHeader) { firstSkipped = true; continue; }
            string[] cells = TokenizeRow(line, shape.RawShape);
            _rows.Add(cells);
        }
    }

    /// <summary>Re-orders the in-memory rows by a typed comparer. Small-mode only.</summary>
    public void Sort(System.Comparison<string[]> comparison) => _rows.Sort(comparison);

    /// <summary>
    /// Sorts by a single hydrated column. Hydrates each raw row once to extract its sort
    /// key, then sorts an index permutation — so the actual rearrangement is O(N log N)
    /// comparisons on string keys instead of re-hydrating per comparison.
    /// </summary>
    public void SortByHydratedColumn(int hydratedColumnIndex, System.Comparison<string> keyComparer)
    {
        var keys = new string[_rows.Count];
        for (int i = 0; i < _rows.Count; i++)
        {
            var hyd = _shape.HydrateRow(_rows[i]);
            keys[i] = hydratedColumnIndex < hyd.Length ? hyd[hydratedColumnIndex] : string.Empty;
        }
        var perm = new int[_rows.Count];
        for (int i = 0; i < perm.Length; i++) perm[i] = i;
        System.Array.Sort(perm, (ia, ib) => keyComparer(keys[ia], keys[ib]));
        var newRows = new List<string[]>(_rows.Count);
        foreach (var p in perm) newRows.Add(_rows[p]);
        _rows.Clear();
        _rows.AddRange(newRows);
    }

    public Task<List<HydratedRow>> GetVisibleAsync(
        int fromRow, int maxVisible, RowFilter filter, CancellationToken ct)
    {
        var result = new List<HydratedRow>(maxVisible);
        for (int i = System.Math.Max(0, fromRow); i < _rows.Count && result.Count < maxVisible; i++)
        {
            if ((i & 0x1F) == 0 && ct.IsCancellationRequested) break;
            var hydrated = Hydrate(_rows[i]);
            if (filter(hydrated)) result.Add(new HydratedRow(i, hydrated));
        }
        return Task.FromResult(result);
    }

    public void Dispose() { /* nothing to release */ }

    private string[] Hydrate(string[] raw) => _shape.HydrateRow(raw);

    public static string[] TokenizeRow(string line, CsvShape shape)
    {
        if (shape.FixedWidthBoundaries is { Count: > 0 } b)
        {
            var arr = new string[b.Count];
            for (int c = 0; c < b.Count; c++)
            {
                int start = b[c];
                int end   = c + 1 < b.Count ? b[c + 1] : line.Length;
                if (start >= line.Length) { arr[c] = string.Empty; continue; }
                if (end > line.Length) end = line.Length;
                arr[c] = line[start..end].Trim();
            }
            return arr;
        }
        if (shape.Separator is char sep)
            return CsvTokenizer.Split(line, sep, shape.Quote);
        return new[] { line };
    }
}
