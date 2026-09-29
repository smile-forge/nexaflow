using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Hdf5;
using Nexaflow.Visuals.Common.Collections;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>
/// A dataset as a table. Only the window of rows the grid shows is read — one hyperslab per window — so the table
/// costs the same over ten rows or ten billion. A newer window supersedes one still being read.
/// </summary>
public sealed partial class Hdf5TableViewModel : ObservableObject, IDisposable
{
    /// <summary>The rows read per window: a screenful and more, as the grid's own window is.</summary>
    public const int WindowSize = 150;

    private readonly Hdf5TableSource _rows;
    private CancellationTokenSource _windowCts = new();

    public Hdf5TableViewModel(IHdf5Source source, Hdf5Object dataset, Hdf5SliceViewModel slice)
    {
        Dataset = dataset;
        Slice   = slice;
        _rows   = new Hdf5TableSource(source, dataset, slice);
        slice.Changed += OnSliceChanged;
        RebuildColumns();
    }

    public Hdf5Object Dataset { get; }
    public Hdf5SliceViewModel Slice { get; }
    public ObservableCollection<VirtualizedColumn> Columns { get; } = [];
    public ObservableCollection<VirtualizedRow> Window { get; } = [];

    [ObservableProperty] private int _totalRowCount;
    [ObservableProperty] private int _focalRow;
    [ObservableProperty] private string? _problem;
    [ObservableProperty] private bool _isReading;

    /// <summary>Raised when something other than the grid moves the window, so the grid's scrollbar follows.</summary>
    public event Action<int>? ScrollRequested;

    /// <summary>Reads the window starting at <see cref="FocalRow"/>, dropping any read it supersedes.</summary>
    public async Task RefreshWindowAsync()
    {
        _windowCts.Cancel();
        _windowCts.Dispose();
        _windowCts = new CancellationTokenSource();
        var ct = _windowCts.Token;

        IsReading = true;
        try
        {
            var rows = await _rows.GetVisibleAsync(FocalRow, WindowSize, static _ => true, ct);
            if (ct.IsCancellationRequested) return;
            Window.Clear();
            foreach (var r in rows) Window.Add(new VirtualizedRow(r.AbsoluteIndex, r.Cells));
            Problem = null;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Hdf5Exception or ArgumentException or ObjectDisposedException)
        {
            if (ct.IsCancellationRequested) return;
            Window.Clear();
            Problem = ex.Message;
        }
        IsReading = false;
    }

    /// <summary>Moves the window to a row, as the AI does to show the user one.</summary>
    public Task ScrollToAsync(int row)
    {
        FocalRow = Math.Clamp(row, 0, Math.Max(0, TotalRowCount - 1));
        ScrollRequested?.Invoke(FocalRow);
        return RefreshWindowAsync();
    }

    public void Dispose()
    {
        Slice.Changed -= OnSliceChanged;
        _windowCts.Cancel();
        _windowCts.Dispose();
    }

    private void OnSliceChanged()
    {
        RebuildColumns();
        _ = RefreshWindowAsync();
    }

    private void RebuildColumns()
    {
        var view  = Slice.Snapshot();
        var width = view.Type.Class == Hdf5TypeClass.Compound || view.ColumnAxis < 0 ? 160 : 110;
        Columns.Clear();
        foreach (var name in view.ColumnNames(Dataset.Name))
            Columns.Add(new VirtualizedColumn { Header = name, Width = width });
        TotalRowCount = (int)Math.Min(view.RowExtent, int.MaxValue);
        if (FocalRow >= TotalRowCount) FocalRow = Math.Max(0, TotalRowCount - 1);
    }
}

/// <summary>
/// The table's rows as an <see cref="IRowSource"/>: each read turns a run of rows into one selection of the
/// dataset through the slice frozen at the moment of the call.
/// </summary>
internal sealed class Hdf5TableSource(IHdf5Source source, Hdf5Object dataset, Hdf5SliceViewModel slice) : IRowSource
{
    public int? KnownRowCount => (int)Math.Min(slice.Snapshot().RowExtent, int.MaxValue);

    public Task<List<HydratedRow>> GetVisibleAsync(int fromRow, int maxVisible, RowFilter filter, CancellationToken ct)
    {
        var view = slice.Snapshot();
        return Task.Run(() =>
        {
            var rows = new List<HydratedRow>(maxVisible);
            ulong next = (ulong)Math.Max(0, fromRow);
            while (rows.Count < maxVisible && next < view.RowExtent)
            {
                ct.ThrowIfCancellationRequested();
                ulong count = Math.Min((ulong)(maxVisible - rows.Count), view.RowExtent - next);
                var block = source.ReadBlock(dataset.Path, view.SelectionFor(next, count), ct);
                for (ulong r = 0; r < count && rows.Count < maxVisible; r++)
                {
                    var cells = view.Cells(block, r);
                    if (filter(cells)) rows.Add(new HydratedRow((int)Math.Min(next + r, int.MaxValue), cells));
                }
                next += count;
            }
            return rows;
        }, ct);
    }

    /// <summary>The source belongs to the view-model's lease; there is nothing here to release.</summary>
    public void Dispose() { }
}
