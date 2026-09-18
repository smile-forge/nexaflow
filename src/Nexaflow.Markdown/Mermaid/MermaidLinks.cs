namespace Nexaflow.Markdown.Mermaid;

/// <summary>What a link draws at one of its ends.</summary>
public enum MermaidHead
{
    /// <summary>Nothing: the line simply stops.</summary>
    None,

    /// <summary><c>&gt;</c> at the end, <c>&lt;</c> at the start.</summary>
    Arrow,

    /// <summary><c>o</c>.</summary>
    Circle,

    /// <summary><c>x</c>.</summary>
    Cross,
}

/// <summary>How the line of a link is drawn, which is what it is written with.</summary>
public enum MermaidLineStyle
{
    /// <summary>Dashes: <c>--&gt;</c>.</summary>
    Solid,

    /// <summary>Equals signs, drawn thicker: <c>==&gt;</c>.</summary>
    Thick,

    /// <summary>Dots between dashes: <c>-.-&gt;</c>.</summary>
    Dotted,

    /// <summary>Tildes, which draw nothing at all and only hold the nodes apart: <c>~~~</c>.</summary>
    Invisible,
}

/// <summary>
/// The links Mermaid writes between two nodes — a flowchart's edges, a block diagram's links — read from the characters they
/// are drawn as. The same set in both, because Mermaid writes them the same way: dashes, equals signs or dots for the line,
/// a head at either end or both, and as many characters as the author wants for a link that reaches further.
///
/// <para>
/// A link is written whole — <c>--&gt;</c>, <c>-.-&gt;</c>, <c>==o</c> — or opened, with what is written on it and the closing
/// of it still to come: <c>--</c> text <c>--&gt;</c>. <see cref="At"/> says which, so a grammar reading a line knows whether to
/// look for a label next.
/// </para>
/// </summary>
public static class MermaidLinks
{
    /// <summary>The characters a head is drawn as, at either end of a link.</summary>
    public const string Heads = "xo<>";

    /// <summary>The characters a link's line is drawn with, which is where one starts.</summary>
    public const string Lines = "-=.~";

    /// <summary>
    /// A link written at a place: how many characters it takes, and whether it is the whole of one or the opening of one whose
    /// label — and closing — are still to come.
    /// </summary>
    public readonly record struct Joined(int Length, bool Whole);

    /// <summary>What a link draws: the head at each end, the line it is drawn as, and how many ranks it reaches over.</summary>
    /// <param name="Span">
    /// How far apart the link holds what it joins, in ranks: one for the shortest way of writing it, and one more for every
    /// character it is written longer than that — <c>--&gt;</c> one, <c>---&gt;</c> two, <c>-..-&gt;</c> two.
    /// </param>
    public readonly record struct Drawn(MermaidHead Start, MermaidHead End, MermaidLineStyle Style, int Span);

    /// <summary>What is written at <paramref name="at"/> as a link, or null where nothing there is one.</summary>
    public static Joined? At(string text, int at)
    {
        var from = at;
        if (at < text.Length && text[at] is 'x' or 'o' or '<') at++;
        if (at >= text.Length) return null;

        return text[at] switch
        {
            '-' or '=' => Dashed(text, at, from, text[at]),
            '.' => Dotted(text, at, from, leading: false),

            // A link drawn as nothing at all, which takes no head and so no character before it either.
            '~' when at == from && Run(text, at, '~') is var tildes and >= 3 => new Joined(tildes, true),
            _ => null,
        };
    }

    /// <summary>What the characters a whole link is written as draw.</summary>
    public static Drawn Of(string? token)
    {
        var drawn = (token ?? string.Empty).Trim();
        if (drawn.Length == 0) return new Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Solid, 1);

        var start = Head(drawn[0], start: true);
        var style = Styled(drawn);

        return new Drawn(start, Head(drawn[^1], start: false), style, Span(drawn, style, start != MermaidHead.None));
    }

    /// <summary>What a character at one end of a link draws there — an arrow only where it points away from the link.</summary>
    public static MermaidHead Head(char character, bool start) => character switch
    {
        'x' => MermaidHead.Cross,
        'o' => MermaidHead.Circle,
        '<' when start => MermaidHead.Arrow,
        '>' when !start => MermaidHead.Arrow,
        _ => MermaidHead.None,
    };

    private static MermaidLineStyle Styled(string drawn) =>
        drawn.Contains('~') ? MermaidLineStyle.Invisible
        : drawn.Contains('.') ? MermaidLineStyle.Dotted
        : drawn.Contains('=') ? MermaidLineStyle.Thick
        : MermaidLineStyle.Solid;

    /// <summary>
    /// How far a link reaches: the dots of a dotted one, and otherwise everything it is written with beyond the shortest way of
    /// writing it — its head, and the one character a link cannot be written without.
    /// </summary>
    private static int Span(string drawn, MermaidLineStyle style, bool headed)
    {
        if (style == MermaidLineStyle.Dotted) return Math.Max(1, drawn.Count(character => character == '.'));

        return Math.Max(1, drawn.Length - (headed ? 1 : 0) - 2);
    }

    /// <summary>
    /// A link of dashes or of equals signs. Two of them are the opening of a labelled link; more than two, or two and a head,
    /// are the whole of one.
    /// </summary>
    private static Joined? Dashed(string text, int at, int from, char dash)
    {
        var run = Run(text, at, dash);

        // A single dash opens the dots of a dotted link, and is nothing else.
        if (run == 1) return dash == '-' ? Dotted(text, at + 1, from, leading: true) : null;

        var after = at + run;
        if (after < text.Length && text[after] is 'x' or 'o' or '>') return new Joined(after + 1 - from, true);

        return run > 2 ? new Joined(after - from, true) : new Joined(at + 2 - from, false);
    }

    /// <summary>A dotted link: the dots with a dash either side of them — <c>-.-</c>, <c>-.-&gt;</c> — or the <c>-.</c> that opens one.</summary>
    private static Joined? Dotted(string text, int at, int from, bool leading)
    {
        var dots = Run(text, at, '.');
        if (dots == 0) return null;

        var after = at + dots;
        if (after >= text.Length || text[after] != '-') return leading ? new Joined(at + 1 - from, false) : null;

        after++;
        if (after < text.Length && text[after] is 'x' or 'o' or '>') after++;

        return new Joined(after - from, true);
    }

    /// <summary>How many of <paramref name="character"/> are written in a row at <paramref name="at"/>.</summary>
    private static int Run(string text, int at, char character)
    {
        var end = at;
        while (end < text.Length && text[end] == character) end++;

        return end - at;
    }
}
