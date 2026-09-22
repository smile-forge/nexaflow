using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

    /// <summary>The most room a picture is given, either way, before it is fitted down into it.</summary>
    private const double Biggest = 600;

    private const string MonoFont = "Consolas";

    /// <summary>How far down the next block goes, and how far right anything has reached.</summary>
    private double _y;
    private double _reach;

    internal MarkdownBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly)
    {
    }

    /// <summary>
    /// Lays <paramref name="markdown"/> out. Never null and never throws: source nothing can be made of comes
    /// back as its own characters, which is what a reader is looking at while they type it anyway.
    /// </summary>
    /// <param name="at">Where this source starts in the document that holds it, for markdown written inside something else.</param>
    /// <param name="reader">
    /// What the document is read by once its blocks are found. A host with something to say about the diagrams
    /// inside it assembles its own; on its own this reads everything and says nothing about how it is pressed.
    /// </param>
    public static Laid Lay(string? markdown, StyleFormat style, double room = double.PositiveInfinity,
                           RawZone? shownAsWritten = null, bool isReadOnly = true, int at = 0,
                           Nexaflow.Markdown.Pipeline.AstPipeline? reader = null)
    {
        var source = markdown ?? string.Empty;
        var read = (reader ?? MarkdownParser.Reader.Then(new Stages.WithNested(style))).Run(MarkdownParser.Read(source));

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

            // Front matter is what a document says about itself rather than anything it says, so it is not on
            // the page at all — until somebody puts the caret in it, when it is the characters they are editing.
            if (part.Kind == MarkdownKinds.FrontMatter && !Shown(part)) continue;

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
            case MarkdownKinds.Item: Item(into, part, x, room, null); return;
            case MarkdownKinds.Table: Tabled(into, part, x, room); return;
            case MarkdownKinds.Fence: Fenced(into, part, x, room); return;
            case MarkdownKinds.Math: Displayed(into, part, x, room); return;

            case MarkdownKinds.Definition: Blocks(into, Body(part), x, room); return;
            case MarkdownKinds.Term: Text(into, Body(part), x, room, Face.Plain with { Bold = true, Ink = Style.DefTerm }); return;
            case MarkdownKinds.Described: Described(into, part, x, room); return;
            case MarkdownKinds.Figure: Figured(into, part, x, room); return;
            case MarkdownKinds.Caption: Captioned(into, part, x, room); return;
            case MarkdownKinds.Footer: Footed(into, part, x, room); return;

            // Passed through rather than drawn: what a browser would make of it is a question this renderer does
            // not answer, so the source is shown quietly instead of pretending to have rendered it.
            case MarkdownKinds.Html: Muted(into, part, x, room); return;

            case MarkdownKinds.Code:
            case MarkdownKinds.Reference: AsWritten(into, part, x, room); return;

            default: Text(into, Body(part), x, room, Face.Plain); return;
        }
    }

    /// <summary>Whether this block is one somebody is being shown the characters of.</summary>
    private bool Shown(ContentPart part) =>
        State.Raw is { } zone && zone.Start < part.End && part.Start < zone.End;

    /// <summary>Whether this block holds blocks, so there is something further in to ask.</summary>
    private static bool Holds(ContentPart part) =>
        part.Kind is MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.List or MarkdownKinds.Item
            or MarkdownKinds.Definition or MarkdownKinds.Described or MarkdownKinds.Figure or MarkdownKinds.Footer;

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

        // The two ranks that open a document and its parts are ruled off under, which is what says a section
        // started rather than a paragraph being loud.
        if (rank > 2) return;

        var thick = Math.Max(1, Style.TextSize / 16);

        _y += Gap * 0.25;

        into.Open(MarkdownPieces.Rule, part, new Point(x, _y));
        into.Draw(new RuleMark(new Rect(0, 0, Math.Max(room, 1), thick), Style.Hr));
        into.Close();

        _y += thick;
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

        var (ink, label) = Calls(part);

        var (tree, size) = Apart(sub =>
        {
            // What it calls itself, said once at the top in the colour that says it. Drawn rather than read,
            // because the reader wrote [!NOTE] and what they meant by it is the word Note.
            if (label is not null) Labelled(sub, part, label, ink, inside);

            Blocks(sub, Body(part), 0, inside);
        });

        var height = size.Height + pad;
        var top = _y;

        into.Open(MarkdownPieces.Block, part, new Point(x, top));
        into.Draw(new WashMark(new Rect(0, 0, Math.Max(room, 1), height), Style.QuoteBg));
        into.Draw(new RuleMark(new Rect(0, 0, bar, height), ink));
        into.Graft(tree, new Point(bar + pad, pad * 0.5));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    /// <summary>
    /// What an alert calls itself, and the colour that says it — an ordinary quotation being the plain case,
    /// which calls itself nothing.
    /// </summary>
    private (Brush Ink, string? Label) Calls(ContentPart part)
    {
        if (part.Kind != MarkdownKinds.Alert) return (Style.TextMuted, null);

        var said = part.Print();
        var opens = said.IndexOf('!');
        var shuts = opens < 0 ? -1 : said.IndexOf(']', opens);
        var name = opens < 0 || shuts < 0 ? string.Empty : said[(opens + 1)..shuts].Trim();

        return name.ToUpperInvariant() switch
        {
            "NOTE" => (Style.Accent, "Note"),
            "TIP" => (Style.Success, "Tip"),
            "IMPORTANT" => (Style.Important, "Important"),
            "WARNING" => (Style.Warning, "Warning"),
            "CAUTION" => (Style.Danger, "Caution"),
            "" => (Style.Accent, null),
            _ => (Style.Accent, char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant()),
        };
    }

    /// <summary>
    /// The word an alert calls itself, set over what it says. It stands for the <c>[!NOTE]</c> that was
    /// typed without being those characters, so pressing it shows them — the same bargain a renumbered list
    /// marker makes, and what lets a search for the word a reader can see land on the marks it was drawn from.
    /// </summary>
    private void Labelled(LayoutBuilder into, ContentPart part, string label, Brush ink, double room)
    {
        var glyphs = Glyphs(label, Face.Plain with { Bold = true, Ink = ink });

        LayoutText.Words(into, glyphs, new Point(0, _y), Math.Max(room, 1), TextAlignment.Left,
                         Marked(part) ?? (ISourcePart)part, MarkdownPieces.Words, maps: false, writes: true, ink: ink);

        _y += glyphs.Height;
    }

    /// <summary>The <c>[!NOTE]</c> itself, where it can be picked out of what the alert was written as.</summary>
    private static SourceSpan? Marked(ContentPart part)
    {
        var said = part.Print();
        var opens = said.IndexOf('[');
        var shuts = opens < 0 ? -1 : said.IndexOf(']', opens);

        return shuts < 0 ? null : new SourceSpan(part.Start + opens, shuts - opens + 1);
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A list, item by item. A numbered list is drawn with the number the item actually is rather than the one that was
    /// typed, because a writer who numbered every line <c>1.</c> meant a list — and what they typed is still there to
    /// be read back and written in.
    /// </summary>
    private void Listed(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var counting = Counted(part);
        var number = counting?.Start ?? 0;
        var first = true;

        foreach (var item in Body(part).Children)
        {
            if (item.Kind != MarkdownKinds.Item) continue;

            if (!first) _y += Gap * 0.35;
            first = false;

            Item(into, item, x, room, counting is null ? null : new MarkdownNumbering(counting.Bullet, number++));
        }
    }

    /// <summary>How this list counts itself, or null where it does not count at all.</summary>
    private static MarkdownNumbering? Counted(ContentPart part) =>
        Body(part).Children
            .Select(child => child.Node.Held as MarkdownNumbering)
            .FirstOrDefault(counting => counting is not null);

    private void Item(LayoutBuilder into, ContentPart item, double x, double room, MarkdownNumbering? counting)
    {
        var indent = Style.TextSize * 1.6;
        var tick = item.Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Task);

        if (tick is not null) Ticked(into, tick, x + indent * 0.2);
        else Marker(into, item, x, indent, counting);

        Blocks(into, item, x + indent, Math.Max(room - indent, 1));
    }

    /// <summary>
    /// The bullet or the number, drawn where the marker was typed and standing for it: pressing it shows the characters
    /// the writer actually put there, which is how a list is renumbered by writing rather than by a command.
    /// </summary>
    private void Marker(LayoutBuilder into, ContentPart item, double x, double indent, MarkdownNumbering? counting)
    {
        var written = item.Children.FirstOrDefault(child => child.Role == Roles.Trivia && !child.Derived);
        var face = Face.Plain with { Ink = Style.TextMuted };
        var glyphs = Glyphs(Numbered(counting), face);

        LayoutText.Words(into, glyphs, new Point(x, _y), indent * 0.85,
                         TextAlignment.Left, written ?? item, MarkdownPieces.Marker,
                         maps: false, writes: written is not null, ink: face.Ink);
    }

    /// <summary>
    /// What an item's marker is drawn as: a bullet where the list does not count, and otherwise its number in
    /// whichever alphabet the list was written in.
    /// </summary>
    private static string Numbered(MarkdownNumbering? counting)
    {
        if (counting is not { Start: > 0 } at) return "\u2022";

        return at.Bullet switch
        {
            'a' => $"{Lettered(at.Start)}.",
            'A' => $"{Lettered(at.Start).ToUpperInvariant()}.",
            'i' => $"{Roman(at.Start).ToLowerInvariant()}.",
            'I' => $"{Roman(at.Start)}.",
            _ => $"{at.Start}.",
        };
    }

    /// <summary>A number in letters, carrying past z the way a spreadsheet's columns do.</summary>
    private static string Lettered(int number)
    {
        var said = string.Empty;

        for (var left = number; left > 0; left = (left - 1) / 26)
            said = (char)('a' + ((left - 1) % 26)) + said;

        return said;
    }

    /// <summary>A number in roman numerals, or itself where there is no such numeral.</summary>
    private static string Roman(int number)
    {
        if (number < 1 || number > 3999) return number.ToString(CultureInfo.InvariantCulture);

        (int Worth, string Sign)[] signs =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];

        var said = new StringBuilder();
        var left = number;

        foreach (var (worth, sign) in signs)
            while (left >= worth)
            {
                said.Append(sign);
                left -= worth;
            }

        return said.ToString();
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
    /// A fenced block, laid by whichever language reads what it names itself and set down where the fence was.
    /// A fence in a language nothing draws falls back to the characters, which is what a reader wanted from it
    /// anyway.
    ///
    /// <para>
    /// Which language that is was settled by a stage and is hanging on the node. All this decides is the room it
    /// gets and where it goes — which is what a builder is for.
    /// </para>
    /// </summary>
    private void Fenced(LayoutBuilder into, ContentPart part, double x, double room)
    {
        if (ContentNesting.Of(part)?.At(part.Part(Roles.Body), room) is { } inset)
        {
            into.Open(MarkdownPieces.Block, part, new Point(x, _y));
            inset.Set(into, default, MarkdownPieces.Block);
            into.Close();

            _y += inset.Height;
            Reached(x + inset.Width);

            return;
        }

        AsWritten(into, part, x, room);
    }

    /// <summary>
    /// A formula on a line of its own: set half as big again as the words around it, centred, with air above
    /// and below so it reads as a thing rather than as a tall line of a paragraph.
    ///
    /// <para>
    /// <strong>Trouble in a formula does not cost it its typesetting.</strong> Maths under a caret is invalid
    /// most of the time — every command is unreadable until its last letter is typed — so a formula that turned
    /// into a box of its source as it was written would spend most of its life as a box of source. What could be
    /// read is set and a wave goes under the rest, which is the reader's own parser's doing and not this one's.
    /// Only a delimiter with nothing between it and its partner falls back, because there is no formula there to
    /// draw and the characters are all there is to put a caret in.
    /// </para>
    /// </summary>
    private void Displayed(LayoutBuilder into, ContentPart part, double x, double room)
    {
        if (ContentNesting.Of(part)?.At(part.Part(Roles.Body), room) is not { } inset)
        {
            AsWritten(into, part, x, room);

            return;
        }

        var air = Style.TextSize * 0.5;
        var left = x + Math.Max(0, (Fits(room) - inset.Width) / 2);

        _y += air;

        into.Open(MarkdownPieces.Block, part, new Point(left, _y));
        inset.Set(into, default, MarkdownPieces.Block);
        into.Close();

        _y += inset.Height + air;
        Reached(left + inset.Width);
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
        // Printed rather than read off the node: a block with no body of its own — a fence or a formula with
        // nothing between its marks — is a branch, and a branch holds no text. What it was written as is the
        // characters under it, which is what Print says and what the caret has to land in.
        var body = part.Part(Roles.Body) ?? part;
        var shown = body.Print().TrimEnd('\n', '\r');
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

    /// <summary>
    /// Source shown quietly, with nothing drawn round it: what a block of raw HTML gets. It is not code and
    /// it is not prose, and the panel a code block sits in would claim it was one.
    /// </summary>
    private void Muted(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var body = part.Part(Roles.Body) ?? part;
        var shown = body.Print().TrimEnd('\n', '\r');

        var face = Face.Plain with { Mono = true, Scale = 0.94, Ink = Style.TextMuted };
        var glyphs = Glyphs(shown.Length == 0 ? " " : shown, face);
        glyphs.MaxTextWidth = Math.Max(1, room);

        LayoutText.Words(into, glyphs, new Point(x, _y), Math.Max(1, room), TextAlignment.Left,
                         new SourceSpan(body.Start, shown.Length), MarkdownPieces.Verbatim,
                         maps: shown.Length > 0, ink: face.Ink);

        _y += glyphs.Height;
        Reached(x + room);
    }

    /// <summary>What a term is explained by, set in from the term so the two read as a pair.</summary>
    private void Described(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var indent = Style.TextSize * 1.8;

        Blocks(into, Body(part), x + indent, Math.Max(room - indent, 1));
    }

    /// <summary>
    /// A figure: whatever was written inside it, set apart on a panel of its own so it reads as one thing
    /// lifted out of the prose rather than as more prose.
    /// </summary>
    private void Figured(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var pad = Style.TextSize * 0.9;
        var inside = Math.Max(room - (pad * 2), 1);

        var (tree, size) = Apart(sub => Blocks(sub, Body(part), 0, inside));

        var height = size.Height + (pad * 1.2);
        var round = Math.Max(2, Style.TextSize * 0.3);
        var top = _y;

        into.Open(MarkdownPieces.Block, part, new Point(x, top));
        into.Draw(new GeometryMark(new RectangleGeometry(new Rect(0, 0, Math.Max(room, 1), height), round, round),
                                   Style.FigureBg, Style.FigureBorder, 1));
        into.Graft(tree, new Point(pad, pad * 0.6));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    /// <summary>What a figure calls itself: smaller, slanted, quiet, and under the middle of what it names.</summary>
    private void Captioned(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var face = Face.Plain with { Italic = true, Scale = Caption, Ink = Style.TextMuted };
        var (tree, size) = Apart(sub => Text(sub, Body(part), 0, room, face));

        into.Open(MarkdownPieces.Block, part, new Point(x + Math.Max((room - size.Width) / 2, 0), _y));
        into.Graft(tree, default);
        into.Close();

        _y += size.Height;
        Reached(x + room);
    }

    /// <summary>
    /// What stands at the foot of the page: a rule to say the document proper has ended, and then whatever
    /// was written, quiet and small.
    /// </summary>
    private void Footed(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var pad = Style.TextSize * 0.3;
        var thick = Math.Max(1, Style.TextSize / 13.5);

        var (tree, size) = Apart(sub => Blocks(sub, Body(part), 0, room));

        var height = thick + (pad * 2) + size.Height;
        var top = _y;

        into.Open(MarkdownPieces.Block, part, new Point(x, top));
        into.Draw(new RuleMark(new Rect(0, 0, Math.Max(room, 1), thick), Style.Hr));
        into.Draw(new WashMark(new Rect(0, thick, Math.Max(room, 1), height - thick), Style.FooterBg));
        into.Graft(tree, new Point(0, thick + pad));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    /// <summary>How big a caption is set, against the reader's own text size.</summary>
    private static double Caption => 12.0 / 13.5;

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
    public const string Picture = "MarkdownPicture";
}

/// <summary>What a press on a piece of a markdown document can mean, beside the shared <see cref="LayoutVerbs"/>.</summary>
public static class MarkdownVerbs
{
    /// <summary>Tick an item off, or take the tick back — <see cref="LayoutIntent.Target"/> says which way.</summary>
    public const string Tick = "tick";
}
