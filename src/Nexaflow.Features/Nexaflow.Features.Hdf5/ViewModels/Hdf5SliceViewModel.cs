using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Hdf5;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>A dimension offered as the table's rows or columns.</summary>
public sealed record Hdf5AxisOption(int Dimension, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A dimension shown neither as rows nor as columns, held at one index.</summary>
public sealed partial class Hdf5FixedIndex(int dimension, ulong extent) : ObservableObject
{
    public int Dimension { get; } = dimension;
    public ulong Extent { get; } = extent;
    public double Maximum => Extent == 0 ? 0 : Extent - 1;
    public string Label => Str.Format("Hdf5.Slice.DimensionFormat", Dimension);
    public string AutomationId => $"Hdf5_SliceIndex{Dimension}";

    [ObservableProperty] private double _index;
}

/// <summary>
/// How an N-D dataset lies on a 2-D table: which dimension runs down the rows, which across the columns, the
/// index every other dimension is held at, and — for data wider than a table can hold — which window of columns
/// is shown. A compound dataset's columns are its members, so only its rows are chosen.
/// </summary>
public sealed partial class Hdf5SliceViewModel : ObservableObject
{
    /// <summary>The most columns laid out at once; wider data is paged through a column window.</summary>
    public const ulong ColumnWindow = 256;

    private readonly Hdf5DatasetInfo _info;
    private bool _rebuilding;

    public Hdf5SliceViewModel(Hdf5DatasetInfo info)
    {
        _info = info;
        int rank = info.Space.Kind == Hdf5SpaceKind.Simple ? info.Space.Rank : 0;
        IsCompound = info.Type.Class == Hdf5TypeClass.Compound;
        Axes = [.. Enumerable.Range(0, rank).Select(d => new Hdf5AxisOption(d,
            Str.Format("Hdf5.Slice.AxisFormat", d, info.Space.Shape[d].ToString("N0", CultureInfo.CurrentCulture))))];

        _rebuilding = true;
        if (rank >= 1) RowAxis = Axes[IsCompound || rank == 1 ? 0 : rank - 2];
        if (rank >= 2 && !IsCompound) ColumnAxis = Axes[rank - 1];
        _rebuilding = false;
        RebuildFixed();
    }

    public IReadOnlyList<Hdf5AxisOption> Axes { get; }
    public bool IsCompound { get; }

    /// <summary>There is a choice of rows (and, unless compound, of columns) only from two dimensions up.</summary>
    public bool CanChooseAxes => Axes.Count >= 2;
    public bool CanChooseColumns => CanChooseAxes && !IsCompound;

    public ObservableCollection<Hdf5FixedIndex> FixedIndices { get; } = [];

    /// <summary>Whether the slice bar has anything to offer.</summary>
    public bool HasSlice => CanChooseAxes || FixedIndices.Count > 0 || HasColumnWindow;

    [ObservableProperty] private Hdf5AxisOption? _rowAxis;
    [ObservableProperty] private Hdf5AxisOption? _columnAxis;
    [ObservableProperty] private double _columnOffset;

    public ulong ColumnExtent => ColumnAxis is { } c ? _info.Space.Shape[c.Dimension] : 0;
    public bool HasColumnWindow => ColumnExtent > ColumnWindow;
    public double ColumnOffsetMaximum => HasColumnWindow ? ColumnExtent - ColumnWindow : 0;

    public string ColumnWindowText
    {
        get
        {
            var view = Snapshot();
            return Str.Format("Hdf5.Slice.ColumnWindowFormat",
                view.ColumnOffset, view.ColumnOffset + view.ColumnCount - 1, ColumnExtent);
        }
    }

    /// <summary>Raised whenever what the table shows changes.</summary>
    public event Action? Changed;

    /// <summary>An immutable copy of the current choice, safe to read off the UI thread.</summary>
    public Hdf5SliceView Snapshot()
    {
        ulong offset = (ulong)Math.Round(Math.Clamp(ColumnOffset, 0, ColumnOffsetMaximum));
        ulong count  = ColumnAxis is null ? 0 : Math.Min(ColumnWindow, ColumnExtent - offset);
        var fixedAt  = new ulong[Axes.Count];
        foreach (var f in FixedIndices) fixedAt[f.Dimension] = (ulong)Math.Round(Math.Clamp(f.Index, 0, f.Maximum));
        return new Hdf5SliceView(_info.Space, _info.Type, RowAxis?.Dimension ?? -1, ColumnAxis?.Dimension ?? -1,
                                 offset, count, fixedAt);
    }

    /// <summary>Holds each dimension at the given index (rows and columns ignore theirs) — how the AI shows a slice.</summary>
    public void SetIndices(IReadOnlyList<ulong> indices)
    {
        _rebuilding = true;
        foreach (var f in FixedIndices)
            if (f.Dimension < indices.Count) f.Index = Math.Min(indices[f.Dimension], (ulong)f.Maximum);
        _rebuilding = false;
        Changed?.Invoke();
    }

    /// <summary>Picking for rows the dimension that was the columns swaps the two, so each stays one dimension.</summary>
    partial void OnRowAxisChanged(Hdf5AxisOption? oldValue, Hdf5AxisOption? newValue)
    {
        if (_rebuilding) return;
        _rebuilding = true;
        if (newValue is not null && newValue == ColumnAxis) { ColumnAxis = oldValue; ColumnOffset = 0; }
        _rebuilding = false;
        RebuildFixed();
    }

    partial void OnColumnAxisChanged(Hdf5AxisOption? oldValue, Hdf5AxisOption? newValue)
    {
        if (_rebuilding) return;
        _rebuilding = true;
        if (newValue is not null && newValue == RowAxis) RowAxis = oldValue;
        ColumnOffset = 0;
        _rebuilding = false;
        RebuildFixed();
    }

    partial void OnColumnOffsetChanged(double value)
    {
        OnPropertyChanged(nameof(ColumnWindowText));
        if (!_rebuilding) Changed?.Invoke();
    }

    private void RebuildFixed()
    {
        foreach (var f in FixedIndices) f.PropertyChanged -= OnFixedIndexChanged;
        var held = FixedIndices.ToDictionary(f => f.Dimension, f => f.Index);
        FixedIndices.Clear();
        foreach (var axis in Axes)
        {
            if (axis == RowAxis || axis == ColumnAxis) continue;
            var f = new Hdf5FixedIndex(axis.Dimension, _info.Space.Shape[axis.Dimension])
            {
                Index = held.GetValueOrDefault(axis.Dimension),
            };
            f.PropertyChanged += OnFixedIndexChanged;
            FixedIndices.Add(f);
        }
        OnPropertyChanged(nameof(ColumnExtent));
        OnPropertyChanged(nameof(HasColumnWindow));
        OnPropertyChanged(nameof(ColumnOffsetMaximum));
        OnPropertyChanged(nameof(ColumnWindowText));
        OnPropertyChanged(nameof(HasSlice));
        Changed?.Invoke();
    }

    private void OnFixedIndexChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hdf5FixedIndex.Index) && !_rebuilding) Changed?.Invoke();
    }
}

/// <summary>
/// A frozen slice: turns a run of table rows into the selection that reads them, and a read block back into the
/// rows' cells. <see cref="RowAxis"/> is -1 for a scalar, <see cref="ColumnAxis"/> -1 when there is one value
/// column (1-D data) or the columns are compound members.
/// </summary>
public sealed record Hdf5SliceView(
    Hdf5Dataspace Space, Hdf5Type Type, int RowAxis, int ColumnAxis,
    ulong ColumnOffset, ulong ColumnCount, IReadOnlyList<ulong> Fixed)
{
    public ulong RowExtent => Space.Kind switch
    {
        Hdf5SpaceKind.Scalar => 1,
        Hdf5SpaceKind.Null   => 0,
        _                    => RowAxis >= 0 ? Space.Shape[RowAxis] : 0,
    };

    public IReadOnlyList<string> ColumnNames(string datasetName) =>
        Type.Class == Hdf5TypeClass.Compound
            ? [.. Type.Fields.Select(f => f.Name)]
            : ColumnAxis >= 0
                ? [.. Enumerable.Range(0, (int)ColumnCount).Select(c => (ColumnOffset + (ulong)c).ToString(CultureInfo.InvariantCulture))]
                : [datasetName];

    public Hdf5Selection SelectionFor(ulong rowStart, ulong rowCount)
    {
        if (Space.Kind != Hdf5SpaceKind.Simple) return Hdf5Selection.Scalar;
        var start = new ulong[Space.Rank];
        var count = new ulong[Space.Rank];
        for (int d = 0; d < Space.Rank; d++)
        {
            if (d == RowAxis)         { start[d] = rowStart;     count[d] = rowCount; }
            else if (d == ColumnAxis) { start[d] = ColumnOffset; count[d] = ColumnCount; }
            else                      { start[d] = Fixed[d];     count[d] = 1; }
        }
        return new Hdf5Selection(start, count);
    }

    /// <summary>The cells of the <paramref name="row"/>-th row a block read by <see cref="SelectionFor"/> holds.</summary>
    public string[] Cells(Hdf5Block block, ulong row)
    {
        if (Type.Class == Hdf5TypeClass.Compound)
        {
            long e = Index(block, row, 0);
            return [.. Enumerable.Range(0, Type.Fields.Count).Select(f => block.Format(e, f))];
        }
        if (ColumnAxis < 0) return [block.Format(Index(block, row, 0))];
        var cells = new string[ColumnCount];
        for (ulong c = 0; c < ColumnCount; c++) cells[c] = block.Format(Index(block, row, c));
        return cells;
    }

    /// <summary>The C-order element index of (row, column) in a block whose other dimensions are one wide.</summary>
    private long Index(Hdf5Block block, ulong row, ulong column)
    {
        long index = 0, stride = 1;
        for (int d = block.Shape.Count - 1; d >= 0; d--)
        {
            ulong coordinate = d == RowAxis ? row : d == ColumnAxis ? column : 0;
            index  += (long)coordinate * stride;
            stride *= (long)block.Shape[d];
        }
        return index;
    }
}
