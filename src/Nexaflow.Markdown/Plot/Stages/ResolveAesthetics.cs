using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Says under each cell which channel it feeds — ggplot2's <c>aes()</c>, worked out rather than written
/// twice.
///
/// <para>
/// A setting names a column or it does not, and that is the whole of the rule: <c>size: pop</c> maps the
/// pop column to size, and <c>size: 4</c> sets the size of every mark. Which of the two a value is
/// cannot be known from the characters — <c>4</c> is a perfectly good column name — so it is settled
/// here, where the columns are known, and only a mapping leaves a fact behind. A constant is nobody's
/// business but the builder's.
/// </para>
/// <para>
/// <strong>What nobody mapped, the columns take in order.</strong> The first free column is x and the
/// next is y, which is what makes two columns of numbers a plot with nothing written at all. A third
/// channel is only filled in where the fence asks for one — a bubble plot's size, a heat map's fill —
/// because a scatter plot of three columns means two of them and a name, and guessing otherwise would
/// draw a chart nobody asked for.
/// </para>
/// <para>
/// A matrix means something else by its columns: across it is x, down it is y, and the cell itself is
/// the value. So every cell of one feeds fill, and the x and y are the name above it and the name
/// beside it, which the shape stage has already hung there.
/// </para>
/// </summary>
public sealed class ResolveAesthetics(PlotSettings settings) : IAstStage
{
    public string Name => "plot:aesthetics";

    public ContentNode Run(ContentNode tree)
    {
        if (tree.Rows().Count == 0) return tree;

        if (tree.Said(PlotRoles.Form) == ResolveShape.Matrix)
            return AstRewrite.Each(tree, node => node.Kind == PlotKinds.Row && !node.IsHeader()
                ? node.WithCells((cell, at) => at == 0 ? cell : Feeding(cell, [PlotAesthetic.Fill]))
                : node);

        var mapped = this.Mapped(tree);

        return AstRewrite.Each(tree, node => node.Kind == PlotKinds.Row && !node.IsHeader()
            ? node.WithCells((cell, at) =>
                  mapped.TryGetValue(at, out var channels) ? Feeding(cell, channels) : cell)
            : node);
    }

    private static ContentNode Feeding(ContentNode cell, IReadOnlyList<PlotAesthetic> channels) =>
        cell.Telling([.. channels.Select(channel => (PlotKinds.Fact, PlotRoles.Aesthetic, Written(channel)))]);

    /// <summary>A channel as a fact carries it, which is how it is written.</summary>
    public static string Written(PlotAesthetic channel) => channel.ToString().ToLowerInvariant();

    // ── Which column feeds what ─────────────────────────────────────────────

    /// <summary>
    /// Which channels each column feeds.
    ///
    /// <para>
    /// A column may feed several: <c>colour: region</c> beside <c>shape: region</c> is how a chart tells its
    /// groups apart twice over. So this is a list per column rather than one channel, and a cell carries a
    /// fact for each.
    /// </para>
    /// <para>
    /// <strong>Only a place is used up by being taken.</strong> What nobody mapped, the columns take in
    /// order — but a column feeding colour or alpha is still free to be x, because those say something
    /// <em>about</em> a mark rather than where it goes. Counting them as taken left a plot of four columns
    /// with three of them spoken for and nothing at all to put up the page.
    /// </para>
    /// </summary>
    private IReadOnlyDictionary<int, List<PlotAesthetic>> Mapped(ContentNode tree)
    {
        var columns = Columns(tree);
        var mapped = new Dictionary<int, List<PlotAesthetic>>();
        var spoken = new HashSet<PlotAesthetic>();

        // The columns standing for somewhere on the panel, which is what cannot be shared.
        var placed = new HashSet<int>();

        Map(settings.X, PlotAesthetic.X);
        Map(settings.Y, PlotAesthetic.Y);
        Map(settings.Colour, PlotAesthetic.Colour);
        Map(settings.Fill, PlotAesthetic.Fill);
        Map(settings.Size, PlotAesthetic.Size);
        Map(settings.Shape, PlotAesthetic.Shape);
        Map(settings.Alpha, PlotAesthetic.Alpha);
        Map(settings.Label, PlotAesthetic.Label);
        Map(settings.Group, PlotAesthetic.Group);

        Fall(PlotAesthetic.X);
        Fall(PlotAesthetic.Y);
        if (settings.Third is { } third) Fall(third);

        return mapped;

        void Map(string? written, PlotAesthetic channel)
        {
            if (Named(written, columns) is not { } which) return;

            if (!mapped.TryGetValue(which, out var channels)) mapped[which] = channels = [];

            channels.Add(channel);
            spoken.Add(channel);

            if (Places(channel)) placed.Add(which);
        }

        // The first column no place has been given to yet, where this channel has none.
        void Fall(PlotAesthetic channel)
        {
            if (spoken.Contains(channel)) return;

            for (var at = 0; at < columns.Count; at++)
                if (!placed.Contains(at))
                {
                    if (!mapped.TryGetValue(at, out var channels)) mapped[at] = channels = [];

                    channels.Add(channel);
                    spoken.Add(channel);
                    placed.Add(at);

                    return;
                }
        }
    }

    /// <summary>
    /// Whether a channel says where a mark goes rather than what it looks like. Two marks may share a
    /// colour; they cannot share a place.
    /// </summary>
    private static bool Places(PlotAesthetic channel) =>
        channel is PlotAesthetic.X or PlotAesthetic.Y or PlotAesthetic.Fill or PlotAesthetic.Size;

    /// <summary>
    /// Which column <paramref name="written"/> names, by its name or by its place counted from one — or
    /// null, which is what makes it a constant.
    /// </summary>
    private static int? Named(string? written, IReadOnlyList<string> columns)
    {
        if (string.IsNullOrWhiteSpace(written)) return null;

        var said = written.Trim();

        for (var at = 0; at < columns.Count; at++)
            if (string.Equals(columns[at], said, StringComparison.OrdinalIgnoreCase))
                return at;

        // A column named by its place, which is how a table with no header is spoken about. A number
        // that reaches past the columns names none, and is a constant like any other number.
        return int.TryParse(said, out var which) && which >= 1 && which <= columns.Count
            ? which - 1
            : null;
    }

    /// <summary>
    /// What the columns are called, by place. A table with no header still has columns, so this is how
    /// many the widest row has, with no names to them.
    /// </summary>
    private static IReadOnlyList<string> Columns(ContentNode tree)
    {
        var rows = tree.Rows();
        var header = rows.FirstOrDefault(row => row.IsHeader());

        if (header is not null) return [.. header.Cells().Select(cell => cell.Says())];

        var wide = rows.Max(row => row.Cells().Count);

        return [.. Enumerable.Repeat(string.Empty, wide)];
    }
}
