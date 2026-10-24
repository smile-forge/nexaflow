using System.Globalization;
using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>
/// A markdown document, on the renderer-free layout tree — the WPF builder's layout, made through a medium.
///
/// <para>
/// A pilot of the builder living beside its parser: it reads the same reading, makes the same tree and asks the same questions
/// of it, with every resource it draws with asked of an <see cref="ILayoutMedium"/>. What it does not do is what the WPF builder
/// does only when a document is laid <em>again</em> — set down a block that reads as it did — since that is a graft of the
/// layout already made, the same either way.
/// </para>
/// </summary>
public sealed partial class PortableMarkdownBuilder
{
    private const double Widest = 640;
    private const double Biggest = 600;

    private readonly ContentReading _reading;
    private readonly EditState _state;
    private readonly PortableStyle _style;
    private readonly bool _readOnly;
    private readonly PortableNesting _nesting;
    private readonly ILayoutMedium _medium;
    private readonly Func<ContentPart, PortableLinkLook?> _links;
    private readonly Func<ContentPart, LayoutPicture?> _pictures;

    private double _y;
    private double _reach;
    private double _room = double.PositiveInfinity;

    /// <param name="links">How this showing wants each link to look — what its stage settled, said in the medium's inks.</param>
    /// <param name="pictures">The picture each <c>![alt](where)</c> names, where this showing found one.</param>
    public PortableMarkdownBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, PortableNesting nesting,
                                   ILayoutMedium medium, Func<ContentPart, PortableLinkLook?> links, Func<ContentPart, LayoutPicture?> pictures)
    {
        _reading = reading;
        _state = state;
        _style = style;
        _readOnly = isReadOnly;
        _nesting = nesting;
        _medium = medium;
        _links = links;
        _pictures = pictures;
    }

    private bool Writing => !_readOnly;

    /// <summary>Lays the document out. Never null, and never throws.</summary>
    public PortableLaid Lay(double room = double.PositiveInfinity)
    {
        _room = double.IsNaN(room) || room <= 0 ? double.PositiveInfinity : room;

        try
        {
            return Build() ?? AsSource([]);
        }
        catch (Exception error)
        {
            return AsSource([PortableDiagnostic.Of(_reading.Root, string.Format(CultureInfo.CurrentCulture,
                                                                                 _style.Says("Text.Content.CouldNotSet"), error.Message))]);
        }
    }

    private PortableLaid AsSource(IReadOnlyList<PortableDiagnostic> blamed)
    {
        var face = Face.Plain with { Mono = true };
        return PortableSourceShown.Lay(_reading.Root, blamed, _medium, Typeface(face), Size(face), _style.Text, _style, _room);
    }

    private PortableLaid? Build()
    {
        if (_reading.Root.Length == 0 || _reading.Root.Children.All(child => child.Role == Roles.Trivia || child.Derived)) return null;

        var into = new PortableBuilder(_medium);

        into.Open(PortablePieces.Document, _reading.Root, stops: PortableStops.None);
        Blocks(into, _reading.Root, 0, Fits(_room));
        into.Close();

        return new PortableLaid(into.Seal(), Math.Max(_reach, 1), Math.Max(_y, 1), Trouble());
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    private void Blocks(PortableBuilder into, ContentPart holder, double x, double room)
    {
        ContentPart? above = null;

        var whole = holder.Parent is null;
        var folded = whole ? Folded(holder) : null;

        for (var at = 0; at < holder.Children.Count; at++)
        {
            var part = holder.Children[at];
            if (part.Derived || part.Role == Roles.Trivia || part.Kind is MarkdownKinds.Task or MarkdownKinds.Marker) continue;
            if (part.Kind is MarkdownKinds.FrontMatter or MarkdownKinds.Reference) continue;
            if (folded is not null && folded.Hidden[at]) continue;

            if (above is not null) _y += Between(above, part);
            above = part;

            if (whole) Whole(into, part, x, room);
            else Block(into, part, x, room);

            if (folded is not null && folded.Behind.TryGetValue(at, out var fold)) Fold(into, part, fold.Key, fold.Blocks, x);
        }
    }

    private SectionFolds? Folded(ContentPart document)
    {
        if (_style.Expansion is not { Count: > 0 } said) return null;

        var sections = MarkdownSections.Of(document.Node);
        if (sections.Sections.Count == 0) return null;

        var folds = new SectionFolds(document.Children.Count);

        foreach (var section in sections.All())
        {
            if (section.Anchor is not { } key || !section.Holds || folds.Hidden[section.Heading]) continue;
            if (!said.TryGetValue(key, out var open) || open) continue;

            var blocks = 0;
            for (var at = section.Heading + 1; at < section.End; at++)
            {
                folds.Hidden[at] = true;
                if (document.Children[at] is { Derived: false } part && part.Role != Roles.Trivia
                    && part.Kind is not (MarkdownKinds.FrontMatter or MarkdownKinds.Reference)) blocks++;
            }

            folds.Behind[section.Heading] = (key, blocks);
        }

        return folds.Behind.Count == 0 ? null : folds;
    }

    private void Fold(PortableBuilder into, ContentPart heading, string key, int blocks, double x)
    {
        var says = Glyphs($"+{blocks}", Face.Plain with { Scale = 0.85, Ink = _style.TextMuted });
        var pad = _style.TextSize * 0.4;

        _y += Gap * 0.3;

        var box = _medium.Rectangle(new LayoutRect(0, 0, says.Width + (pad * 2), says.Height + (pad * 0.5)), 3, 3);

        into.Open(PortablePieces.Fold, heading, new LayoutPoint(x, _y), PortableStops.None);
        into.Acts(new PortableActions { Click = new PortableIntent(PortableVerbs.Expand, key, "Unfold this section") });
        into.Draw(new PortableGeometryMark(box, _style.QuoteBg, null));
        into.Occupies(box);
        into.Draw(new PortableTextMark(says, new LayoutPoint(pad, pad * 0.25), _style.TextMuted));
        into.Close();

        _y += box.Bounds.Height;
        Reached(x + box.Bounds.Width);
    }

    private sealed class SectionFolds(int parts)
    {
        public bool[] Hidden { get; } = new bool[parts];

        public Dictionary<int, (string Key, int Blocks)> Behind { get; } = [];
    }

    private double Between(ContentPart above, ContentPart below)
    {
        var after = (above.Node.HeldAs(MarkdownRoles.Typeset) as ParagraphLook)?.After;
        var before = (below.Node.HeldAs(MarkdownRoles.Typeset) as ParagraphLook)?.Before;

        return after is null && before is null ? Gap : ((after ?? 0) + (before ?? 0)) * _style.TextSize;
    }

    /// <summary>One block of the document, as a piece of its own standing at the top of its own frame, its picture kept once painted.</summary>
    private void Whole(PortableBuilder into, ContentPart part, double x, double room)
    {
        var (y, reach) = (_y, _reach);
        (_y, _reach) = (0, 0);

        var apart = Rent();
        apart.Open(PortablePieces.Whole, stops: PortableStops.None, paints: new PortablePaint(Kept: new PortableKept()));
        Block(apart, part, x, room);
        apart.Close();

        var made = Sealed(apart);
        var (height, reached) = (_y, _reach);
        (_y, _reach) = (y, Math.Max(reach, reached));

        into.Graft(made, new LayoutPoint(0, _y));
        _y += height;
    }

    private void Block(PortableBuilder into, ContentPart part, double x, double room)
    {
        switch (part.Kind)
        {
            case MarkdownKinds.Written: Sourced(into, part, x, room); return;
            case MarkdownKinds.Heading: Heading(into, part, x, room); return;
            case MarkdownKinds.Rule: Rule(into, part, x, room); return;
            case MarkdownKinds.Page: Paged(into, part, x, room); return;
            case MarkdownKinds.Quote:
            case MarkdownKinds.Alert: Quoted(into, part, x, room); return;
            case MarkdownKinds.List: Listed(into, part, x, room); return;
            case MarkdownKinds.Item: Item(into, part, x, room, null); return;
            case MarkdownKinds.Table: Tabled(into, part, x, room); return;
            case Kinds.Block: Fenced(into, part, x, room); return;

            case MarkdownKinds.Definition: Blocks(into, Body(part), x, room); return;
            case MarkdownKinds.Term: Text(into, Body(part), x, room, Face.Plain with { Bold = true, Ink = _style.DefTerm }); return;
            case MarkdownKinds.Described: Described(into, part, x, room); return;
            case MarkdownKinds.Figure: Figured(into, part, x, room); return;
            case MarkdownKinds.Caption: Captioned(into, part, x, room); return;
            case MarkdownKinds.Footer: Footed(into, part, x, room); return;

            case MarkdownKinds.Html: Muted(into, part, x, room); return;

            case MarkdownKinds.Code:
            case MarkdownKinds.Reference: AsWritten(into, part, x, room); return;

            default:
                if (part.Node.HeldAs(MarkdownRoles.Typeset) is ParagraphLook look) Paragraph(into, part, x, room, look);
                else Text(into, Body(part), x, room, Face.Plain);

                return;
        }
    }

    private void Paragraph(PortableBuilder into, ContentPart part, double x, double room, ParagraphLook look)
    {
        var em = _style.TextSize;
        var start = Math.Max(look.Indent * em, 0);
        var width = room - start - Math.Max(look.EndIndent * em, 0);

        Text(into, Body(part), x + start, Math.Max(width, em), Face.Plain, look);
    }

    private PortableInset? Nested(ContentPart part, double room, PortableStanding standing) => _nesting(part, room, standing);

    private void Sourced(PortableBuilder into, ContentPart part, double x, double room) =>
        Set(into, part.Part(Roles.Body) is { } body ? (body, body.Text) : null, part, Face.Plain with { Mono = true, Scale = 0.96 }, x, room,
            PortableText.SourceKind, _style.Text);

    private double Set(PortableBuilder into, (ISourcePart Part, string Says)? written, ContentPart holder, Face face, double x, double room,
                       string kind, LayoutInk ink)
    {
        var says = written?.Says ?? string.Empty;
        var glyphs = Glyphs(says.Length == 0 ? " " : says, face);
        glyphs.Bound(Math.Max(1, room), LayoutAlign.Left);

        PortableText.Words(into, glyphs, new LayoutPoint(x, _y), Math.Max(1, room), LayoutAlign.Left, written?.Part ?? holder, kind,
                           maps: says.Length > 0, ink: ink);

        _y += glyphs.Height;
        Reached(x + room);
        return glyphs.Height;
    }

    private static List<(ISourcePart Part, string Says)> Held(ContentPart part)
    {
        if (part.Part(Roles.Body) is not { } body) return [];

        if (body.Children.Count == 0) return [Shown(body, body.Kind == Kinds.Nested ? ContentNested.Own(body.Node) : body.Text)];

        var runs = new List<(ISourcePart Part, string Says)>();

        foreach (var child in body.Children)
            if (child.Role != Roles.Trivia && !child.Derived) runs.Add(Shown(child, child.Text.TrimEnd('\r', '\n')));

        return runs;

        static (ISourcePart, string) Shown(ContentPart held, string text) => (text.Length == held.Length ? held : new PartSlice(held, 0, text.Length), text);
    }

    private static readonly double[] Ranks = [1.9, 1.55, 1.3, 1.15, 1.0, 0.92];

    private void Heading(PortableBuilder into, ContentPart part, double x, double room)
    {
        var rank = Rank(part);

        _y += Gap * 0.4;
        Text(into, Body(part), x, room, Face.Plain with
        {
            Bold = true,
            Scale = Ranks[Math.Clamp(rank, 1, Ranks.Length) - 1],
            Ink = _style.Heading,
        });

        if (rank > 2) return;

        var thick = Math.Max(1, _style.TextSize / 16);

        _y += Gap * 0.25;

        into.Open(PortablePieces.Rule, part, new LayoutPoint(x, _y), PortableStops.None);
        into.Draw(new PortableRuleMark(new LayoutRect(0, 0, Math.Max(room, 1), thick), _style.Hr));
        into.Close();

        _y += thick;
    }

    private static int Rank(ContentPart part) =>
        int.TryParse(part.Node.Said(MarkdownRoles.Rank), CultureInfo.InvariantCulture, out var rank) ? Math.Clamp(rank, 1, 6) : 1;

    private void Rule(PortableBuilder into, ContentPart part, double x, double room)
    {
        var thick = Math.Max(1, _style.TextSize / 13.5);

        into.Open(PortablePieces.Rule, part, new LayoutPoint(x, _y), PortableStops.None);
        into.Draw(new PortableRuleMark(new LayoutRect(0, Gap * 0.5, Math.Max(room, 1), thick), _style.Hr));
        into.Covers(new LayoutRect(0, 0, Math.Max(room, 1), thick + Gap));
        into.Close();

        _y += thick + Gap;
        Reached(x + room);
    }

    private const int PageLines = 3;

    private void Paged(PortableBuilder into, ContentPart part, double x, double room)
    {
        var height = PageLines * Space.Height;

        into.Open(PortablePieces.Page, part, new LayoutPoint(x, _y), PortableStops.None);
        into.Covers(new LayoutRect(0, 0, Math.Max(room, 1), height));
        into.Close();

        _y += height;
        Reached(x + room);
    }

    private void Quoted(PortableBuilder into, ContentPart part, double x, double room)
    {
        var bar = Math.Max(2, _style.TextSize * 4 / 13.5);
        var pad = _style.TextSize * 0.6;
        var inside = Math.Max(room - bar - pad * 2, 1);

        var (ink, label) = Calls(part);

        var (tree, _, height0) = Apart(sub =>
        {
            if (label is not null) Labelled(sub, part, label, ink, inside);

            Blocks(sub, Body(part), 0, inside);
        });

        var height = height0 + pad;
        var top = _y;

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, top), PortableStops.None);
        into.Draw(new PortableWashMark(new LayoutRect(0, 0, Math.Max(room, 1), height), _style.QuoteBg));
        into.Draw(new PortableRuleMark(new LayoutRect(0, 0, bar, height), ink));
        into.Graft(tree, new LayoutPoint(bar + pad, pad * 0.5));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    private (LayoutInk Ink, string? Label) Calls(ContentPart part)
    {
        if (part.Kind != MarkdownKinds.Alert) return (_style.Accent, null);

        var name = Marker(part)?.Part(Roles.Name)?.Text ?? string.Empty;

        return name.ToUpperInvariant() switch
        {
            "NOTE" => (_style.Accent, "Note"),
            "TIP" => (_style.Success, "Tip"),
            "IMPORTANT" => (_style.Important, "Important"),
            "WARNING" => (_style.Warning, "Warning"),
            "CAUTION" => (_style.Danger, "Caution"),
            "" => (_style.Accent, null),
            _ => (_style.Accent, char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant()),
        };
    }

    private void Labelled(PortableBuilder into, ContentPart part, string label, LayoutInk ink, double room)
    {
        var glyphs = Glyphs(label, Face.Plain with { Bold = true, Ink = ink });

        PortableText.Words(into, glyphs, new LayoutPoint(0, _y), Math.Max(room, 1), LayoutAlign.Left,
                           Marker(part) ?? (ISourcePart)part, PortablePieces.Words, maps: false, ink: ink);

        _y += glyphs.Height;
    }

    private static ContentPart? Marker(ContentPart alert) =>
        Body(alert).Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Marker);

    // ── Lists ───────────────────────────────────────────────────────────────

    private void Listed(PortableBuilder into, ContentPart part, double x, double room)
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

    private static MarkdownNumbering? Counted(ContentPart part) =>
        Body(part).Children
            .Select(child => child.Node.Held as MarkdownNumbering)
            .FirstOrDefault(counting => counting is not null);

    private void Item(PortableBuilder into, ContentPart item, double x, double room, MarkdownNumbering? counting)
    {
        var indent = _style.TextSize * 1.6;
        var tick = item.Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Task);

        if (tick is not null) Ticked(into, tick, x + indent * 0.2);
        else Marker(into, item, x, indent, counting);

        Blocks(into, item, x + indent, Math.Max(room - indent, 1));
    }

    private void Marker(PortableBuilder into, ContentPart item, double x, double indent, MarkdownNumbering? counting)
    {
        var written = item.Children.FirstOrDefault(child => child.Role == Roles.Trivia && !child.Derived);
        var face = Face.Plain with { Ink = _style.TextMuted };
        var glyphs = Glyphs(Numbered(counting), face);

        PortableText.Words(into, glyphs, new LayoutPoint(x, _y), indent * 0.85, LayoutAlign.Left, written ?? item, PortablePieces.Marker,
                           maps: false, writes: written is not null, ink: face.Ink);
    }

    private static string Numbered(MarkdownNumbering? counting)
    {
        if (counting is not { Start: > 0 } at) return "•";

        return at.Bullet switch
        {
            'a' => $"{Lettered(at.Start)}.",
            'A' => $"{Lettered(at.Start).ToUpperInvariant()}.",
            'i' => $"{Roman(at.Start).ToLowerInvariant()}.",
            'I' => $"{Roman(at.Start)}.",
            _ => $"{at.Start}.",
        };
    }

    private static string Lettered(int number)
    {
        var said = string.Empty;

        for (var left = number; left > 0; left = (left - 1) / 26)
            said = (char)('a' + ((left - 1) % 26)) + said;

        return said;
    }

    private static readonly (int Worth, string Sign)[] Numerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    private static string Roman(int number)
    {
        if (number < 1 || number > 3999) return number.ToString(CultureInfo.InvariantCulture);

        var said = new StringBuilder();
        var left = number;

        foreach (var (worth, sign) in Numerals)
            while (left >= worth)
            {
                said.Append(sign);
                left -= worth;
            }

        return said.ToString();
    }

    private void Ticked(PortableBuilder into, ContentPart tick, double x)
    {
        var done = tick.Part(MarkdownRoles.Done) is not null;
        var side = _style.TextSize * 0.82;
        var round = side * 0.22;

        into.Open(PortablePieces.Tick, tick, new LayoutPoint(x, _y + _style.TextSize * 0.22));
        into.Draw(new PortableGeometryMark(_medium.Rectangle(new LayoutRect(0, 0, side, side), round, round),
                                           done ? _style.Accent : null, _medium.Stroke(_style.CodeBorder, Math.Max(1, side * 0.09))));

        if (done) into.Draw(new PortableGeometryMark(Ticking(side), null, _medium.Stroke(_style.Text, Math.Max(1.4, side * 0.14))));

        into.Acts(new PortableActions
        {
            Click = new PortableIntent(PortablePieces.TickVerb, done ? "off" : "on",
                                       done ? _style.Says("Text.Markdown.TaskUndone") : _style.Says("Text.Markdown.TaskDone")),
        });

        into.Covers(new LayoutRect(0, 0, side, side));
        into.Close();

        Reached(x + side);
    }

    private LayoutShape Ticking(double side)
    {
        var path = _medium.Outline(evenOdd: true);
        path.Begin(new LayoutPoint(side * 0.22, side * 0.52), filled: true, closed: false);
        path.LineTo(new LayoutPoint(side * 0.42, side * 0.73));
        path.LineTo(new LayoutPoint(side * 0.79, side * 0.27));

        return path.Close();
    }

    // ── What another language, or nobody, reads ─────────────────────────────

    private void Fenced(PortableBuilder into, ContentPart part, double x, double room)
    {
        var nested = Nested(part, room, PortableStanding.Block);

        if (nested is { Draws: true, Laid.ShowsSource: false } inset)
        {
            _borrowed.AddRange(inset.Laid.Trouble);

            into.Open(PortablePieces.Block, part, new LayoutPoint(x, _y), stops: PortableStops.None);
            inset.Set(into, default, PortablePieces.Block);
            into.Close();

            _y += inset.Height;
            Reached(x + inset.Width);

            return;
        }

        if (nested is { Laid.Trouble.Count: > 0 } unread) Unreadable(into, part, x, room, unread.Laid.Trouble);
        else AsWritten(into, part, x, room);
    }

    private void AsWritten(PortableBuilder into, ContentPart part, double x, double room)
    {
        var held = Held(part);
        var pad = _style.TextSize * 0.55;
        var face = Face.Plain with { Mono = true, Scale = 0.94 };

        var glyphs = new IShapedText[held.Count];
        for (var at = 0; at < held.Count; at++)
        {
            glyphs[at] = Glyphs(held[at].Says.Length == 0 ? " " : held[at].Says, face);
            glyphs[at].Bound(Math.Max(1, room - pad * 2), LayoutAlign.Left);
        }

        var marks = held.Count == 0 ? Marks(part) : [];
        var line = Glyphs(" ", face).Height;
        var height = (held.Count > 0 ? glyphs.Sum(set => set.Height) : (marks.Count(Ends) + 1) * line) + pad * 2;
        var top = _y;

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, top), PortableStops.None);
        into.Draw(new PortableWashMark(new LayoutRect(0, 0, Math.Max(room, 1), height), _style.CodeBg));
        into.Close();

        var (across, down) = (x + pad, top + pad);

        for (var at = 0; at < held.Count; at++)
        {
            PortableText.Words(into, glyphs[at], new LayoutPoint(across, down), Math.Max(1, room - pad * 2), LayoutAlign.Left,
                               held[at].Part, PortablePieces.Verbatim, maps: held[at].Says.Length > 0, ink: _style.Text);
            down += glyphs[at].Height;
        }

        foreach (var mark in marks)
        {
            if (Ends(mark))
            {
                (across, down) = (x + pad, down + line);
                continue;
            }

            var set = Glyphs(mark.Text, face);
            PortableText.Words(into, set, new LayoutPoint(across, down), set.Width + 1, LayoutAlign.Left, mark, PortablePieces.Verbatim,
                               maps: true, ink: _style.Text);
            across += set.Width;
        }

        _y = top + height;
        Reached(x + room);

        static bool Ends(ContentPart mark) => mark.Text.Trim('\r', '\n').Length == 0;
    }

    private static List<ContentPart> Marks(ContentPart part) =>
        [.. part.SelfAndDescendants().Where(piece => piece.Children.Count == 0 && !piece.Derived && piece.Length > 0)];

    private void Unreadable(PortableBuilder into, ContentPart part, double x, double room, IReadOnlyList<PortableDiagnostic> why)
    {
        var block = ContentNested.Holders(part.Part(Roles.Body) ?? part).FirstOrDefault() ?? part;
        var face = Face.Plain with { Mono = true, Scale = 0.94 };
        var shown = PortableSourceShown.Lay(block, why, _medium, Typeface(face), Size(face), face.Ink ?? _style.Text, _style, Fits(room));
        _borrowed.AddRange(shown.Trouble);

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, _y), stops: PortableStops.None);
        into.Graft(shown.Tree, default);
        into.Close();
        _y += shown.Height;
        Reached(x + shown.Width);
    }

    private void Muted(PortableBuilder into, ContentPart part, double x, double room)
    {
        var face = Face.Plain with { Mono = true, Scale = 0.94, Ink = _style.TextMuted };
        var held = Held(part);

        if (held.Count == 0) Set(into, null, part, face, x, room, PortablePieces.Verbatim, _style.TextMuted);

        foreach (var run in held) Set(into, run, part, face, x, room, PortablePieces.Verbatim, _style.TextMuted);
    }

    private void Described(PortableBuilder into, ContentPart part, double x, double room)
    {
        var indent = _style.TextSize * 1.8;

        Blocks(into, Body(part), x + indent, Math.Max(room - indent, 1));
    }

    private void Figured(PortableBuilder into, ContentPart part, double x, double room)
    {
        var pad = _style.TextSize * 0.9;
        var inside = Math.Max(room - (pad * 2), 1);

        var (tree, _, laidHeight) = Apart(sub => Blocks(sub, Body(part), 0, inside));

        var height = laidHeight + (pad * 1.2);
        var round = Math.Max(2, _style.TextSize * 0.3);
        var top = _y;

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, top), PortableStops.None);
        into.Draw(new PortableGeometryMark(_medium.Rectangle(new LayoutRect(0, 0, Math.Max(room, 1), height), round, round),
                                           _style.FigureBg, _medium.Stroke(_style.FigureBorder, 1)));
        into.Graft(tree, new LayoutPoint(pad, pad * 0.6));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    private void Captioned(PortableBuilder into, ContentPart part, double x, double room)
    {
        var face = Face.Plain with { Italic = true, Scale = Caption, Ink = _style.TextMuted };
        var (tree, width, height) = Apart(sub => Text(sub, Body(part), 0, room, face));

        into.Open(PortablePieces.Block, part, new LayoutPoint(x + Math.Max((room - width) / 2, 0), _y), PortableStops.None);
        into.Graft(tree, default);
        into.Close();

        _y += height;
        Reached(x + room);
    }

    private void Footed(PortableBuilder into, ContentPart part, double x, double room)
    {
        var pad = _style.TextSize * 0.3;
        var thick = Math.Max(1, _style.TextSize / 13.5);

        var (tree, _, laidHeight) = Apart(sub => Blocks(sub, Body(part), 0, room));

        var height = thick + (pad * 2) + laidHeight;
        var top = _y;

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, top), PortableStops.None);
        into.Draw(new PortableRuleMark(new LayoutRect(0, 0, Math.Max(room, 1), thick), _style.Hr));
        into.Draw(new PortableWashMark(new LayoutRect(0, thick, Math.Max(room, 1), height - thick), _style.FooterBg));
        into.Graft(tree, new LayoutPoint(0, thick + pad));
        into.Close();

        _y = top + height;
        Reached(x + room);
    }

    private static double Caption => 12.0 / 13.5;

    // ── Keeping count ───────────────────────────────────────────────────────

    private double Gap => _style.TextSize * 0.72;

    private void Reached(double x) => _reach = Math.Max(_reach, x);

    private static double Fits(double room) => double.IsFinite(room) && room > 0 ? room : Widest;

    private (PortableTree Tree, double Width, double Height) Apart(Action<PortableBuilder> lay)
    {
        var (y, reach) = (_y, _reach);
        (_y, _reach) = (0, 0);

        var into = Rent();
        lay(into);

        var (width, height) = (Math.Max(_reach, 1), Math.Max(_y, 0));
        (_y, _reach) = (y, reach);

        return (Sealed(into), width, height);
    }

    private PortableBuilder Rent() => _builders.Count > 0 ? _builders.Pop() : new PortableBuilder(_medium);

    private PortableTree Sealed(PortableBuilder built)
    {
        var tree = built.Seal();

        built.Clear();
        _builders.Push(built);

        return tree;
    }

    private readonly Stack<PortableBuilder> _builders = new();

    private static ContentPart Body(ContentPart part) => part.Part(Roles.Body) ?? part;

    private IReadOnlyList<PortableDiagnostic> Trouble() =>
        [.. ContentNested.OwnParts(_reading.Root)
            .Where(part => part.Node.Trouble is not null)
            .Select(part => PortableDiagnostic.Of(part, part.Node.Trouble!)),
         .. _borrowed];

    private readonly List<PortableDiagnostic> _borrowed = [];
}
