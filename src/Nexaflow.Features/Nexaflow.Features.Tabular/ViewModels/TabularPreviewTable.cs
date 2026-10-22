namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>
/// A few rows of a table as aligned monospace text, for the context preview: the header, a rule, then each
/// row, over the first <see cref="MaxColumns"/> columns with each cell cut to <see cref="MaxCellWidth"/>. A
/// reminder of what the table holds, not a second grid.
/// </summary>
public static class TabularPreviewTable
{
    public const int MaxColumns   = 4;
    public const int MaxCellWidth = 14;

    private const string Gap = "  ";

    public static string Format(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var shown = Math.Min(headers.Count, MaxColumns);
        if (shown == 0) return string.Empty;

        var table  = rows.Select(row => Cut(row, shown)).Prepend(Cut(headers, shown)).ToList();
        var widths = Enumerable.Range(0, shown).Select(c => table.Max(row => row[c].Length)).ToArray();

        var lines = new List<string>(table.Count + 1)
        {
            Join(table[0], widths) + (headers.Count > shown ? Gap + "…" : string.Empty),
            string.Join(Gap, widths.Select(w => new string('─', w))),
        };
        lines.AddRange(table.Skip(1).Select(row => Join(row, widths)));
        return string.Join("\n", lines);
    }

    private static string[] Cut(IReadOnlyList<string> cells, int count) =>
        Enumerable.Range(0, count).Select(i => i < cells.Count ? Shorten(cells[i]) : string.Empty).ToArray();

    private static string Shorten(string cell)
    {
        var flat = cell.ReplaceLineEndings(" ");
        return flat.Length <= MaxCellWidth ? flat : flat[..(MaxCellWidth - 1)] + "…";
    }

    private static string Join(string[] cells, int[] widths) =>
        string.Join(Gap, cells.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd();
}
