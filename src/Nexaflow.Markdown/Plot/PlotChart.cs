using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Plot.Stages;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// What one channel was given for one mark: the cell it came from, what that cell says, and the number
/// it reads as where it reads as one.
/// </summary>
/// <param name="Cell">Where it was written — which is what a press on the mark means.</param>
/// <param name="Inner">
/// The characters themselves, without the quotes the cell may have been written in. What is drawn where
/// a value is drawn, and so where a caret goes and what typing changes.
/// </param>
public sealed record PlotValue(ContentPart Cell, ContentPart Inner, string Text, double? Number)
{
    /// <summary>Whether this is a number rather than a name, which is what makes its channel continuous.</summary>
    public bool Counts => this.Number is not null;
}

/// <summary>
/// One mark of a plot, and what it was given for each channel it feeds.
/// </summary>
/// <param name="Part">
/// What the mark stands for: the row it was drawn from, or — down a matrix, where a row is a line of
/// marks rather than one — the cell itself.
/// </param>
public sealed record PlotMark(ContentPart Part, IReadOnlyDictionary<PlotAesthetic, PlotValue> Channels)
{
    /// <summary>What this mark was given for a channel, or null where it feeds none.</summary>
    public PlotValue? this[PlotAesthetic channel] => this.Channels.GetValueOrDefault(channel);
}

/// <summary>
/// A plot block read back into what it describes: its settings, its columns, and a mark for every value
/// the table holds.
///
/// <para>
/// Everything here was worked out by the stages and is only gathered up — which is what keeps the two
/// readings of "which column is x" from ever disagreeing. What the model adds is the questions a drawing
/// asks that no single mark can answer: how far a channel reaches, and what its values are where they
/// are names rather than numbers.
/// </para>
/// </summary>
public sealed record PlotChart(PlotSettings Settings,
                               IReadOnlyList<string> Columns,
                               IReadOnlyList<PlotMark> Marks)
{
    /// <summary>The chart the tree describes, once the stages have been over it.</summary>
    public static PlotChart Of(ContentPart root, PlotSettings settings)
    {
        var matrix = root.Node.Said(PlotRoles.Form) == ResolveShape.Matrix;
        var rows = root.SelfAndDescendants().Where(part => part.Kind == PlotKinds.Row).ToList();

        var header = rows.FirstOrDefault(row => row.Node.IsHeader());
        IReadOnlyList<string> columns = header is null
            ? []
            : [.. Cells(header).Select(Says)];

        var marks = new List<PlotMark>();

        foreach (var row in rows)
        {
            if (row.Node.IsHeader()) continue;

            if (matrix) Down(row, marks);
            else Across(row, marks);
        }

        return new PlotChart(settings, columns, marks) { Matrix = matrix };
    }

    /// <summary>
    /// Whether the table was read down its side as well as across it. Kept because it changes what the
    /// axes mean: down a matrix the columns are the values, so neither axis has a name of its own.
    /// </summary>
    public bool Matrix { get; init; }

    /// <summary>A long row: one mark, its channels the cells that feed them.</summary>
    private static void Across(ContentPart row, List<PlotMark> marks)
    {
        var channels = new Dictionary<PlotAesthetic, PlotValue>();

        foreach (var cell in Cells(row))
            foreach (var channel in Feeds(cell))
                channels[channel] = Valued(cell, Says(cell));

        if (channels.Count > 0) marks.Add(new PlotMark(row, channels));
    }

    /// <summary>
    /// A row of a matrix: a mark per value, its x the column above it and its y the name beside it.
    ///
    /// <para>
    /// Both of those are facts about the cell rather than cells of their own, so they carry the cell as
    /// where they came from — which is right, because pressing the tile is what means them.
    /// </para>
    /// </summary>
    private static void Down(ContentPart row, List<PlotMark> marks)
    {
        var names = row.Node.Said(PlotRoles.Names) ?? string.Empty;

        foreach (var cell in Cells(row))
        {
            if (!Feeds(cell).Contains(PlotAesthetic.Fill)) continue;

            marks.Add(new PlotMark(cell, new Dictionary<PlotAesthetic, PlotValue>
            {
                [PlotAesthetic.X] = new(cell, cell, cell.Node.Said(PlotRoles.Column) ?? string.Empty, null),
                            [PlotAesthetic.Y] = new(cell, cell, names, null),
                [PlotAesthetic.Fill] = Valued(cell, Says(cell)),
            }));
        }
    }

    // ── What a drawing asks that a mark cannot answer ───────────────────────

    /// <summary>
    /// Whether a channel is drawn as numbers rather than as one of a few names.
    ///
    /// <para>
    /// <strong>It is, when more of its values read as numbers than do not.</strong> A column of names has
    /// none at all, and a column of numbers with a half-typed cell in it still has most — which is the
    /// difference between an axis of categories and an axis with something wrong on one row of it. Letting
    /// a single unreadable cell turn the axis categorical would rearrange the whole picture under somebody
    /// midway through typing a number, which is the one moment it must not.
    /// </para>
    /// </summary>
    public bool Counts(PlotAesthetic channel)
    {
        var numbers = 0;
        var names = 0;

        foreach (var mark in this.Marks)
        {
            if (mark[channel] is not { } value) continue;

            if (value.Counts) numbers++;
            else names++;
        }

        return numbers > names;
    }

    /// <summary>How far a channel's numbers reach, or null where it has none.</summary>
    public (double Min, double Max)? Reach(PlotAesthetic channel)
    {
        double? min = null, max = null;

        foreach (var mark in this.Marks)
            if (mark[channel]?.Number is { } number)
            {
                min = min is null ? number : Math.Min(min.Value, number);
                max = max is null ? number : Math.Max(max.Value, number);
            }

        return min is null || max is null ? null : (min.Value, max.Value);
    }

    /// <summary>
    /// What a channel's values are where they are names, in the order they were first written — which is
    /// the order they are placed along an axis and listed in the key.
    /// </summary>
    public IReadOnlyList<string> Named(PlotAesthetic channel)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mark in this.Marks)
            if (mark[channel] is { } value && known.Add(value.Text))
                seen.Add(value.Text);

        return seen;
    }

    /// <summary>
    /// The distinct values of a channel, in the order they are laid along an axis of slots: by number where
    /// they are all numbers, and in the order they were first written otherwise.
    ///
    /// <para>
    /// What a grid of tiles is laid on. A tile stands for one value rather than a stretch of them, so an
    /// axis carrying tiles has as many places as there are values — and a run of years read as a plain
    /// range would leave each tile a third of the panel wide with three of them to fit, which is how a heat
    /// map ends up drawn over its own title.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Slots(PlotAesthetic channel)
    {
        var seen = new List<PlotValue>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mark in this.Marks)
            if (mark[channel] is { } value && known.Add(value.Text))
                seen.Add(value);

        return seen.All(value => value.Counts)
            ? [.. seen.OrderBy(value => value.Number!.Value).Select(value => value.Text)]
            : [.. seen.Select(value => value.Text)];
    }

    /// <summary>What a channel is called: what the block says, else the name of the column feeding it.</summary>
    public string? Titled(PlotAesthetic channel, string? written)
    {
        if (written is not null) return written;

                // Down a matrix the columns are the values and the rows are named beside them, so an axis
                // titled with any one column's name would be naming one of its own ticks.
                if (this.Matrix && channel is PlotAesthetic.X or PlotAesthetic.Y) return null;

        foreach (var mark in this.Marks)
            if (mark[channel]?.Cell.Node.Said(PlotRoles.Column) is { } column && column.Length > 0)
                return column;

        return null;
    }

    // ── Reading the tree back ───────────────────────────────────────────────

    private static IEnumerable<ContentPart> Cells(ContentPart row) =>
        row.Children.Where(child => child.Kind == PlotKinds.Cell);

    private static string Says(ContentPart cell) =>
        cell.Node.IsLeaf
            ? cell.Node.Text
            : cell.Children.FirstOrDefault(child => child.Kind == PlotKinds.Cell && child.Node.IsLeaf)
                  ?.Node.Text ?? string.Empty;

    /// <summary>Every channel a cell feeds — a column may feed several.</summary>
    private static IReadOnlyList<PlotAesthetic> Feeds(ContentPart cell)
    {
        var channels = new List<PlotAesthetic>();

        foreach (var fact in cell.Node.Facts(PlotRoles.Aesthetic))
            if (Enum.TryParse<PlotAesthetic>(fact.Text, ignoreCase: true, out var channel))
                channels.Add(channel);

        return channels;
    }

    private static PlotValue Valued(ContentPart cell, string text) =>
        new(cell, Inner(cell), text, SettingValues.Read(cell.Node.Said(PlotRoles.Number) ?? text));

    /// <summary>
    /// The part holding a cell's own characters: the cell itself where it is bare, and what is between the
    /// quotes where it is not. A caret goes in here and nowhere else — the quotes are what hold the value
    /// together, not part of it.
    /// </summary>
    private static ContentPart Inner(ContentPart cell) =>
        cell.Node.IsLeaf
            ? cell
            : cell.Children.FirstOrDefault(child => child.Kind == PlotKinds.Cell && child.Node.IsLeaf) ?? cell;
}
