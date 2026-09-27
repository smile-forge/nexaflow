using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;

/// <summary>
/// What an edit means in a pie chart, and what the chart offers where it is right-clicked.
///
/// <para>
/// In the legend, a slice's label and its value are the places written in. Enter at either starts the next slice under it, a
/// label and a value still to write, and the caret in the label; at the title, the first slice. Shift+Enter breaks the label's
/// line. Delete at the end of a label or a value and Backspace at the start of one take nothing, because past either is what
/// holds the slice together. Tab goes from a label to its value, and from a value to the next slice's label. With whole slices
/// picked out, Delete takes their lines — leaving one slice still to write where they were all there was — and Insert starts a
/// slice above them. A paste goes into a label, a value or the title, as the words it holds with them in, which the parser
/// makes safe — a value takes only what still reads as a number; anywhere else in the chart nothing goes in.
/// </para>
/// <para>
/// A right-click on the legend offers where the legend goes; one on the chart, how big a hole it has and how thick the lines
/// between its wedges are — each drawn as what it would make. What is chosen is written into the front matter, at the field it
/// names — given its new value where it is written, and written under the field that holds it where it is not. With the caret in
/// a label, a value or the title, it offers to paste there too.
/// </para>
/// <para>
/// Every change names the node of the chart's tree it gives a new value; only text typed at the caret is named by where the
/// caret is.
/// </para>
/// </summary>
internal sealed class PieEdits : IContentLanguage, IOnEdit
{
    /// <summary>A slice with nothing written in it yet.</summary>
    private const string Blank = "\"\" : ";

    /// <summary>How a line breaks inside a label.</summary>
    private const string Break = "<br>";

    private const string Legend = "Legend";
    private const string Hole = "Hole";
    private const string Stroke = "Stroke";

    /// <summary>Where the legend may go: what is offered, the front matter's word for it, and what it is read back as.</summary>
    private static readonly (string Tip, string Written, PieLegend Read)[] Placings =
    [
        ("Left", "left", PieLegend.Left),
        ("Top", "top", PieLegend.Top),
        ("Right", "right", PieLegend.Right),
        ("Bottom", "bottom", PieLegend.Bottom),
        ("Centre", "center", PieLegend.Centre),
    ];

    /// <summary>How big the hole may be, as a share of the radius.</summary>
    private static readonly (string Tip, string Written, double Read)[] Holes =
    [
        ("None", "0", 0),
        ("20%", "0.2", 0.2),
        ("50%", "0.5", 0.5),
        ("80%", "0.8", 0.8),
    ];

    /// <summary>How thick the lines between the wedges may be.</summary>
    private static readonly (string Tip, string Written, double Read)[] Strokes =
    [
        ("None", "0", 0),
        ("Thin", "2", 2),
        ("Thick", "4", 4),
    ];

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing => DiagramWriting.Typed(edit, Escaping),
        EditKind.Pasting => Pasted(edit),
        EditKind.Settling when edit.Text == "\n" => Entered(edit),
        EditKind.Settling => DiagramWriting.Typed(edit, Escaping),
        EditKind.Breaking => Broken(edit),
        EditKind.Erasing or EditKind.Deleting => Erased(edit),
        EditKind.Tabbing => Tabbed(edit, forward: true),
        EditKind.TabbingBack => Tabbed(edit, forward: false),
        EditKind.Inserting => Inserted(edit),
        EditKind.Choosing => Chosen(edit),
        EditKind.Dropping => Dropped(edit),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<LayoutIntent> Offers(ContentAsk ask)
    {
        if (ask.IsReadOnly) return [];

        var config = (ask.Root?.Node as ConfiguredNode<PieConfig>)?.Config ?? PieConfig.Default;
        var kinds = new HashSet<string>();
        for (var up = ask.Piece; up.Exists; up = up.Parent) kinds.Add(up.Kind);

        if (kinds.Contains(MermaidPiece.Legend))
            return
            [
                .. Placings.Select(placing => Offer(Legend, placing.Written, placing.Tip, Placed(placing.Read), config.Legend == placing.Read)),
                .. Pasting(ask),
            ];

        if (kinds.Contains(PiePiece.Slices) || kinds.Contains(PiePiece.Shares))
            return
            [
                .. Holes.Select(hole => Offer(Hole, hole.Written, hole.Tip, Holed(hole.Read), Math.Abs(config.DonutHole - hole.Read) < 0.001)),
                .. Strokes.Select(stroke => Offer(Stroke, stroke.Written, stroke.Tip, Stroked(stroke.Read), Math.Abs(config.StrokeWidth - stroke.Read) < 0.001)),
            ];

        return [.. Pasting(ask)];

        static LayoutIntent Offer(string group, string written, string tip, Geometry shape, bool current) =>
            new($"pie.{group.ToLowerInvariant()}.{written}", null, tip) { Group = group, Current = current, Shape = shape };
    }

    /// <summary>Paste, where the caret stands in a label, a value or the title — the only places in the chart words go.</summary>
    private static IEnumerable<LayoutIntent> Pasting(ContentAsk ask)
    {
        if (ask.Caret is { } caret && Places(ask.Part).Any(place => place.Start <= caret && caret <= place.End))
            yield return new LayoutIntent(LayoutVerbs.Paste, null, "Paste");
    }

    // ── What each option is drawn as ────────────────────────────────────────

    /// <summary>A pie and its legend's three lines, the legend where <paramref name="read"/> puts it.</summary>
    private static Geometry Placed(PieLegend read) => read switch
    {
        PieLegend.Left => Drawn(Lines(1, 4.5, 4, 1.5, 3), Disc(11, 8, 4.5)),
        PieLegend.Right => Drawn(Disc(5, 8, 4.5), Lines(11, 4.5, 4, 1.5, 3)),
        PieLegend.Top => Drawn(Keys(1), Disc(8, 10, 5.5)),
        PieLegend.Bottom => Drawn(Disc(8, 6, 5.5), Keys(13.5)),
        _ => Drawn(Disc(8, 8, 7.5), Disc(8, 8, 5), Lines(5.5, 5.75, 5, 1, 2)),
    };

    /// <summary>A pie with a hole <paramref name="share"/> of its radius across.</summary>
    private static Geometry Holed(double share) => share > 0 ? Drawn(Disc(8, 8, 7.5), Disc(8, 8, 7.5 * share)) : Drawn(Disc(8, 8, 7.5));

    /// <summary>A pie of three wedges, the lines between them <paramref name="width"/> thick as the chart writes it.</summary>
    private static Geometry Stroked(double width)
    {
        if (width <= 0) return Drawn(Disc(8, 8, 7.5));

        var gaps = new GeometryGroup();
        foreach (var turn in new[] { 0.0, 120, 240 })
            gaps.Children.Add(new RectangleGeometry(new Rect(8 - (width * 0.275), 0, width * 0.55, 8)) { Transform = new RotateTransform(turn, 8, 8) });

        var cut = new CombinedGeometry(GeometryCombineMode.Exclude, Disc(8, 8, 7.5), gaps);
        cut.Freeze();
        return cut;
    }

    /// <summary>Three lines of a legend, one under another.</summary>
    private static Geometry Lines(double x, double y, double width, double thick, double step) =>
        Drawn(Bar(x, y, width, thick), Bar(x, y + thick + step, width, thick), Bar(x, y + (2 * (thick + step)), width, thick));

    /// <summary>Three keys of a legend, side by side.</summary>
    private static Geometry Keys(double y) => Drawn(Bar(2, y, 3, 1.5), Bar(6.5, y, 3, 1.5), Bar(11, y, 3, 1.5));

    private static Geometry Disc(double x, double y, double radius) => new EllipseGeometry(new Point(x, y), radius, radius);

    private static Geometry Bar(double x, double y, double width, double height) => new RectangleGeometry(new Rect(x, y, width, height));

    /// <summary>Shapes drawn as one, a shape inside another cut out of it.</summary>
    private static Geometry Drawn(params Geometry[] shapes)
    {
        var drawn = new GeometryGroup { FillRule = FillRule.EvenOdd };
        foreach (var shape in shapes) drawn.Children.Add(shape);
        drawn.Freeze();
        return drawn;
    }

    /// <summary>A label holds anything but a quote, which is written <c>#quot;</c>.</summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) => MermaidWriting.Escape(part, caret, text);

    // ── Keys ────────────────────────────────────────────────────────────────

    /// <summary>Enter: in a slice, the next slice under it; at the title, the first slice.</summary>
    private static ContentChange? Entered(ContentEdit edit)
    {
        if (Slice(edit.Part) is { } slice && Where(edit, slice) is not Place.Elsewhere) return After(edit, Line(slice));
        if (Titled(edit.Part) is not { } title) return null;

        return Slices(edit.Root).FirstOrDefault() is { } first ? Before(edit, Line(first)) : After(edit, title, indent: "  ");
    }

    /// <summary>Shift+Enter: in a label, a line broken inside it; anywhere else in the chart, nothing — a line ending would end the slice.</summary>
    private static ContentChange? Broken(ContentEdit edit) =>
        Slice(edit.Part) is { } slice && Where(edit, slice) == Place.Label
            ? ContentChange.Typed(edit.State, Break)
            : ContentChange.Stay(edit.State);

    /// <summary>
    /// A paste in a label, a value or the title: the whole of it given what it holds with the words in, named as words for the parser
    /// to make safe — which, in a value, takes only what still reads as a number. Anywhere else in the chart nothing goes in, because
    /// nowhere else holds words.
    /// </summary>
    private static ContentChange Pasted(ContentEdit edit)
    {
        var state = edit.State;
        var (from, to) = state.HasSelection ? (state.Selection[0].Start, state.Selection[^1].End) : (state.Caret, state.Caret);

        if (state.Selection.Count <= 1)
            foreach (var (part, start, end) in Places(edit.Part))
                if (start <= from && to <= end)
                {
                    var now = state.Source[start..from] + edit.Text + state.Source[to..end];
                    return new ContentChange([ContentWrite.Words(part, start, end - start, now)], from + edit.Text.Length);
                }

        return ContentChange.Stay(state);
    }

    /// <summary>
    /// Backspace and Delete: whole slices picked out, their lines; past the end of a label or a value, or before the start of
    /// one, nothing — what is there holds the slice together.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        if (Chosen(edit.Root, edit.State) is { Count: > 0 } chosen) return Removed(edit, chosen);
        if (edit.State.HasSelection || Slice(edit.Part) is not { } slice) return null;

        var caret = edit.State.Caret;
        var (label, value) = (Label(slice), Value(slice));

        return edit.Kind == EditKind.Deleting
            ? caret == label.End || caret == value?.End ? ContentChange.Stay(edit.State) : null
            : caret == label.Start || caret == value?.Start ? ContentChange.Stay(edit.State) : null;
    }

    /// <summary>Tab: from a label to its value, from a value to the next slice's label — and Shift+Tab back the same way.</summary>
    private static ContentChange? Tabbed(ContentEdit edit, bool forward)
    {
        if (Slice(edit.Part) is not { } slice) return null;

        var slices = Slices(edit.Root);
        var at = slices.IndexOf(slice);
        var place = Where(edit, slice);

        int? to = (forward, place) switch
        {
            (true, Place.Label) when Shown(slice) => Value(slice)?.Start,
            (true, _) when at + 1 < slices.Count => Label(slices[at + 1]).Start,
            (false, Place.Value) => Label(slice).End,
            (false, _) when at > 0 => Shown(slices[at - 1]) ? Value(slices[at - 1])?.End : Label(slices[at - 1]).End,
            _ => null,
        };

        return to is { } caret ? new ContentChange([], caret, null) : null;
    }

    /// <summary>Insert: with whole slices picked out, a slice still to write above the first of them.</summary>
    private static ContentChange? Inserted(ContentEdit edit) =>
        Chosen(edit.Root, edit.State) is [var first, ..] ? Before(edit, Line(first)) : null;

    /// <summary>
    /// Chosen slices carried to another row of the legend: put before it where they came from below it, after it where they came from
    /// above — every slice between given the one that now goes in its place, so no line ending or indent moves. Over a chosen slice,
    /// or anywhere not a slice, they stay where they are.
    /// </summary>
    private static ContentChange? Dropped(ContentEdit edit)
    {
        if (Chosen(edit.Root, edit.State) is not { Count: > 0 } chosen) return null;
        if (Slice(edit.Part) is not { } over || chosen.Contains(over)) return ContentChange.Stay(edit.State);

        var slices = Slices(edit.Root);
        var order = slices.Where(slice => !chosen.Contains(slice)).ToList();
        var at = order.IndexOf(over) + (slices.IndexOf(over) > slices.IndexOf(chosen[0]) ? 1 : 0);
        order.InsertRange(at, chosen);

        var writes = slices.Select((slice, place) => (slice, now: order[place]))
            .Where(pair => pair.slice != pair.now)
            .Select(pair => new ContentWrite(pair.slice, pair.now.Node.Print()))
            .ToList();

        return new ContentChange(writes, After(writes, slices[at].Start) + 1, null);
    }

    /// <summary>
    /// The chosen slices' lines taken out. Where they are every slice there is, the first stays as a slice still to write, so the
    /// chart still says what it is.
    /// </summary>
    private static ContentChange Removed(ContentEdit edit, IReadOnlyList<ContentPart> chosen)
    {
        var slices = Slices(edit.Root);
        var kept = chosen.Count == slices.Count ? chosen[0] : null;
        var writes = new List<ContentWrite>();

        if (kept is not null) writes.Add(new ContentWrite(kept, Blank));

        foreach (var slice in chosen.Where(slice => slice != kept))
            writes.Add(new ContentWrite(Line(slice), string.Empty));

        // The last line has no line ending of its own to take with it, so the one before it gives up its own instead.
        if (kept is null && chosen.Contains(slices[^1]) && !Ends(Line(slices[^1]))
            && slices.LastOrDefault(slice => !chosen.Contains(slice)) is { } before && Ending(Line(before)) is { } ending)
            writes.Add(new ContentWrite(ending, string.Empty));

        var caret = kept is not null ? kept.Start + 1
                  : slices.SkipWhile(slice => slice != chosen[0]).FirstOrDefault(slice => !chosen.Contains(slice)) is { } next
                        ? After(writes, Label(next).Start)
                  : After(writes, Line(chosen[0]).Start);

        return new ContentChange(writes, caret, null);
    }

    /// <summary>A slice still to write, on a line of its own after <paramref name="line"/>, indented like it, the caret in its label.</summary>
    private static ContentChange After(ContentEdit edit, ContentPart line, string? indent = null)
    {
        var said = line.Node.Print();
        var newline = Newline(edit);
        indent ??= Indent(line);

        var written = Ends(line) ? said + indent + Blank + newline : said + newline + indent + Blank;
        var caret = line.Start + (Ends(line) ? said.Length : said.Length + newline.Length) + indent.Length + 1;

        return new ContentChange([new ContentWrite(line, written)], caret, null);
    }

    /// <summary>A slice still to write, on a line of its own before <paramref name="line"/>, indented like it, the caret in its label.</summary>
    private static ContentChange Before(ContentEdit edit, ContentPart line)
    {
        var indent = Indent(line);
        return new ContentChange([new ContentWrite(line, indent + Blank + Newline(edit) + line.Node.Print())], line.Start + indent.Length + 1, null);
    }

    // ── What was chosen ─────────────────────────────────────────────────────

    /// <summary>What the right-click offered and was chosen, written into the front matter.</summary>
    private static ContentChange? Chosen(ContentEdit edit)
    {
        string[]? path = edit.Text.Split('.') switch
        {
            ["pie", "legend", _] => ["config", "pie", "legendPosition"],
            ["pie", "hole", _, ..] => ["config", "pie", "donutHole"],
            ["pie", "stroke", _] => ["config", "themeVariables", "pieStrokeWidth"],
            _ => null,
        };

        if (path is null) return null;

        var value = edit.Text[(edit.Text.IndexOf('.', 4) + 1)..];
        var write = Configured(edit, path, value);

        return new ContentChange([write], After([write], edit.State.Caret), null);
    }

    /// <summary>
    /// The front matter's field at <paramref name="path"/> given <paramref name="value"/>: its value, where it is written; else the
    /// deepest field of the path that is, written again with the rest of the path under it; else the front matter's closing fence,
    /// with the whole path before it; else — no front matter at all — the chart's first line, with front matter holding the path
    /// before it.
    /// </summary>
    private static ContentWrite Configured(ContentEdit edit, string[] path, string value)
    {
        var newline = Newline(edit);

        if (edit.Root.Children.FirstOrDefault(part => part.Kind == MermaidKinds.FrontMatter) is not { } front)
        {
            var first = edit.Root.Children[0];
            return new ContentWrite(first, "---" + newline + Nest(path, value, 0, newline) + newline + "---" + newline + first.Node.Print());
        }

        var at = front;
        var depth = 0;

        while (depth < path.Length && Under(at, path[depth]) is { } line)
        {
            at = line;
            depth++;
        }

        if (depth == 0)
        {
            var fence = front.Children[^1].Children.First(part => part.Kind == MermaidKinds.Fence);
            return new ContentWrite(fence, Nest(path, value, 0, newline) + newline + fence.Node.Print());
        }

        var field = Field(at)!;

        if (depth == path.Length)
            return field.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Value) is { } written
                ? new ContentWrite(written, value)
                : new ContentWrite(field, path[^1] + ": " + value);

        return new ContentWrite(field, field.Node.Print() + newline + Nest(path[depth..], value, Indent(at).Length + 2, newline));
    }

    /// <summary>The line directly under <paramref name="within"/> — the front matter, or a line of it — whose field is <paramref name="key"/>.</summary>
    private static ContentPart? Under(ContentPart within, string key) =>
        within.Children.FirstOrDefault(line => line.Kind == MermaidKinds.Line
            && Field(line)?.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Key) is { } named
            && string.Equals(named.Text, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The field a line of front matter says, or null for one that says none.</summary>
    private static ContentPart? Field(ContentPart line) => line.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Field);

    /// <summary><paramref name="keys"/> as lines of fields each under the one before, from <paramref name="indent"/>, the last saying <paramref name="value"/>.</summary>
    private static string Nest(IReadOnlyList<string> keys, string value, int indent, string newline) =>
        string.Join(newline, keys.Select((key, depth) =>
            new string(' ', indent + (2 * depth)) + key + (depth == keys.Count - 1 ? ": " + value : ":")));

    // ── The chart's tree ────────────────────────────────────────────────────

    /// <summary>Where in a slice the caret is.</summary>
    private enum Place
    {
        Label,
        Value,
        Elsewhere,
    }

    /// <summary>Whether the caret is in <paramref name="slice"/>'s label, its value, or neither.</summary>
    private static Place Where(ContentEdit edit, ContentPart slice)
    {
        var caret = edit.State.Caret;
        var label = Label(slice);

        if (caret >= label.Start && caret <= label.End) return Place.Label;
        return Value(slice) is { } value && caret >= value.Start && caret <= value.End ? Place.Value : Place.Elsewhere;
    }

    /// <summary>Every slice, in the order written.</summary>
    private static List<ContentPart> Slices(ContentPart root) => [.. root.SelfAndDescendants().Where(part => part.Kind == PieKinds.Slice)];

    /// <summary>The slice <paramref name="part"/> is in, or null.</summary>
    private static ContentPart? Slice(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == PieKinds.Slice) return at;

        return null;
    }

    /// <summary>The title <paramref name="part"/> is in, as the line it is written on — or null.</summary>
    private static ContentPart? Titled(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Title)
            {
                for (var line = at; line is not null; line = line.Parent)
                    if (line.Kind == MermaidKinds.Line) return line;

                return null;
            }

        return null;
    }

    /// <summary>The slices whose whole line of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentPart root, EditState state) =>
        [.. Slices(root).Where(slice => state.Selection.Any(range => range.Start <= slice.Start && slice.End <= range.End))];

    /// <summary>Where a slice's label is written: between its quotes.</summary>
    private static (int Start, int End) Label(ContentPart slice)
    {
        var quoted = slice.Children.First(part => part.Kind == MermaidKinds.Quoted);
        var open = quoted.Children.FirstOrDefault(part => part.Role == Roles.Open);
        var close = quoted.Children.LastOrDefault(part => part.Role == Roles.Close);

        return (open?.End ?? quoted.Start, close?.Start ?? quoted.End);
    }

    /// <summary>Where a slice's value is written, or null where it has none.</summary>
    private static (int Start, int End)? Value(ContentPart slice) =>
        slice.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Amount) is { } amount ? (amount.Start, amount.End) : null;

    /// <summary>
    /// Where words go at <paramref name="part"/>: in a slice, between its label's quotes and its value; in the title, what it says —
    /// each with the part that holds it.
    /// </summary>
    private static IEnumerable<(ContentPart Part, int Start, int End)> Places(ContentPart? part)
    {
        if (Slice(part) is { } slice)
        {
            var (start, end) = Label(slice);
            yield return (slice.Children.First(child => child.Kind == MermaidKinds.Quoted), start, end);

            if (slice.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Amount) is { } amount) yield return (amount, amount.Start, amount.End);
        }
        else if (Heading(part) is { } title && title.Part(MermaidRoles.Title) is { } said)
            yield return (title, said.Start, said.End);
    }

    /// <summary>The title <paramref name="part"/> is in, or null.</summary>
    private static ContentPart? Heading(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Title) return at;

        return null;
    }

    /// <summary>Whether the legend shows a slice's value, so the caret has somewhere to go in it.</summary>
    private static bool Shown(ContentPart slice) => slice.Node is PieSliceNode { ValueShown: true };

    /// <summary>The line a slice is written on.</summary>
    private static ContentPart Line(ContentPart slice) => slice.Parent is { Kind: MermaidKinds.Line } line ? line : slice;

    /// <summary>The space a line starts with.</summary>
    private static string Indent(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Text : string.Empty;

    /// <summary>The line ending that closes a line, where it has one.</summary>
    private static ContentPart? Ending(ContentPart line) =>
        line.Children.LastOrDefault(part => part.Kind != MermaidKinds.Line) is { Kind: Kinds.Space } space && space.Text.EndsWith('\n') ? space : null;

    /// <summary>Whether a line is closed by a line ending of its own.</summary>
    private static bool Ends(ContentPart line) => Ending(line) is not null;

    /// <summary>The line ending the chart is written with.</summary>
    private static string Newline(ContentEdit edit) => edit.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Where <paramref name="offset"/> in the document as it stood is, once <paramref name="writes"/> are made.</summary>
    private static int After(IEnumerable<ContentWrite> writes, int offset) =>
        offset + writes.Where(write => write.End <= offset).Sum(write => write.Text.Length - write.Length);
}
