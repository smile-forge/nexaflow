using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A markdown document, on the shared layout tree.
///
/// <para>
/// The same tree a formula, a tune and twenty-six diagrams are laid on, which is the point of it: one caret, one
/// selection, one hit test, one way of writing in place, and a diagram written inside a document is laid by its own
/// builder and grafted where its fence was, rather than being a second kind of thing the document has to host.
/// </para>
/// <para>
/// Nothing here parses. The document arrives read all the way down — blocks found by <see cref="MarkdownParser"/> and
/// each block's body read by the parser its kind names — so every question this asks is asked of the tree, and the
/// marks a writer typed are still in it at the offsets they were typed at. That is what lets a heading be drawn large
/// with its hashes not drawn at all, and a box be drawn where three characters were written.
/// </para>
/// </summary>
public sealed partial class MarkdownBuilder : ContentBuilder
{
    /// <summary>How wide a document is set where nothing says. Prose with nowhere to break is no easier to read than none.</summary>
    private const double Widest = 640;

    private const string MonoFont = "Consolas";

    /// <summary>How far down the next block goes, and how far right anything has reached.</summary>
    private double _y;
    private double _reach;

    internal MarkdownBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly)
    {
    }

    /// <summary>
    /// Lays <paramref name="markdown"/> out. Never null and never throws: source nothing can be made of comes back as
    /// its own characters, which is what a reader is looking at while they type it anyway.
    /// </summary>
    /// <param name="at">Where this source starts in the document that holds it, for markdown written inside something else.</param>
    public static Laid Lay(string? markdown, StyleFormat style, double room = double.PositiveInfinity,
                           RawZone? shownAsWritten = null, bool isReadOnly = true, int at = 0)
    {
        var source = markdown ?? string.Empty;
        var read = MarkdownParser.Reader.Run(MarkdownParser.Read(source));

        return new MarkdownBuilder(ContentReading.Of(read, at),
                                   new EditState(source, 0, null, shownAsWritten), style, isReadOnly).Lay(room);
    }

    /// <inheritdoc/>
    protected override Laid? Build()
    {
        if (Source.Length == 0) return null;

        var into = new LayoutBuilder();

        into.Open(MarkdownPieces.Document, Reading.Root);
        Blocks(into, Reading.Root, 0, Fits(Room));
        into.Close();

        return new Laid(into.Seal(), new Size(Math.Max(_reach, 1), Math.Max(_y, 1)), Trouble());
    }

    /// <summary>Unreadable source shown in a monospaced face, so it reads as the source it is.</summary>
    protected override FormattedText Characters(string text) => Glyphs(text, Face.Plain with { Mono = true });

    // ── Blocks ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every block <paramref name="holder"/> holds, one under the last. Trivia is passed over rather than drawn: the
    /// blank line between two paragraphs is what said they were two, and the space it took is now the gap between them.
    /// </summary>
    private void Blocks(LayoutBuilder into, ContentPart holder, double x, double room)
    {
        var first = true;

        foreach (var part in holder.Children)
        {
            if (part.Derived || part.Role == Roles.Trivia || part.Kind == MarkdownKinds.Task) continue;

            if (!first) _y += Gap;
            first = false;

            Block(into, part, x, room);
        }
    }

    private void Block(LayoutBuilder into, ContentPart part, double x, double room)
    {
        // Somebody is changing the markup rather than the words, so the markup is what is shown. Asked of the
        // innermost block that holds the stretch: a block that holds blocks lets the one being written in answer.
        if (!Holds(part) && Shown(part))
        {
            Sourced(into, part, x, room);

            return;
        }

        switch (part.Kind)
        {
            case MarkdownKinds.Heading: Heading(into, part, x, room); return;
            case MarkdownKinds.Rule: Rule(into, part, x, room); return;
            case MarkdownKinds.Quote:
            case MarkdownKinds.Alert: Quoted(into, part, x, room); return;
            case MarkdownKinds.List: Listed(into, part, x, room); return;
            case MarkdownKinds.Item: Item(into, part, x, room, 0); return;
            case MarkdownKinds.Table: Tabled(into, part, x, room); return;
            case MarkdownKinds.Fence: Fenced(into, part, x, room); return;

            case MarkdownKinds.Code:
            case MarkdownKinds.Html:
            case MarkdownKinds.FrontMatter:
            case MarkdownKinds.Reference: AsWritten(into, part, x, room); return;

            default: Text(into, Body(part), x, room, Face.Plain); return;
        }
    }

    /// <summary>Whether this block is one somebody is being shown the characters of.</summary>
    private bool Shown(ContentPart part) =>
        State.Raw is { } zone && zone.Start < part.End && part.Start < zone.End;

    /// <summary>Whether this block holds blocks, so there is something further in to ask.</summary>
    private static bool Holds(ContentPart part) =>
        part.Kind is MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.List or MarkdownKinds.Item;

    /// <summary>
    /// A block set as the characters it was written with. The same face unreadable source is set in, so it reads as
    /// source at a glance — and the same kind of piece, so anything asking whether a reader is looking at markup has one
    /// question to ask however it came to be showing.
    /// </summary>
    private void Sourced(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var shown = part.Print().TrimEnd('\n', '\r');
        var face = Face.Plain with { Mono = true, Scale = 0.96 };

        var glyphs = Glyphs(shown.Length == 0 ? " " : shown, face);
        glyphs.MaxTextWidth = Math.Max(1, room);

        LayoutText.Words(into, glyphs, new Point(x, _y), Math.Max(1, room), TextAlignment.Left,
                         new SourceSpan(part.Start, shown.Length), LayoutText.SourceKind,
                         maps: shown.Length > 0, ink: Style.Text);

        _y += glyphs.Height;
        Reached(x + room);
    }

    /// <summary>
    /// A heading, set at the size its rank asks for. The hashes are not drawn — they are machinery, and what they say
    /// is said by the size instead — but they are still in the tree at the offsets they were typed at, so nothing has
    /// been lost by not drawing them.
    /// </summary>
    private void Heading(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var rank = Rank(part);
        double[] scales = [1.9, 1.55, 1.3, 1.15, 1.0, 0.92];

        _y += Gap * 0.4;
        Text(into, Body(part), x, room, Face.Plain with
        {
            Bold = true,
            Scale = scales[Math.Clamp(rank, 1, scales.Length) - 1],
            Ink = Style.Heading,
        });
    }

    /// <summary>How deep a heading is, counted off the hashes — or off which character underlined it, where it was written that way.</summary>
    private static int Rank(ContentPart part)
    {
        var text = part.Print().TrimStart();

        var hashes = 0;
        while (hashes < text.Length && text[hashes] == '#') hashes++;

        if (hashes > 0) return Math.Clamp(hashes, 1, 6);

        return text.Contains('=') ? 1 : 2;
    }

    private void Rule(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var thick = Math.Max(1, Style.TextSize / 13.5);

        into.Open(MarkdownPieces.Rule, part, new Point(x, _y + Gap * 0.5));
        into.Draw(new RuleMark(new Rect(0, 0, Math.Max(room, 1), thick), Style.Hr));
        into.Close();

        _y += thick + Gap;
        Reached(x + room);
    }

    /// <summary>
    /// A quotation, or an alert — the same block with something to say about itself. Its contents are laid into a tree
    /// of their own and grafted, which is what lets the bar and the wash be drawn behind them: a mark drawn later is
    /// drawn over, and a wash over its own words would hide them.
    /// </summary>
    private void Quoted(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var bar = Math.Max(2, Style.TextSize * 0.22);
        var pad = Style.TextSize * 0.6;
        var inside = Math.Max(room - bar - pad * 2, 1);

        var (tree, size) = Apart(sub => Blocks(sub, Body(part), 0, inside));
        var height = size.Height + pad;
        var top = _y;

        into.Open(MarkdownPieces.Block, part, new Point(x, top));
        into.Draw(new WashMark(new Rect(0, 0, Math.Max(room, 1), height), Style.QuoteBg));
        into.Draw(new RuleMark(new Rect(0, 0, bar, height), Calls(part)));
        into.Graft(tree, new Point(bar + pad, pad * 0.5));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    /// <summary>What an alert calls itself, in the colour that says it — an ordinary quotation being the plain case.</summary>
    private Brush Calls(ContentPart part)
    {
        if (part.Kind != MarkdownKinds.Alert) return Style.TextMuted;

        var said = part.Print();
        var opens = said.IndexOf('!');
        var shuts = opens < 0 ? -1 : said.IndexOf(']', opens);
        var name = opens < 0 || shuts < 0 ? string.Empty : said[(opens + 1)..shuts].Trim().ToUpperInvariant();

        return name switch
        {
            "TIP" => Style.Success,
            "WARNING" => Style.Warning,
            "CAUTION" => Style.Danger,
            "IMPORTANT" => Style.Important,
            _ => Style.Accent,
        };
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A list, item by item. A numbered list is drawn with the number the item actually is rather than the one that was
    /// typed, because a writer who numbered every line <c>1.</c> meant a list — and what they typed is still there to
    /// be read back and written in.
    /// </summary>
    private void Listed(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var counts = Counted(part);
        var number = 0;
        var first = true;

        foreach (var item in Body(part).Children)
        {
            if (item.Kind != MarkdownKinds.Item) continue;

            if (!first) _y += Gap * 0.35;
            first = false;

            Item(into, item, x, room, counts ? ++number : 0);
        }
    }

    /// <summary>Whether this list numbers itself.</summary>
    private static bool Counted(ContentPart part)
    {
        var text = part.Print().TrimStart();

        return text.Length > 0 && char.IsDigit(text[0]);
    }

    private void Item(LayoutBuilder into, ContentPart item, double x, double room, int number)
    {
        var indent = Style.TextSize * 1.6;
        var tick = item.Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Task);

        if (tick is not null) Ticked(into, tick, x + indent * 0.2);
        else Marker(into, item, x, indent, number);

        Blocks(into, item, x + indent, Math.Max(room - indent, 1));
    }

    /// <summary>
    /// The bullet or the number, drawn where the marker was typed and standing for it: pressing it shows the characters
    /// the writer actually put there, which is how a list is renumbered by writing rather than by a command.
    /// </summary>
    private void Marker(LayoutBuilder into, ContentPart item, double x, double indent, int number)
    {
        var written = item.Children.FirstOrDefault(child => child.Role == Roles.Trivia && !child.Derived);
        var face = Face.Plain with { Ink = Style.TextMuted };
        var glyphs = Glyphs(number > 0 ? $"{number}." : "•", face);

        LayoutText.Words(into, glyphs, new Point(x, _y), indent * 0.85,
                         TextAlignment.Left, written ?? item, MarkdownPieces.Marker,
                         maps: false, writes: written is not null, ink: face.Ink);
    }

    /// <summary>
    /// The box an item is ticked by. Its own piece, carrying the three characters it stands for and what a press on it
    /// means — the block answers that by writing <c>[x]</c> over <c>[ ]</c>, so ticking one is an edit like any other
    /// and takes its place in the undo the reader already has.
    /// </summary>
    private void Ticked(LayoutBuilder into, ContentPart tick, double x)
    {
        var done = tick.Part(MarkdownRoles.Done) is not null;
        var side = Style.TextSize * 0.82;
        var round = side * 0.22;

        into.Open(MarkdownPieces.Tick, tick, new Point(x, _y + Style.TextSize * 0.22));
        into.Draw(new GeometryMark(new RectangleGeometry(new Rect(0, 0, side, side), round, round),
                                   done ? Style.Accent : null, Style.CodeBorder, Math.Max(1, side * 0.09)));

        if (done) into.Draw(new GeometryMark(Ticking(side), null, Style.Text, Math.Max(1.4, side * 0.14)));

        into.Acts(new LayoutActions
        {
            Click = new LayoutIntent(MarkdownVerbs.Tick, done ? "off" : "on",
                                     done ? "Not done after all" : "Done"),
        });

        into.Covers(new Rect(0, 0, side, side));
        into.Close();

        Reached(x + side);
    }

    private static Geometry Ticking(double side)
    {
        var figure = new PathFigure { StartPoint = new Point(side * 0.22, side * 0.52) };
        figure.Segments.Add(new PolyLineSegment([new Point(side * 0.42, side * 0.73), new Point(side * 0.79, side * 0.27)], true));

        var path = new PathGeometry();
        path.Figures.Add(figure);

        return path;
    }

    // ── What another language, or nobody, reads ─────────────────────────────

    /// <summary>
    /// A fenced block, laid by whichever builder reads the language it names itself and grafted where the fence was.
    /// A language nothing here draws falls back to the characters, which is what a reader wanted from it anyway.
    /// </summary>
    private void Fenced(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var language = part.Part(Roles.Name);
        var body = part.Part(Roles.Body);

        if (language is { Length: > 0 } && body is { Length: > 0 }
            && ContentLanguages.Lay(new ContentLink(part, language.Text, body.Text, body.Start), Style, room)
               is { Exists: true } laid)
        {
            into.Open(MarkdownPieces.Block, part, new Point(x, _y));
            into.Graft(laid.Tree);
            into.Close();

            _y += laid.Size.Height;
            Reached(x + laid.Size.Width);

            return;
        }

        AsWritten(into, part, x, room);
    }

    /// <summary>
    /// Source held as written, set in a monospaced face on a panel of its own — a fence in a language nothing draws,
    /// indented code, raw markup, the front matter a document says about itself.
    ///
    /// <para>
    /// The line endings are left off the run rather than trimmed out of it: the part names the characters actually
    /// drawn, so what is set still <em>is</em> the source and the caret still lands a character at a time in it.
    /// </para>
    /// </summary>
    private void AsWritten(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var body = part.Part(Roles.Body) ?? part;
        var shown = body.Text.TrimEnd('\n', '\r');
        var pad = Style.TextSize * 0.55;

        var face = Face.Plain with { Mono = true, Scale = 0.94 };
        var glyphs = Glyphs(shown.Length == 0 ? " " : shown, face);
        glyphs.MaxTextWidth = Math.Max(1, room - pad * 2);

        var height = glyphs.Height + pad * 2;
        var top = _y;

        into.Open(MarkdownPieces.Block, part, new Point(x, top));
        into.Draw(new WashMark(new Rect(0, 0, Math.Max(room, 1), height), Style.CodeBg));
        into.Close();

        _y = top + pad;
        LayoutText.Words(into, glyphs, new Point(x + pad, _y), Math.Max(1, room - pad * 2),
                         TextAlignment.Left, new SourceSpan(body.Start, shown.Length), MarkdownPieces.Verbatim,
                         maps: shown.Length > 0, ink: Style.Text);

        _y = top + height;
        Reached(x + room);
    }

    // ── Keeping count ───────────────────────────────────────────────────────

    /// <summary>The gap between two blocks: what the blank line that separated them is drawn as.</summary>
    private double Gap => Style.TextSize * 0.72;

    private void Reached(double x) => _reach = Math.Max(_reach, x);

    private static double Fits(double room) => double.IsFinite(room) && room > 0 ? room : Widest;

    /// <summary>
    /// Lays something into a tree of its own and hands back how big it came out, so a wash or a bar can be drawn behind
    /// it. Marks are drawn in the order they are made, so anything behind has to be made before what it is behind —
    /// and how tall a block is is not known until it has been laid.
    /// </summary>
    private (LayoutTree Tree, Size Size) Apart(Action<LayoutBuilder> lay)
    {
        var (y, reach) = (_y, _reach);
        (_y, _reach) = (0, 0);

        var into = new LayoutBuilder();
        lay(into);

        var size = new Size(Math.Max(_reach, 1), Math.Max(_y, 0));
        (_y, _reach) = (y, reach);

        return (into.Seal(), size);
    }

    /// <summary>What a block was read into, or the block itself where nothing read it.</summary>
    private static ContentPart Body(ContentPart part) => part.Part(Roles.Body) ?? part;

    private IReadOnlyList<Diagnostic> Trouble() =>
        [.. Reading.Root.SelfAndDescendants()
            .Where(part => part.Node.Trouble is not null)
            .Select(part => new Diagnostic(part.Start, part.Length, DiagnosticSeverity.Error, part.Node.Trouble!))];
}

/// <summary>What a piece of a laid-out markdown document is. Kinds, so a test can say which piece it means.</summary>
public static class MarkdownPieces
{
    public const string Document = "MarkdownDocument";
    public const string Block = "MarkdownBlock";
    public const string Words = "MarkdownWords";
    public const string Marker = "MarkdownMarker";
    public const string Tick = "MarkdownTick";
    public const string Rule = "MarkdownRule";
    public const string Row = "MarkdownRow";
    public const string Cell = "MarkdownCell";
    public const string Verbatim = "MarkdownVerbatim";
}

/// <summary>What a press on a piece of a markdown document can mean, beside the shared <see cref="LayoutVerbs"/>.</summary>
public static class MarkdownVerbs
{
    /// <summary>Tick an item off, or take the tick back — <see cref="LayoutIntent.Target"/> says which way.</summary>
    public const string Tick = "tick";
}
