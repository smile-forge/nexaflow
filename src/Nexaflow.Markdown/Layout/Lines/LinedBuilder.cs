using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Lines;

namespace Nexaflow.Markdown.Layout.Lines;

/// <summary>
/// Content read line by line — code, text shown as typed — onto the renderer-free layout tree, set line under line in a monospaced face,
/// each piece of a line in the ink the language gives it. What a line is made of is the language's: a builder of its own says what the
/// whole stretch and each line are laid as, and what each piece is drawn in; how lines are laid, kept and wrapped is this, the same for
/// every one of them.
///
/// <para>
/// Lines are kept. Code that reflowed would be code that lied about where a statement ended, and a reader counting columns is the whole
/// reason it is set in a monospaced face. Where the host asks for text shown as typed instead (<see cref="LayoutHost.WrapsLines"/>) — a
/// page's source, read as a page — a line too long for the room carries on in rows beneath, broken after the last space that fits
/// (<see cref="LineRows"/>), and a piece a row break cuts is laid as pieces standing for their share of it.
/// </para>
/// <para>
/// A line is a block: one that reads exactly as it did last time — the same characters, made of the same pieces, and where it wraps, in
/// the same room — is set down as it was laid (<see cref="LaidBlocks"/>), so a keystroke lays the line it was typed on. Where the host asks
/// for only what the reader reaches (<see cref="LayoutHost.Reach"/>), only the lines there are laid, room left for every other; and where
/// each line laid stands is said (<see cref="Laid.Lines"/>), for what is drawn beside the page in step with them.
/// </para>
/// </summary>
public abstract class LinedBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                           LayoutHost host)
    : ContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    /// <summary>How far along each character is set, measured once a laying out, to break lines into rows.</summary>
    private readonly Dictionary<char, double> _advances = [];

    /// <summary>What measures the characters lines are set in, made the first time a line is broken into rows.</summary>
    private TextMeasure? _measure;

    /// <summary>What the whole stretch is laid as.</summary>
    protected abstract string StretchKind { get; }

    /// <summary>What a line of it is, in the tree it was read into and laid as.</summary>
    protected abstract string LineKind { get; }

    /// <summary>What a piece of a line the language calls <paramref name="kind"/> is drawn in.</summary>
    protected abstract LayoutInk Ink(string kind);

    protected sealed override Laid? Build()
    {
        if (Reading.Root.Length == 0) return null;

        var height = LinedSetting.LineHeight(Characters, Medium);
        var laid = LaidBlocks.Of(Reading.Root);

        // A stretch the next carries on from ends where the next begins, which stands for that place: a caret is not put after it too.
        var carriedOn = LinedSetting.CarriedOn(Reading.Root.Node);

        // Where the host asks for only what the reader reaches, only the lines there are laid, and room is left for every other.
        var asked = ReaderReach;
        var stretches = asked is null ? null : new List<(int Start, int End)>();

        // Where each line laid stands, for what is drawn beside the page in step with them.
        var lines = new List<LaidLine>();

        var into = new LayoutBuilder(Medium, Reading.Root.Children.Count + 1);
        var (y, wide, last) = (0.0, 0.0, height);

        into.Open(StretchKind, Reading.Root, stops: carriedOn ? LayoutStops.Before : LayoutStops.Both);

        for (var at = 0; at < Reading.Root.Children.Count; at++)
        {
            var line = Reading.Root.Children[at];
            if (line.Kind != LineKind) continue;

            // A line takes the room its rows do, laid or not: one, where lines are kept, and where they wrap, as many as it breaks onto.
            var kept = this.Kept(laid?.For(at));
            var wrapped = WrapsLines && kept is null ? this.Breaks(line) : ((List<int> Starts, double Indent)?)null;
            var tall = !WrapsLines ? height : kept?.Height ?? (1 + wrapped!.Value.Starts.Count) * height;

            if (asked is null || Reaches(asked, line, y, y + tall))
            {
                var (mount, reach) = kept is not null ? (new LayoutMount(kept.Tree, kept.Part, line), kept.Reach) : this.Line(line, laid?.For(at), height, wrapped);
                into.Mount(mount, new LayoutPoint(0, y));
                wide = Math.Max(wide, reach);
                lines.Add(new LaidLine(line.Start, y, tall));

                if (stretches is not null)
                {
                    if (stretches.Count > 0 && stretches[^1].End == line.Start) stretches[^1] = (stretches[^1].Start, line.End);
                    else stretches.Add((line.Start, line.End));
                }
            }

            if (line.Children.Count > 0 && line.Children[^1].Kind == LineKinds.LineEnd) y += tall;
            else last = tall;
        }

        // The line after the last line end is a line of its own — where a caret goes after a closing line feed — unless the next
        // stretch carries on from this one, whose first line it is.
        var all = Math.Max(carriedOn ? y : y + last, height);

        into.Close();

        return new Laid(into.Seal(), Math.Max(wide, 1), all, [])
        {
            Reached = asked is null ? null : new LayoutReached(asked.Top, asked.Bottom, stretches!),
            Lines = lines,
        };
    }

    /// <summary>Whether <paramref name="line"/>, standing from <paramref name="top"/> to <paramref name="bottom"/>, is what <paramref name="asked"/> reaches: on it, or holding one of its places.</summary>
    private static bool Reaches(LayoutReach asked, ContentPart line, double top, double bottom)
    {
        if (top < asked.Bottom && bottom > asked.Top) return true;

        foreach (var place in asked.Places)
            if (place >= line.Start && place <= line.End) return true;

        return false;
    }

    /// <summary>
    /// The tree a line was laid as last time, where it can be set down again: laid as this lays it, and where lines wrap, in this room —
    /// null where it is to be laid afresh.
    /// </summary>
    private KeptLaying? Kept(LaidBlock? laid) =>
        laid?.Last is { } last && last.IsReadOnly == IsReadOnly && (!WrapsLines || last.Room == Room) ? last : null;

    /// <summary>
    /// One line, as a tree of its own standing at the top of its own frame, its picture kept once painted, put down whole — and how far
    /// across it reaches: each of its pieces set in the ink it is given, the line end taking no room, and where it wraps, carrying on under
    /// its indent in the row beneath where each row after the first starts, a piece cut there laid as pieces standing for their share of
    /// it. Kept as it was laid, to be set down again where it reads the same.
    /// </summary>
    /// <param name="wrapped">Where in the line each row after its first starts, and how far in, or null where lines are kept whole.</param>
    private (LayoutMount Mount, double Reach) Line(ContentPart line, LaidBlock? laid, double height, (List<int> Starts, double Indent)? wrapped)
    {
        var characters = Characters;
        var apart = new LayoutBuilder(Medium);
        var (breaks, indent) = (wrapped?.Starts, wrapped?.Indent ?? 0);
        var (x, reach, row, next, along) = (0.0, 0.0, 0, 0, 0);

        apart.Open(LineKind, line, stops: LayoutStops.None, paints: new LayoutPaint(Kept: new LayoutKept()));

        foreach (var piece in line.Children)
        {
            if (piece.Kind == LineKinds.LineEnd) continue;

            var ink = Ink(piece.Kind);
            var text = piece.Text;

            for (var from = 0; from < text.Length;)
            {
                // A row starting here carries the line on in the row beneath, under its indent.
                for (; breaks is not null && next < breaks.Count && breaks[next] <= along + from; next++) (x, row) = (indent, row + 1);

                var to = breaks is not null && next < breaks.Count ? Math.Min(breaks[next] - along, text.Length) : text.Length;
                var whole = from == 0 && to == text.Length;
                var glyphs = Medium.Shape(whole ? text : text[from..to], characters.Face, characters.Size, ink);

                // How far along the next piece starts: what this one is set in AND the space after it. A run of spaces is a piece of its
                // own, and a width that leaves trailing space out measures such a piece at nothing and closes up the words either side.
                var across = glyphs.WidthWithSpace;

                // The piece is called what the language called it, so what a reader pressed and what the theme coloured are the same
                // question asked of the same piece.
                LayoutText.Words(apart, glyphs, new LayoutPoint(x, row * height), across + 1, LayoutAlign.Left,
                                 whole ? piece : new PartSlice(piece, from, to - from), piece.Kind, maps: true, ink: ink);

                x += across;
                reach = Math.Max(reach, x);
                from = to;
            }

            along += text.Length;
        }

        apart.Close();

        var made = apart.Seal();
        if (laid is not null) laid.Last = new KeptLaying(line, made, (1 + (breaks?.Count ?? 0)) * height, reach, [], Room, IsReadOnly);

        return (new LayoutMount(made), reach);
    }

    /// <summary>
    /// Where in <paramref name="line"/> each row after its first starts, and how far in those rows start: its characters measured in the
    /// face lines are set in, and broken in the room (<see cref="LineRows"/>).
    /// </summary>
    private (List<int> Starts, double Indent) Breaks(ContentPart line)
    {
        var breaks = new List<int>();
        var rows = new LineRows(Room);

        foreach (var piece in line.Children)
        {
            if (piece.Kind == LineKinds.LineEnd) continue;

            var text = piece.Text;
            for (var at = 0; at < text.Length; at++)
                if (rows.Next(text[at], this.Advance(text, at)) is >= 0 and var starts) breaks.Add(starts);
        }

        return (breaks, rows.Indent);
    }

    /// <summary>
    /// How far along the character at <paramref name="at"/> in <paramref name="text"/> is set: a pair standing for one character measured
    /// as one, its second half taking no room.
    /// </summary>
    private double Advance(string text, int at)
    {
        var character = text[at];
        _measure ??= Medium.Measuring(Characters.Face, Characters.Size);

        if (char.IsLowSurrogate(character) && at > 0 && char.IsHighSurrogate(text[at - 1])) return 0;
        if (char.IsHighSurrogate(character) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1])) return _measure.Of(text.AsSpan(at, 2)).Advance;

        if (_advances.TryGetValue(character, out var known)) return known;

        return _advances[character] = _measure.Of(text.AsSpan(at, 1)).Advance;
    }
}
