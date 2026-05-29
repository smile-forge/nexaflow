using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Tabular.Streaming;

public sealed record HydratedRow(int AbsoluteIndex, string[] Cells);

public delegate bool RowFilter(string[] cells);

/// <summary>
/// Common interface for the small (whole-file) and large (sliding window) loaders.
/// Both produce filter-aware windows so the view always sees up to N visible rows.
/// </summary>
public interface IRowSource : System.IDisposable
{
    /// <summary>
    /// Best-known row count. Null until the background <see cref="RowCounter"/> finishes
    /// for large files; equals the actual count for small-file loaders.
    /// </summary>
    int? KnownRowCount { get; }

    /// <summary>
    /// Returns up to <paramref name="maxVisible"/> rows that pass <paramref name="filter"/>
    /// starting at or after <paramref name="fromRow"/>. Stops at EOF.
    /// </summary>
    Task<List<HydratedRow>> GetVisibleAsync(
        int fromRow, int maxVisible, RowFilter filter, CancellationToken ct);
}
