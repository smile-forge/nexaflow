using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Visuals.Common.Collections;

public sealed record HydratedRow(int AbsoluteIndex, string[] Cells);

public delegate bool RowFilter(string[] cells);

/// <summary>
/// A windowed source of rows for <see cref="Nexaflow.Visuals.Common.Controls.VirtualizedRowsControl"/>.
/// Every read returns a filter-aware window, so the view holds at most N visible rows however large
/// the source is.
/// </summary>
public interface IRowSource : System.IDisposable
{
    /// <summary>
    /// Best-known row count. Null while the source is still counting; the exact count once known.
    /// </summary>
    int? KnownRowCount { get; }

    /// <summary>
    /// Returns up to <paramref name="maxVisible"/> rows that pass <paramref name="filter"/>
    /// starting at or after <paramref name="fromRow"/>. Stops at the end of the source.
    /// </summary>
    Task<List<HydratedRow>> GetVisibleAsync(
        int fromRow, int maxVisible, RowFilter filter, CancellationToken ct);
}
