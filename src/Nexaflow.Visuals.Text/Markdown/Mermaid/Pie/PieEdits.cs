using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Localization;
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
internal sealed class PieEdits : IContentLanguage, IOnEdit, IOnMove
{
    /// <summary>A slice with nothing written in it yet.</summary>
    private const string Blank = "\"\" : ";

    /// <summary>How a line breaks inside a label.</summary>
    private const string Break = "<br>";



    /// <summary>Where the legend may go: what is offered, the front matter's word for it, and what it is read back as.</summary>
    private static readonly (Func<string> Tip, string Written, PieLegend Read)[] Placings =
    [
        (static () => Str.Get("Text.Pie.Legend.Left"), "left", PieLegend.Left),
        (static () => Str.Get("Text.Pie.Legend.Top"), "top", PieLegend.Top),
        (static () => Str.Get("Text.Pie.Legend.Right"), "right", PieLegend.Right),
        (static () => Str.Get("Text.Pie.Legend.Bottom"), "bottom", PieLegend.Bottom),
        (static () => Str.Get("Text.Pie.Legend.Centre"), "center", PieLegend.Centre),
    ];

    /// <summary>How big the hole may be, as a share of the radius.</summary>
    private static readonly (Func<string> Tip, string Written, double Read)[] Holes =
    [
        (static () => Str.Get("Text.Pie.Hole.None"), "0", 0),
        (static () => Str.Format("Text.Pie.Hole.Share", 0.2), "0.2", 0.2),
        (static () => Str.Format("Text.Pie.Hole.Share", 0.5), "0.5", 0.5),
        (static () => Str.Format("Text.Pie.Hole.Share", 0.8), "0.8", 0.8),
    ];

    /// <summary>How thick the lines between the wedges may be.</summary>
    private static readonly (Func<string> Tip, string Written, double Read)[] Strokes =
    [
        (static () => Str.Get("Text.Pie.Stroke.None"), "0", 0),
        (static () => Str.Get("Text.Pie.Stroke.Thin"), "2", 2),
        (static () => Str.Get("Text.Pie.Stroke.Thick"), "4", 4),
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
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly) return [];

        var config = (ask.Root?.Node as ConfiguredNode<PieConfig>)?.Config ?? PieConfig.Default;
        var medium = ask.Medium;
        var kinds = new HashSet<string>();
        for (var up = ask.Piece; up.Exists; up = up.Parent) kinds.Add(up.Kind);

        if (kinds.Contains(MermaidPiece.Legend))
            return
            [
                Choice("legend", "Text.Pie.Legend", ["config", "pie", "legendPosition"],
                       Placings.Select(placing => (placing.Written, placing.Tip(), Placed(medium, placing.Read), config.Legend == placing.Read))),
            ];

        if (kinds.Contains(PiePiece.Slices) || kinds.Contains(PiePiece.Shares))
            return
            [
                Choice("hole", "Text.Pie.Hole", ["config", "pie", "donutHole"],
                       Holes.Select(hole => (hole.Written, hole.Tip(), Holed(medium, hole.Read), Math.Abs(config.DonutHole - hole.Read) < 0.001))),
                Choice("stroke", "Text.Pie.Stroke", ["config", "themeVariables", "pieStrokeWidth"],
                       Strokes.Select(stroke => (stroke.Written, stroke.Tip(), Stroked(medium, stroke.Read), Math.Abs(config.StrokeWidth - stroke.Read) < 0.001))),
            ];

        return [];

        // Each drawn as what it would make; the one chosen written into the front matter at the field it names.
        static MiniRibbonChoice Choice(string name, string tip, string[] path, IEnumerable<(string Written, string Tip, LayoutShape Shape, bool Current)> options)
        {
            var all = options.ToList();

            return new MiniRibbonChoice("pie." + name, Str.Get(tip), [.. all.Select(option => new MiniRibbonOption(option.Written, option.Tip) { Shape = option.Shape })],
                                        (edit, option) => FrontMatterWriting.Configuring(edit, path, option.Id))
            {
                Current = all.FirstOrDefault(option => option.Current).Written,
            };
        }
    }

    /// <summary>Paste, where the caret stands in a label, a value or the title — the only places in the chart words go.</summary>
    /// <inheritdoc/>
    /// <remarks>
    /// Paste where the caret stands in a label, a value or the title — the only places in the chart words go — as the engine does it.
    /// </remarks>
    public ClipboardOffers Clipboard(ContentAsk ask) =>
        ask.Caret is { } caret && Places(ask.Part).Any(place => place.Start <= caret && caret <= place.End) ? ClipboardOffers.Paste : ClipboardOffers.None;

    // ── What each option is drawn as ────────────────────────────────────────

    /// <summary>A pie and its legend's three lines, the legend where <paramref name="read"/> puts it.</summary>
    private static LayoutShape Placed(ILayoutMedium medium, PieLegend read) => read switch
    {
        PieLegend.Left => Drawn(medium, Lines(medium, 1, 4.5, 4, 1.5, 3), Disc(medium, 11, 8, 4.5)),
        PieLegend.Right => Drawn(medium, Disc(medium, 5, 8, 4.5), Lines(medium, 11, 4.5, 4, 1.5, 3)),
        PieLegend.Top => Drawn(medium, Keys(medium, 1), Disc(medium, 8, 10, 5.5)),
        PieLegend.Bottom => Drawn(medium, Disc(medium, 8, 6, 5.5), Keys(medium, 13.5)),
        _ => Drawn(medium, Disc(medium, 8, 8, 7.5), Disc(medium, 8, 8, 5), Lines(medium, 5.5, 5.75, 5, 1, 2)),
    };

    /// <summary>A pie with a hole <paramref name="share"/> of its radius across.</summary>
    private static LayoutShape Holed(ILayoutMedium medium, double share) =>
        share > 0 ? Drawn(medium, Disc(medium, 8, 8, 7.5), Disc(medium, 8, 8, 7.5 * share)) : Drawn(medium, Disc(medium, 8, 8, 7.5));

    /// <summary>A pie of three wedges, the lines between them <paramref name="width"/> thick as the chart writes it.</summary>
    private static LayoutShape Stroked(ILayoutMedium medium, double width)
    {
        if (width <= 0) return Drawn(medium, Disc(medium, 8, 8, 7.5));

        LayoutShape[] gaps =
        [
            .. new[] { 0.0, 120, 240 }.Select(turn =>
                medium.Transformed(Bar(medium, 8 - (width * 0.275), 0, width * 0.55, 8), LayoutMatrix.Rotation(turn, 8, 8))),
        ];

        return medium.Combine(Disc(medium, 8, 8, 7.5), medium.Group(gaps), ShapeCombine.Exclude);
    }

    /// <summary>Three lines of a legend, one under another.</summary>
    private static LayoutShape Lines(ILayoutMedium medium, double x, double y, double width, double thick, double step) =>
        Drawn(medium, Bar(medium, x, y, width, thick), Bar(medium, x, y + thick + step, width, thick), Bar(medium, x, y + (2 * (thick + step)), width, thick));

    /// <summary>Three keys of a legend, side by side.</summary>
    private static LayoutShape Keys(ILayoutMedium medium, double y) =>
        Drawn(medium, Bar(medium, 2, y, 3, 1.5), Bar(medium, 6.5, y, 3, 1.5), Bar(medium, 11, y, 3, 1.5));

    private static LayoutShape Disc(ILayoutMedium medium, double x, double y, double radius) => medium.Ellipse(new LayoutPoint(x, y), radius, radius);

    private static LayoutShape Bar(ILayoutMedium medium, double x, double y, double width, double height) => medium.Rectangle(new LayoutRect(x, y, width, height));

    /// <summary>Shapes drawn as one, a shape inside another cut out of it.</summary>
    private static LayoutShape Drawn(ILayoutMedium medium, params LayoutShape[] shapes) => medium.Group(shapes, evenOdd: true);

    /// <summary>A label holds anything but what would end it, which a backslash keeps (<see cref="PieParser.Escaped"/>).</summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (PieParser.LabelOf(part) is not { } label || PieParser.Opened(label) is not { } quote) return null;

        var kept = PieParser.Escaped(text, quote);
        return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
    }

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
                    var now = state.Document.Slice(start, from - start) + edit.Text + state.Document.Slice(to, end - to);
                    return new ContentChange([ContentWrite.Words(part, start, end - start, now)], from + edit.Text.Length);
                }

        return ContentChange.Stay(state);
    }

    /// <summary>
    /// Backspace and Delete: whole slices picked out, their lines; past the end of a label or a value, or before the start of
    /// one, nothing — what is there holds the slice together; over a backslash and what it keeps in a label, the two together.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        if (Chosen(edit.Root, edit.State) is { Count: > 0 } chosen) return Removed(edit, chosen);
        if (edit.State.HasSelection || Slice(edit.Part) is not { } slice) return null;

        var caret = edit.State.Caret;
        var (label, value) = (Label(slice), Value(slice));

        if (edit.Kind == EditKind.Deleting ? caret == label.End || caret == value?.End : caret == label.Start || caret == value?.Start)
            return ContentChange.Stay(edit.State);

        return Unkept(edit);
    }

    /// <summary>Backspace or Delete over a backslash and what it keeps in a label: the two together, since neither says anything without the other.</summary>
    private static ContentChange? Unkept(ContentEdit edit)
    {
        if (edit.Part is not { } part || PieParser.LabelOf(part) is not { } label) return null;

        var caret = edit.State.Caret;
        var erasing = edit.Kind == EditKind.Erasing;

        foreach (var escape in label.SelfAndDescendants())
            if (escape.Kind == PieKinds.Escape && (erasing ? escape.Start < caret && caret <= escape.End : escape.Start <= caret && caret < escape.End))
                return ContentChange.Write(escape.Start, escape.Length, string.Empty, escape.Start);

        return null;
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

    /// <summary>A pie carries what is picked out in it, as the engine carries it: whole slices.</summary>
    public ContentLift Lift(ContentGrip grip) => ContentLift.Selection;

    /// <summary>
    /// Slices carried and let go on another: that one and the ones carried swap places, and the rest shift over.
    ///
    /// <para>
    /// The stretches a move names are the slices' own lines, each given the line of whichever slice now stands in its
    /// place — so the order changes and not one character of any slice does. Nothing is written where a slice is let go on
    /// itself, or on one of the slices being carried.
    /// </para>
    /// </summary>
    public ContentChange? Move(ContentMove move)
    {
        if (!move.Holds) return null;
        if (move.Carrying().Where(part => part.Kind == PieKinds.Slice).ToList() is not { Count: > 0 } chosen) return null;
        if (Slice(move.Part) is not { } over || chosen.Contains(over)) return ContentChange.Stay(move.State);

        var slices = Slices(move.Root);
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
    private static string Newline(ContentEdit edit) => edit.State.Document.LineEnding;

    /// <summary>Where <paramref name="offset"/> in the document as it stood is, once <paramref name="writes"/> are made.</summary>
    private static int After(IEnumerable<ContentWrite> writes, int offset) =>
        offset + writes.Where(write => write.End <= offset).Sum(write => write.Text.Length - write.Length);
}
