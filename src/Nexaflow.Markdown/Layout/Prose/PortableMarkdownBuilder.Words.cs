using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>Setting the words of a block: what each construct is drawn as, and where the lines break.</summary>
public sealed partial class PortableMarkdownBuilder
{
    /// <summary>How a stretch is set. Everything about the drawing; nothing about the source.</summary>
    private readonly record struct Face
    {
        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool Strike { get; init; }

        public bool Underline { get; init; }

        public bool Dotted { get; init; }

        /// <summary>The family it is set in, where it is not the reading one.</summary>
        public string? Font { get; init; }

        public bool Mono { get; init; }

        public bool Capitals { get; init; }

        public double Scale { get; init; }

        public double Lift { get; init; }

        public LayoutInk? Ink { get; init; }

        public LayoutInk? Wash { get; init; }

        public static Face Plain => new() { Scale = 1 };
    }

    private readonly record struct Run(string Text, ContentPart Part, Face Face, bool Maps, PortableIntent? Act = null,
                                       PortableInset? Inset = null, bool Tab = false);

    private void Text(PortableBuilder into, ContentPart words, double x, double room, Face face, ParagraphLook? look = null)
    {
        var runs = _runs;
        runs.Clear();
        Gather(words, face, runs);

        if (runs.Count == 0)
        {
            _y += Glyphs(" ", face).Height;
            Reached(x);

            return;
        }

        Lines(into, runs, x, Math.Max(room, 1), look);
        runs.Clear();
    }

    private readonly List<Run> _runs = [];
    private readonly List<(int Run, int Start, int End)> _line = [];
    private readonly List<(Run Run, IShapedText? Glyphs)> _groups = [];
    private readonly List<double> _tops = [];

    // ── What each construct is set as ───────────────────────────────────────

    private void Gather(ContentPart part, Face face, List<Run> runs)
    {
        if (part.Derived) return;

        if (MarkdownStandIns.Decoded(part.Node) is { } drawn)
        {
            runs.Add(new Run(face.Capitals ? Capitalised(drawn) : drawn, part, face, Maps: false));
            return;
        }

        switch (part.Kind)
        {
            case MarkdownKinds.Strong: Inside(part, face with { Bold = true }, runs); return;
            case MarkdownKinds.Emphasis: Inside(part, face with { Italic = true }, runs); return;
            case MarkdownKinds.Strike: Inside(part, face with { Strike = true }, runs); return;
            case MarkdownKinds.Insert: Inside(part, face with { Underline = true }, runs); return;
            case MarkdownKinds.Mark: Inside(part, face with { Wash = Style.Marked }, runs); return;

            case MarkdownKinds.Sub: Inside(part, face with { Scale = face.Scale * 0.72, Lift = 0.22 }, runs); return;
            case MarkdownKinds.Sup: Inside(part, face with { Scale = face.Scale * 0.72, Lift = -0.34 }, runs); return;

            case MarkdownKinds.Styled when part.Node.HeldAs(MarkdownRoles.Typeset) is TextLook look:
                Inside(part, Typeset(face, look), runs);
                return;

            case MarkdownKinds.Encoded:
                if (part.Node.HeldAs(MarkdownRoles.Decoded) is string { Length: > 0 } decoded)
                    runs.Add(new Run(face.Capitals ? Capitalised(decoded) : decoded, part, face, Maps: false));
                return;

            case MarkdownKinds.Tab:
                runs.Add(new Run(string.Empty, part, face, Maps: false, Tab: true));
                return;

            case MarkdownKinds.Code when part.Part(Roles.Body) is { } code:
            {
                var set = face with { Mono = true, Scale = face.Scale * 0.94, Ink = Style.Accent, Wash = Style.CodeBg };

                if (code.Children.Count == 0) runs.Add(new Run(code.Text, code, set, Maps: true));
                else
                    foreach (var written in code.Children)
                        if (written.Role != Roles.Trivia) runs.Add(new Run(written.Text, written, set, Maps: true));

                return;
            }

            case MarkdownKinds.Image:
                Pictured(part, face, runs);
                return;

            case Kinds.Link:
                Linked(part, face, runs);
                return;

            case MarkdownKinds.Citation:
                Inside(part, face with { Scale = face.Scale * 0.85, Lift = -0.34, Ink = Style.Citation }, runs);
                return;

            case MarkdownKinds.Abbreviation when part.Part(Roles.Body) is { } word:
                runs.Add(new Run(word.Text, part, face with { Dotted = true, Ink = Style.Text }, Maps: true));
                return;

            case MarkdownKinds.Html:
                return;

            case MarkdownKinds.Break:
                if (MarkdownLineEnds.Closes(part)) return;
                runs.Add(new Run(part.Role == MarkdownRoles.Hard ? string.Empty : " ", part, face, Maps: false));
                return;

            case MarkdownKinds.Task:
                return;

            case Kinds.Block:
            case MarkdownKinds.Formula:
                Formula(part, face, runs);
                return;
        }

        if (part.Children.Count > 0)
        {
            Inside(part, face, runs);

            return;
        }

        if (part.Role == Roles.Silent) return;

        if (part.Role is Roles.Open or Roles.Close or Roles.Name or Roles.Trivia)
        {
            if (part.Text.Contains('\n') && !MarkdownLineEnds.Closes(part)) runs.Add(new Run(" ", part, face, Maps: false));

            return;
        }

        if (part.Kind == Kinds.Space && part.Text.Contains('\n') && MarkdownLineEnds.Closes(part)) return;

        if (part.Text.Length == 0) return;

        runs.Add(face.Capitals
            ? new Run(Capitalised(Flowed(part.Text)), part, face, Maps: false)
            : new Run(Flowed(part.Text), part, face, Maps: true));
    }

    private void Inside(ContentPart part, Face face, List<Run> runs)
    {
        foreach (var child in part.Children) Gather(child, face, runs);
    }

    private Face Typeset(Face face, TextLook look)
    {
        var scale = face.Scale * (look.Scale > 0 ? look.Scale : 1);

        return face with
        {
            Bold = face.Bold || look.Bold,
            Italic = face.Italic || look.Italic,
            Underline = face.Underline || look.Underline,
            Strike = face.Strike || look.Strike,
            Capitals = face.Capitals || look.Capitals,
            Font = look.Font is { Length: > 0 } named ? $"{named}, {Style.TextFont}" : face.Font,
            Scale = look.Script == TextScript.None ? scale : scale * 0.72,
            Lift = look.Script switch
            {
                TextScript.Sub => 0.22,
                TextScript.Super => -0.34,
                _ => face.Lift,
            },
            Ink = look.Ink is { } ink ? Inked(ink) : face.Ink,
            Wash = look.Wash is { } wash ? Inked(wash) : face.Wash,
        };
    }

    /// <summary>A colour a look gives, as <c>0xAARRGGBB</c>, as the medium draws it — made once for each.</summary>
    private LayoutInk Inked(uint argb)
    {
        if (_inks.TryGetValue(argb, out var made)) return made;

        return _inks[argb] = Medium.Ink(new LayoutColor((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    private readonly Dictionary<uint, LayoutInk> _inks = [];

    private static string Capitalised(string text) => text.ToUpper(CultureInfo.CurrentCulture);

    private void Linked(ContentPart part, Face face, List<Run> runs)
    {
        var where = MarkdownLinks.Goes(part.Node);
        var says = part.Part(MarkdownRoles.Title)?.Text;
        var act = where is { Length: > 0 } ? new PortableIntent(PortableVerbs.Navigate, where, says) : null;

        var look = LinkOf(part);
        var linked = face with { Underline = look?.Underline ?? true, Ink = look?.Ink ?? Style.Accent };
        var body = part.Part(Roles.Body);

        if (look is { Says.Length: > 0 } && act is { } meant) act = meant with { Tip = look.Says };

        if (body is null || body.Length == 0)
        {
            if (where is null) Inside(part, linked, runs);
            else runs.Add(new Run(where, part, linked, Maps: where.Length > 0, Act: act));

            return;
        }

        if (look is { Before.Length: > 0 } opens)
            runs.Add(new Run(opens.Before + " ", part, Marked(linked, look), Maps: false, Act: act));

        var at = runs.Count;
        Gather(body, linked, runs);

        for (var index = at; index < runs.Count; index++) runs[index] = runs[index] with { Act = act };

        if (look is { After.Length: > 0 } shuts)
            runs.Add(new Run(" " + shuts.After, part, Marked(linked, look), Maps: false, Act: act));
    }

    private static Face Marked(Face linked, PortableLinkLook look) => linked with { Underline = false, Font = look.MarkFont };

    private void Formula(ContentPart part, Face face, List<Run> runs)
    {
        var nested = Nested(part, double.PositiveInfinity, PortableStanding.Inline);

        if (nested is { Draws: true, Laid.ShowsSource: false } set)
        {
            _borrowed.AddRange(set.Laid.Trouble);
            runs.Add(new Run(string.Empty, part, face, Maps: false, Inset: set));

            return;
        }

        var mono = face with { Mono = true, Scale = face.Scale * 0.94, Ink = Style.Accent };

        if (nested is { Laid.Trouble.Count: > 0 } unread)
        {
            var shown = PortableSourceShown.Lay(part, unread.Laid.Trouble, Medium, Typeface(mono), Size(mono), mono.Ink ?? Style.Text, Style);
            _borrowed.AddRange(shown.Trouble);
            runs.Add(new Run(string.Empty, part, face, Maps: false, Inset: new PortableInset(shown)));

            return;
        }

        foreach (var written in part.SelfAndDescendants().Where(piece => piece.Children.Count == 0 && !piece.Derived && piece.Length > 0))
            runs.Add(new Run(written.Text, written, mono, Maps: true));
    }

    private void Pictured(ContentPart part, Face face, List<Run> runs)
    {
        if (PictureOf(part) is not { } picture)
        {
            Linked(part, face, runs);

            return;
        }

        var box = Fitted(picture, MarkdownPictureSize.Of(part.Node));
        var into = new PortableBuilder(Medium);

        into.Open(PortablePieces.Picture, part);
        into.Draw(new PortablePictureMark(picture, box));
        into.Close();

        runs.Add(new Run(string.Empty, part, face, Maps: false, Inset: new PortableInset(new PortableLaid(into.Seal(), box.Width, box.Height, []))));
    }

    private static LayoutRect Fitted(LayoutPicture picture, MarkdownPictureSize? asked)
    {
        var (wide, tall) = (picture.Width, picture.Height);
        var known = !double.IsNaN(wide) && !double.IsNaN(tall) && wide > 0 && tall > 0;

        if (asked is { } size)
        {
            var high = size.Height ?? (known ? size.Width * tall / wide : size.Width);

            return new LayoutRect(0, 0, Math.Max(1, size.Width), Math.Max(1, high));
        }

        if (!known) return new LayoutRect(0, 0, Biggest, Biggest);

        var scale = Math.Min(1, Math.Min(Biggest / wide, Biggest / tall));

        return new LayoutRect(0, 0, Math.Max(1, wide * scale), Math.Max(1, tall * scale));
    }

    private static string Flowed(string text) =>
        text.Contains('\n') || text.Contains('\r') ? text.Replace('\n', ' ').Replace('\r', ' ') : text;

    // ── Where the lines break ───────────────────────────────────────────────

    private void Lines(PortableBuilder into, List<Run> runs, double x, double room, ParagraphLook? look = null)
    {
        var line = _line;
        line.Clear();
        var width = 0.0;

        var first = true;
        var hang = Math.Max((look?.FirstLine ?? 0) * Style.TextSize, -x);
        var align = look?.Align ?? ParagraphAlign.Start;

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];

            if (run.Part.Kind == MarkdownKinds.Break && run.Text.Length == 0)
            {
                Broke(closes: true);

                continue;
            }

            if (run.Tab)
            {
                var advance = Tabbed(Starts() + width);

                if (width > 0 && width + advance > Room())
                {
                    Broke(closes: false);
                    advance = Tabbed(Starts());
                }

                runs[index] = run with { Inset = TabRoom(run.Part, advance) };
                line.Add((index, 0, 0));
                width += advance;

                continue;
            }

            if (run.Inset is { } inset)
            {
                Place(index, 0, 0, inset.Width, inset.Width);

                continue;
            }

            var measure = Medium.Measuring(Typeface(run.Face), Size(run.Face));
            var text = run.Text;
            var at = 0;

            while (at < text.Length)
            {
                var end = at;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                if (end == at) end++;
                while (end < text.Length && char.IsWhiteSpace(text[end])) end++;

                var (inked, advance) = measure.Of(text.AsSpan(at, end - at));
                Place(index, at, end, inked, advance);
                at = end;
            }
        }

        Row(into, runs, line, x + Starts(), Room(), align, closes: true);

        double Starts() => first ? hang : 0;

        double Room() => Math.Max(room - Starts(), 1);

        void Broke(bool closes)
        {
            Row(into, runs, line, x + Starts(), Room(), align, closes);
            width = 0;
            first = false;
        }

        double Tabbed(double at)
        {
            var stops = look ?? Untabbed;
            var em = Style.TextSize;
            var indent = stops.Indent * em;

            return Math.Max((stops.NextStop((indent + at) / em) * em) - indent - at, 0);
        }

        void Place(int index, int start, int end, double inked, double advance)
        {
            if (width > 0 && width + inked > Room()) Broke(closes: false);

            line.Add((index, start, end));
            width += advance;
        }
    }

    private static readonly ParagraphLook Untabbed = new();

    private PortableInset TabRoom(ContentPart part, double width)
    {
        var into = new PortableBuilder(Medium);

        into.Open(PortablePieces.Block, part, stops: PortableStops.None);
        into.Covers(new LayoutRect(0, 0, Math.Max(width, 0), 1));
        into.Close();

        return new PortableInset(new PortableLaid(into.Seal(), Math.Max(width, 0), 0, []));
    }

    private void Row(PortableBuilder into, IReadOnlyList<Run> runs, List<(int Run, int Start, int End)> line, double x,
                     double room, ParagraphAlign align, bool closes)
    {
        if (line.Count == 0) return;

        var groups = _groups;
        groups.Clear();
        var at = 0;

        while (at < line.Count)
        {
            var (index, start, end) = line[at++];
            var run = runs[index];

            if (run.Inset is not null)
            {
                groups.Add((run, null));

                continue;
            }

            while (at < line.Count && line[at].Run == index) end = line[at++].End;

            var cut = start == 0 && end == run.Text.Length ? run.Text : run.Text[start..end];

            if (align == ParagraphAlign.Justified && !closes) Worded(run, cut, groups);
            else groups.Add((run, Glyphs(cut, run.Face)));
        }

        line.Clear();

        var words = Space;
        var baseline = double.NegativeInfinity;
        foreach (var (_, glyphs) in groups) baseline = Math.Max(baseline, glyphs?.Baseline ?? words.Baseline);

        var middle = baseline - words.Baseline + (words.Height / 2);

        var tops = _tops;
        tops.Clear();
        var over = 0.0;

        for (var index = 0; index < groups.Count; index++)
        {
            var (run, glyphs) = groups[index];

            tops.Add(glyphs is not null
                ? baseline - glyphs.Baseline + (run.Face.Lift * Style.TextSize)
                : middle - (run.Inset!.Height / 2));

            over = Math.Min(over, tops[index]);
        }

        var (shift, widen) = Aligned(groups, room, align, closes);

        var height = 0.0;
        var cursor = x + shift;
        var reach = x;

        for (var index = 0; index < groups.Count; index++)
        {
            var (run, glyphs) = groups[index];
            var top = tops[index] - over;

            Set(into, run, glyphs, cursor, _y + top);

            reach = cursor + (glyphs?.Width ?? run.Inset!.Width);
            cursor += glyphs?.WidthWithSpace ?? run.Inset!.Width;
            height = Math.Max(height, top + (glyphs?.Height ?? run.Inset!.Height));

            if (widen > 0 && index < groups.Count - 1 && Spaced(glyphs)) cursor += widen;
        }

        groups.Clear();
        _y += height;
        Reached(reach);
    }

    private void Worded(Run run, string cut, List<(Run Run, IShapedText? Glyphs)> groups)
    {
        var at = 0;

        while (at < cut.Length)
        {
            var end = at;
            while (end < cut.Length && !char.IsWhiteSpace(cut[end])) end++;
            while (end < cut.Length && char.IsWhiteSpace(cut[end])) end++;
            if (end == at) end++;

            groups.Add((run, Glyphs(at == 0 && end == cut.Length ? cut : cut[at..end], run.Face)));
            at = end;
        }
    }

    private static (double Shift, double Widen) Aligned(List<(Run Run, IShapedText? Glyphs)> groups, double room,
                                                        ParagraphAlign align, bool closes)
    {
        if (align == ParagraphAlign.Start || groups.Count == 0) return (0, 0);

        var wide = 0.0;
        for (var index = 0; index < groups.Count; index++)
        {
            var (run, glyphs) = groups[index];
            wide += index < groups.Count - 1
                ? glyphs?.WidthWithSpace ?? run.Inset!.Width
                : glyphs?.Width ?? run.Inset!.Width;
        }

        var left = Math.Max(room - wide, 0);

        switch (align)
        {
            case ParagraphAlign.Centre: return (left / 2, 0);
            case ParagraphAlign.End: return (left, 0);
        }

        if (closes) return (0, 0);

        var spaces = 0;
        for (var index = 0; index < groups.Count - 1; index++)
            if (Spaced(groups[index].Glyphs)) spaces++;

        return (0, spaces > 0 ? left / spaces : 0);
    }

    private static bool Spaced(IShapedText? glyphs) =>
        glyphs is { Text.Length: > 0 } words && char.IsWhiteSpace(words.Text[^1]);

    private void Set(PortableBuilder into, Run run, IShapedText? glyphs, double x, double top)
    {
        if (run.Inset is { } inset)
        {
            into.Open(PortablePieces.Block, run.Part, new LayoutPoint(x, top));
            inset.Set(into, default, PortablePieces.Block);
            into.Close();

            return;
        }

        if (glyphs is null) return;

        if (run.Face.Wash is { } wash)
        {
            var pad = Style.TextSize * 0.12;

            into.Open(PortablePieces.Block, run.Part, new LayoutPoint(x - pad, top));
            into.Draw(new PortableWashMark(new LayoutRect(0, 0, glyphs.Width + (pad * 2), glyphs.Height), wash));
            into.Close();
        }

        if (run.Act is not { } act)
        {
            PortableText.Words(into, glyphs, new LayoutPoint(x, top), double.PositiveInfinity, LayoutAlign.Left,
                               run.Part, PortablePieces.Words, maps: run.Maps, writes: !run.Maps, ink: run.Face.Ink ?? Style.Text);

            return;
        }

        into.Open(PortablePieces.Block, run.Part, new LayoutPoint(x, top));
        into.Acts(new PortableActions { Click = act });

        PortableText.Words(into, glyphs, default, double.PositiveInfinity, LayoutAlign.Left,
                           run.Part, PortablePieces.Words, maps: run.Maps, writes: !run.Maps, ink: run.Face.Ink ?? Style.Text);

        into.Close();
    }

    // ── Type ────────────────────────────────────────────────────────────────

    /// <summary>Words set in <paramref name="face"/> by the medium, ruled as the face says.</summary>
    private IShapedText Glyphs(string text, Face face)
    {
        var lines = (face.Strike ? TextLines.Through : TextLines.None)
                    | (face.Underline ? TextLines.Under : TextLines.None)
                    | (face.Dotted ? TextLines.Dotted : TextLines.None);

        return Medium.Shape(text, Typeface(face), Size(face), face.Ink ?? Style.Text, lines,
                             face.Dotted ? face.Ink ?? Style.TextMuted : null);
    }

    private LayoutFace Typeface(Face face) => Style.Face(face.Font ?? (face.Mono ? Style.MonoFont : Style.TextFont), face.Bold, face.Italic);

    private double Size(Face face) => Math.Max(1, Style.TextSize * (face.Scale <= 0 ? 1 : face.Scale));

    private IShapedText Space => _space ??= Glyphs(" ", Face.Plain);

    private IShapedText? _space;
}
