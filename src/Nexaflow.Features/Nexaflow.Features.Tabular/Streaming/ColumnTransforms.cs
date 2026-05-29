using System.Collections.Generic;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.Streaming;

/// <summary>
/// One column in the current effective layout.
/// </summary>
/// <param name="Header">Display name.</param>
/// <param name="Type">Data type used for sort / filter / formatting.</param>
/// <param name="IsHeaderExplicit">True when the user renamed the column; prevents
///   <c>DataShape</c>'s raw-header re-tokenize step from overwriting it.</param>
/// <param name="LeftSeparator">The literal string that produced this column's *left*
///   boundary. For raw columns this is the file's detected separator; for columns produced
///   by a <c>SplitByTransform</c> the first part keeps the original (its left edge wasn't
///   created by the split) and parts 2..N get the split character. <c>MergeWithPrevious</c>
///   uses this so that split-then-merge round-trips cleanly — without it the merge would
///   always use the file separator, even after splitting on something else like space.</param>
public sealed record ColumnDescriptor(
    string      Header,
    CsvDataType Type,
    bool        IsHeaderExplicit = false,
    string?     LeftSeparator    = null,
    bool        IsTypeExplicit   = false);

public interface IColumnTransform
{
    string[] Apply(string[] cells);
    ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols);
}

/// <summary>
/// Concatenates the values at <paramref name="Indices"/> using <paramref name="Separator"/>
/// and replaces the first index with the merged value; remaining indices are dropped.
/// </summary>
public sealed class MergeColumnsTransform(int[] Indices, string Separator) : IColumnTransform
{
    public IReadOnlyList<int> MergedIndices => Indices;
    public string             MergedJoinWith => Separator;
    private readonly int[] _sorted = Indices.OrderBy(i => i).ToArray();

    public string[] Apply(string[] cells)
    {
        if (_sorted.Length < 2) return cells;
        int first = _sorted[0];
        if (first >= cells.Length) return cells;

        var parts = new List<string>(_sorted.Length);
        foreach (var idx in _sorted)
            if (idx < cells.Length) parts.Add(cells[idx]);

        var merged = string.Join(Separator, parts);

        var output = new List<string>(cells.Length);
        for (int i = 0; i < cells.Length; i++)
        {
            if (i == first) output.Add(merged);
            else if (System.Array.IndexOf(_sorted, i) >= 0) continue;
            else output.Add(cells[i]);
        }
        return output.ToArray();
    }

    public ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols)
    {
        if (_sorted.Length < 2) return cols;
        int first = _sorted[0];
        if (first >= cols.Length) return cols;

        var labels = _sorted.Where(i => i < cols.Length).Select(i => cols[i].Header);
        var mergedHeader = string.Join(Separator, labels);

        var output = new List<ColumnDescriptor>(cols.Length);
        for (int i = 0; i < cols.Length; i++)
        {
            if (i == first) output.Add(new ColumnDescriptor(mergedHeader, CsvDataType.String));
            else if (System.Array.IndexOf(_sorted, i) >= 0) continue;
            else output.Add(cols[i]);
        }
        return output.ToArray();
    }
}

/// <summary>
/// Reverses an overly-aggressive split: the boundary between column <paramref name="Index"/>
/// and <paramref name="Index"/>-1 was not a true field separator from the user's perspective,
/// so re-join the cells using the original separator <paramref name="JoinWith"/> as data.
/// </summary>
public sealed class MergeWithPreviousTransform(int Index, string JoinWith) : IColumnTransform
{
    public int    MergeIndex    => Index;
    public string MergeJoinWith => JoinWith;

    public string[] Apply(string[] cells)
    {
        if (Index <= 0 || Index >= cells.Length) return cells;
        var output = new string[cells.Length - 1];
        for (int i = 0; i < Index - 1; i++) output[i] = cells[i];
        output[Index - 1] = cells[Index - 1] + JoinWith + cells[Index];
        for (int i = Index + 1; i < cells.Length; i++) output[i - 1] = cells[i];
        return output;
    }

    public ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols)
    {
        if (Index <= 0 || Index >= cols.Length) return cols;
        var output = new ColumnDescriptor[cols.Length - 1];
        for (int i = 0; i < Index - 1; i++) output[i] = cols[i];
        // Re-derive the merged header by joining the two original headers. Type resets to
        // String + IsTypeExplicit cleared so the orchestrator re-classifies the merged
        // column from the new (combined) sample data.
        output[Index - 1] = new ColumnDescriptor(
            cols[Index - 1].Header + JoinWith + cols[Index].Header,
            CsvDataType.String,
            LeftSeparator: cols[Index - 1].LeftSeparator);
        for (int i = Index + 1; i < cols.Length; i++) output[i - 1] = cols[i];
        return output;
    }
}

/// <summary>
/// Splits the value at <paramref name="Index"/> by <paramref name="Separator"/> into
/// exactly <paramref name="Arity"/> cells. <see cref="Arity"/> is sampled at split-invocation
/// time from the column's current sample values (max occurrences seen, ≥2). Bodies that
/// produce fewer parts pad with ""; bodies with more occurrences keep the overflow inside
/// the last cell so the user can re-split it again if they want finer granularity.
/// The original column header is preserved on the first part; the rest get
/// "{header} 2", "{header} 3", … defaults that can be overridden by Rename.
/// </summary>
public sealed class SplitByTransform(int Index, char Separator, int Arity) : IColumnTransform
{
    public int    SplitIndex     => Index;
    public char   SplitSeparator => Separator;
    public int    SplitArity     => Arity;

    public string[] Apply(string[] cells)
    {
        if (Index < 0 || Index >= cells.Length || Arity < 2) return cells;
        var parts = cells[Index].Split(Separator, Arity);
        var padded = new string[Arity];
        for (int i = 0; i < Arity; i++)
            padded[i] = i < parts.Length ? parts[i] : string.Empty;
        var output = new string[cells.Length + Arity - 1];
        System.Array.Copy(cells, 0, output, 0, Index);
        System.Array.Copy(padded, 0, output, Index, Arity);
        System.Array.Copy(cells, Index + 1, output, Index + Arity, cells.Length - Index - 1);
        return output;
    }

    public ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols)
    {
        if (Index < 0 || Index >= cols.Length || Arity < 2) return cols;
        var original  = cols[Index];
        var baseLabel = original.Header;
        var splitSep  = Separator.ToString();
        var output = new List<ColumnDescriptor>(cols.Length + Arity - 1);
        for (int i = 0; i < cols.Length; i++)
        {
            if (i == Index)
            {
                // First part keeps the header + LeftSeparator of the original (its left edge
                // wasn't created by this split), but its type is RESET to String so the
                // orchestrator re-classifies it from the new sample. The rest are fresh
                // string-typed columns whose LeftSeparator is the split character, so a
                // later MergeWithPrevious rejoins on it.
                output.Add(original with { Type = CsvDataType.String, IsTypeExplicit = false });
                for (int p = 2; p <= Arity; p++)
                    output.Add(new ColumnDescriptor(
                        $"{baseLabel} {p}", CsvDataType.String,
                        LeftSeparator: splitSep));
            }
            else output.Add(cols[i]);
        }
        return output.ToArray();
    }
}

/// <summary>
/// User-supplied display name for one column. Sets <see cref="ColumnDescriptor.IsHeaderExplicit"/>
/// so <see cref="Nexaflow.Features.Tabular.Detection.DataShape"/>'s raw-header re-tokenize
/// step doesn't overwrite the rename.
/// </summary>
public sealed class RenameColumnTransform(int Index, string NewHeader) : IColumnTransform
{
    public int    RenameIndex => Index;
    public string RenameTo    => NewHeader;

    public string[] Apply(string[] cells) => cells;

    public ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols)
    {
        if (Index < 0 || Index >= cols.Length) return cols;
        var output = (ColumnDescriptor[])cols.Clone();
        output[Index] = output[Index] with { Header = NewHeader, IsHeaderExplicit = true };
        return output;
    }
}

/// <summary>
/// Re-classifies the displayed type of one column; the underlying cell text is unchanged
/// but downstream sort/filter compare using the new type.
/// </summary>
public sealed class EvaluateAsTransform(int Index, CsvDataType Target) : IColumnTransform
{
    public int         EvalIndex  => Index;
    public CsvDataType EvalTarget => Target;

    public string[] Apply(string[] cells) => cells; // no-op for raw values
    public ColumnDescriptor[] ApplyToColumns(ColumnDescriptor[] cols)
    {
        if (Index < 0 || Index >= cols.Length) return cols;
        var output = (ColumnDescriptor[])cols.Clone();
        // IsTypeExplicit=true tells DataShape/orchestrator not to re-classify this column
        // from the sample data — the user's explicit choice wins.
        output[Index] = output[Index] with { Type = Target, IsTypeExplicit = true };
        return output;
    }
}
