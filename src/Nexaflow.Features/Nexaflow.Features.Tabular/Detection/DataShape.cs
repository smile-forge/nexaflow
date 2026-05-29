using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Features.Tabular.Detection;

/// <summary>
/// Mutable, view-model-friendly model of the data layout. Holds the raw shape (separator,
/// quote, header tokens, body column types) plus the chain of user-applied morphs. The
/// effective columns/headers/types displayed in the grid are always derived from the raw
/// state by replaying the transform chain — never edited in place. That way the user can
/// always "undo" a Merge / Split / Evaluate by removing it from the chain, and we never
/// drift away from the file's actual layout.
/// </summary>
public sealed class DataShape
{
    public CsvShape                     RawShape         { get; }
    /// <summary>The header line's tokens, untrimmed, exactly as the tokenizer produced them.
    /// Null when the file has no detected header — in which case we synthesize "Column 1" etc.</summary>
    public string[]?                    RawHeaderCells   { get; }
    public IReadOnlyList<IColumnTransform> Transforms { get; }
    /// <summary>Number of "#"-prefixed comment lines at the very start of the file that
    /// readers should skip before parsing any data (and that the row counter should NOT
    /// include in the file row count).</summary>
    public int                          LeadingCommentCount { get; }

    private readonly List<IColumnTransform> _transforms;

    public DataShape(CsvShape rawShape, string[]? rawHeaderCells, int leadingCommentCount = 0)
    {
        RawShape            = rawShape;
        RawHeaderCells      = rawHeaderCells;
        LeadingCommentCount = leadingCommentCount;
        _transforms         = new List<IColumnTransform>();
        Transforms          = _transforms;
    }

    public void AddTransform(IColumnTransform t) => _transforms.Add(t);
    public void ClearTransforms()                => _transforms.Clear();

    public char?  Separator => RawShape.Separator;
    public char?  Quote     => RawShape.Quote;
    public bool   HasHeader => RawShape.HasHeader;

    /// <summary>The current effective column descriptors after applying every transform.
    /// For files with a header, headers are derived by replaying the transform chain on
    /// the raw header tokens; for headerless files, columns are renumbered sequentially.</summary>
    public ColumnDescriptor[] EffectiveColumns
    {
        get
        {
            var rawCols = BuildRawColumnDescriptors();
            ColumnDescriptor[] current = rawCols;
            foreach (var t in _transforms) current = t.ApplyToColumns(current);

            if (!HasHeader)
            {
                // Renumber sequentially — Merge/Split don't preserve meaningful labels
                // when there's no header line to draw from. Rename'd columns are exempt.
                for (int i = 0; i < current.Length; i++)
                    if (!current[i].IsHeaderExplicit)
                        current[i] = current[i] with { Header = $"Column {i + 1}" };
            }
            else if (RawHeaderCells is { } raw)
            {
                // Re-derive headers by applying the same transforms to the raw header tokens
                // — preserves the original whitespace and re-tokenizes naturally for merges.
                string[] effectiveHeaders = (string[])raw.Clone();
                foreach (var t in _transforms) effectiveHeaders = t.Apply(effectiveHeaders);
                for (int i = 0; i < current.Length && i < effectiveHeaders.Length; i++)
                {
                    // Don't override user-renamed columns.
                    if (current[i].IsHeaderExplicit) continue;
                    var trimmed = effectiveHeaders[i].Trim();
                    // Skip empty — keeps the ApplyToColumns default ("{base} 2" after a split,
                    // for example) when the raw header text has no value at this position.
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    current[i] = current[i] with { Header = trimmed };
                }
            }
            return current;
        }
    }

    /// <summary>Applies the transform chain to a raw body row.</summary>
    public string[] HydrateRow(string[] rawRow)
    {
        var current = rawRow;
        foreach (var t in _transforms) current = t.Apply(current);
        return current;
    }

    private ColumnDescriptor[] BuildRawColumnDescriptors()
    {
        var cols = new ColumnDescriptor[RawShape.FieldCount];
        var fileSep = RawShape.Separator?.ToString();
        for (int i = 0; i < RawShape.FieldCount; i++)
        {
            string label;
            if (HasHeader && RawHeaderCells is { } raw && i < raw.Length)
            {
                var trimmed = raw[i].Trim();
                label = string.IsNullOrEmpty(trimmed) ? $"Column {i + 1}" : trimmed;
            }
            else label = $"Column {i + 1}";
            var type = i < RawShape.ColumnTypes.Count ? RawShape.ColumnTypes[i] : CsvDataType.String;
            cols[i] = new ColumnDescriptor(label, type, LeftSeparator: fileSep);
        }
        return cols;
    }
}
