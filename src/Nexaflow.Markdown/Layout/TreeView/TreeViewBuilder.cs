using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;

namespace Nexaflow.Markdown.Layout.TreeView;

/// <summary>
/// Draws a <c>treeView-beta</c> block: a row for each node, top to bottom as they are written, each set in further the deeper it
/// stands, a folder's name in bold, and a line from each folder down past what is in it with a branch across to each.
///
/// <para>
/// <strong>Rows, as it is drawn.</strong> A tree view on the page is a list — rows one under another — so that is what the layout is,
/// whatever the nodes nest as: the rows, every one across the whole width of the tree, and apart from them the lines joining folders
/// to what is in them, which stand for nothing and are never picked out. A row stands for its node with everything under it, though
/// it holds only its own row — so choosing a folder chooses all it holds, and what a press on a row means is that node. A row's own
/// band stands where its words are not, which is where a press picks the node up to be carried.
/// </para>
/// <para>
/// <strong>What is written is written in where it is drawn</strong>: a label, and what is said beside a node in italics. The slash
/// that makes a node a folder is not drawn — the bold is what says so. An icon is drawn where a node names one or icons are shown,
/// standing for its node; a name the app draws no icon for is drawn as a folder's or a file's.
/// </para>
/// </summary>
internal sealed class TreeViewBuilder : ContentBuilder
{
    /// <summary>The air between a node's icon and its label.</summary>
    private const double IconGap = 4;

    /// <summary>The air between a node's label and what is said beside it.</summary>
    private const double Beside = 10;

    /// <summary>How big what is said beside a node is set, against its label.</summary>
    private const double DescriptionScale = 0.85;

    private const int Bold = 700;

    internal TreeViewBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                             LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    private DiagramInk Ink { get; }

    private DiagramText Words { get; }

    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    /// <summary>What the front matter asks of the tree, as its stages left it on the block.</summary>
    private TreeViewConfig Config => DiagramConfig.Of(Reading, TreeViewConfig.Default);

    /// <summary>A node's row as it is measured: the node, what it is under, how deep, and what is set in it.</summary>
    private sealed record Row(ContentPart Part, TreeViewNode Said, Row? Parent, int Depth, DiagramWords? Icon, DiagramWords Label, DiagramWords? Description)
    {
        public double Top { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Middle => this.Top + (this.Height / 2);
        public List<Row> Under { get; } = [];
    }

    private LayoutSize Draw(LayoutBuilder build)
    {
        var config = Config;
        var rows = new List<Row>();
        Measured(Reading.Root, null, 0, config, rows);

        // A tree with no node to draw is the source: there is nothing to look at but the lines written.
        if (rows.Count == 0) return Sheet.AsWritten();

        var (wide, down) = (0.0, 0.0);

        foreach (var row in rows)
        {
            var tallest = Math.Max(row.Label.Height, Math.Max(row.Icon?.Height ?? 0, row.Description?.Height ?? 0));

            row.Top = down;
            row.Height = tallest + (config.PaddingY * 2);
            row.Left = row.Depth * (config.RowIndent + config.PaddingX);
            down += row.Height;

            var last = Placed(row, config)[^1];
            wide = Math.Max(wide, last.At.X + last.Words.Width + config.PaddingX);
        }

        Lines(build, rows, config);

        build.Open(TreeViewPiece.Rows, part: null, stops: LayoutStops.None);
        foreach (var row in rows) Lay(build, row, wide, config);
        build.Close();

        return Sheet.Reduced(new LayoutSize(wide, down));
    }

    /// <summary>The nodes gathered in <paramref name="holder"/>, each measured as a row and added to <paramref name="rows"/> in the order written, with the rows of what is under it after it.</summary>
    private void Measured(ContentPart holder, Row? parent, int depth, TreeViewConfig config, List<Row> rows)
    {
        foreach (var part in holder.Children)
        {
            if (part.Kind != TreeViewKinds.Node || part.Node is not TreeViewNode said) continue;

            var item = part.Children[0].Stated()!;
            var size = config.LabelFontSize;
            var label = item.Inner(TreeViewKinds.Label);
            var description = item.Inner(TreeViewKinds.Description);

            var row = new Row(part, said, parent, depth,
                              Iconed(part, item, said, size, config),
                              Words.Written(label.Words(), label.Hole(), size, Ink.Written(config.LabelColour) ?? Style.Text, said.Folder ? Bold : null),
                              description is null ? null
                                  : Words.Written(description.Inner(Kinds.Words), description.Hole(), size * DescriptionScale,
                                                  Ink.Written(config.DescriptionColour) ?? Style.TextMuted, italic: true));

            rows.Add(row);
            parent?.Under.Add(row);
            Measured(part, row, depth + 1, config, rows);
        }
    }

    /// <summary>
    /// The icon a node is drawn with: the one it names, where the app draws it — none, where it names none — and, where it names one the
    /// app does not draw or icons are shown, the one the front matter maps its name or its extension to, or a folder's or a file's.
    /// </summary>
    private DiagramWords? Iconed(ContentPart part, ContentPart item, TreeViewNode said, double size, TreeViewConfig config)
    {
        var ink = Ink.Written(config.IconColour) ?? Style.TextMuted;

        if (item.Inner(MermaidKinds.Icon) is { } named)
        {
            var name = named.Words()?.Text.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            if (Words.Iconed(named, size, ink) is { } drawn) return drawn;
        }
        else if (!config.ShowIcons) return null;

        var label = item.Inner(TreeViewKinds.Label)?.Words()?.Text ?? string.Empty;
        var dot = label.LastIndexOf('.');
        var mapped = config.FilenameIcons.GetValueOrDefault(label) ?? (dot > 0 ? config.ExtensionIcons.GetValueOrDefault(label[dot..]) : null);

        if (mapped?.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) == true) return null;
        if (mapped is not null && MermaidIcons.Of(mapped) is { } icon && Words.Icon(icon, part, size, ink) is { } chosen) return chosen;

        return Words.Icon(IconRef.Fluent(said.Folder ? "folder" : "document"), part, size, ink);
    }

    /// <summary>Where each of what is set in a row goes: its icon, its label after it, and what is said beside it after that.</summary>
    private static List<(DiagramWords Words, LayoutPoint At, string Kind)> Placed(Row row, TreeViewConfig config)
    {
        var placed = new List<(DiagramWords, LayoutPoint, string)>();
        var across = row.Left + config.PaddingX;

        if (row.Icon is { } icon)
        {
            placed.Add((icon, new LayoutPoint(across, row.Middle - (icon.Height / 2)), TreeViewPiece.Icon));
            across += icon.Width + IconGap;
        }

        placed.Add((row.Label, new LayoutPoint(across, row.Middle - (row.Label.Height / 2)), TreeViewPiece.Label));
        across += row.Label.Width;

        if (row.Description is { } description)
            placed.Add((description, new LayoutPoint(across + Beside, row.Middle - (description.Height / 2)), TreeViewPiece.Description));

        return placed;
    }

    /// <summary>
    /// The lines joining each folder to what is in it, as Mermaid draws them: down from under the folder's row to the middle of the row
    /// of the last thing in it, and across from that line to each of its rows. They stand for nothing.
    /// </summary>
    private void Lines(LayoutBuilder build, IReadOnlyList<Row> rows, TreeViewConfig config)
    {
        if (config.LineThickness <= 0) return;

        var ink = Ink.Written(config.LineColour) ?? Ink.Link;
        build.Open(TreeViewPiece.Lines, part: null, stops: LayoutStops.None);

        foreach (var row in rows.Where(row => row.Under.Count > 0))
        {
            var x = row.Left + config.PaddingX;
            Line(new LayoutPoint(x, row.Top + row.Height), new LayoutPoint(x, row.Under[^1].Middle));

            foreach (var under in row.Under) Line(new LayoutPoint(x, under.Middle), new LayoutPoint(under.Left, under.Middle));
        }

        build.Close();

        void Line(LayoutPoint from, LayoutPoint to) =>
            build.Draw(DiagramMarks.Outlined(Medium, Medium.Line(from, to), null, ink, config.LineThickness));
    }

    /// <summary>
    /// A node's row: its band across the whole tree — picked out where it takes the <c>highlight</c> class — standing where its words
    /// are not, and its icon, label and what is said beside it.
    /// </summary>
    private void Lay(LayoutBuilder build, Row row, double wide, TreeViewConfig config)
    {
        var band = new LayoutRect(0, row.Top, wide, row.Height);
        var placed = Placed(row, config);
        var outline = Medium.Rectangle(band);

        build.Open(TreeViewPiece.Row, row.Part, stops: LayoutStops.None);

        build.Open(MermaidPiece.Shape, row.Part, stops: LayoutStops.None);
        if (row.Said.Highlighted)
            build.Draw(DiagramMarks.Outlined(Medium, Medium.Rectangle(band, 3, 3),
                                             Ink.Written(config.HighlightBackground) ?? Ink.Faded(Style.Marked, 0.35),
                                             Ink.Written(config.HighlightStroke) ?? Style.Marked, 1));
        build.Covers(band);
        build.Occupies(Medium.Combine(outline, Medium.Group([.. placed.Select(words => Medium.Rectangle(new LayoutRect(words.At, new LayoutSize(words.Words.Width, words.Words.Height))))], evenOdd: false), ShapeCombine.Exclude));
        build.Close();

        foreach (var (words, at, kind) in placed) words.Set(build, at, kind);

        build.Close();
    }
}
