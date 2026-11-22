using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>
/// Reads a timeline — a <c>timeline</c> block: a <c>title</c>, which way it runs, the <c>section</c>s grouping its periods, and the
/// periods with their events — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a timeline. The header is optional, since a <c>timeline</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A period is what it is called and then an event after each colon — <c>2004 : Facebook : Google</c> —
/// and a line starting with a colon goes on adding events to the period above it. An event runs to the next colon with space
/// after it, as Mermaid's lexer reads one, so a time — <c>10:30</c> — or a link stays in it; a colon typed into what something says
/// is written <c>#colon;</c> (<see cref="MermaidText"/>), which reads the same wherever it stands. A <c>#</c> before the colon events
/// follow starts a comment, but for an entity code. The way it runs may follow the keyword — <c>timeline TD</c> — or stand on a line
/// of its own, <c>direction TD</c>. Nothing a timeline writes runs past the end of its line, so each statement is its line.
/// </para>
/// <para>
/// Which section a period is in, and which period a line of further events adds them to, is the order the lines are written in.
/// Only a line of events with no period above it at all is worked out over the block, by a stage
/// (<see cref="Stages.ResolveEvents"/>) a timeline's language offers the engine. This reads what each line says, through
/// <see cref="MermaidLine"/>, and holds what it cannot read as written with the reason.
/// </para>
/// </summary>
public sealed class TimelineParser : ITranspile
{
    private TimelineParser()
    {
    }

    /// <summary>The language a timeline is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "timeline";

    /// <summary>The words a timeline's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    public const string SectionWord = "section";
    public const string DirectionWord = "direction";

    /// <summary>The ways a timeline runs: across the page, or down it — <c>TB</c> being Mermaid's other word for down.</summary>
    public static readonly IReadOnlyList<string> Ways = ["LR", "TD", "TB"];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    private const string Running = "A timeline runs LR, across the page, or TD, down it.";
    private const string Grouping = "A section names the periods it groups: section Early days.";
    private const string Timing = "A period is what it is called, then an event after each colon: 2004 : Facebook : Google.";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where a period's name, an event or a section's name is still to write.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind == TimelineKinds.Text;

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a timeline writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it is the header.</param>
    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A # starts a comment running to the end of its row, as Mermaid's lexer reads one — but for an entity code, which is a character.
        if (text[0] == '#' && MermaidLine.EntityLength(text, 0) == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines supplied by whatever the timeline is shown against.
        // Held as written; read into its place before the timeline is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, handed the line to the end of its row: as much of it as is read is what was written, and the rest is the
        // line's. A statement read as nothing yet — a title with nothing after it — is held whole.
        var said = Statement(source[from..row.End], from);
        lines.Add(MermaidParser.Line(source, row.Start, from,
                                     [said ?? ContentNode.Leaf(MermaidKinds.Statement, text, offset: from)], said is null ? to : from + said.Width, row));
        return row.Stop;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>timeline</c>, and the way it runs where that follows it, <c>timeline TD</c>; anything else after it held as
    /// written — or null where the line says something else, which a <c>timeline</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(HeaderWay(rest[gap..], after) ?? ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>The way it runs, written after the keyword — <c>timeline TD</c> — or null where what follows it is not that.</summary>
    private static ContentNode? HeaderWay(string arguments, int at)
    {
        var line = MermaidLine.Of(arguments, at);
        if (line.Done || MermaidLine.Keyword(line.Written, [.. Ways]) is null) return null;

        line.Setting(TimelineRoles.Way, Known);
        return line.Done ? line.Read(TimelineKinds.Direction) : null;
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>
    /// One statement: a title, a section, the way it runs, a period, or more events for the period above — or the text held as
    /// written, with the reason. Null for a title with nothing written after it yet, which is held whole.
    /// </summary>
    private static ContentNode? Statement(string text, int at)
    {
        // A # before the colon events follow starts a comment; one in an event is the event's own, as Mermaid's lexer reads them.
        var line = MermaidLine.Of(text, at, written => MermaidLine.HashComment(written, before: ':'));

        switch (MermaidLine.Keyword(line.Written, [MermaidLine.TitleWord, SectionWord, DirectionWord]))
        {
            case MermaidLine.TitleWord: return line.Title();
            case SectionWord: return Section(line);

            // The way it runs, on a line of its own; a period that only starts with the word — Direction change : Board approves — is a period.
            case DirectionWord when !line.Written.Contains(':'): return Way(line);
        }

        // A line starting with a colon is more events for the period above it; anything else is a period of its own.
        return line.Next == ':' ? More(line) : Period(line);
    }

    /// <summary>A period: what it is called, and an event after each colon.</summary>
    private static ContentNode Period(MermaidLine line)
    {
        Text(line, TimelineRoles.Says, until: ":");
        Events(line);

        return line.Done ? line.Read(TimelineKinds.Period) : line.Shown(Timing);
    }

    /// <summary>More events for the period above: <c>: Orkut</c>.</summary>
    private static ContentNode More(MermaidLine line)
    {
        Events(line);
        return line.Done ? line.Read(TimelineKinds.More) : line.Shown(Timing);
    }

    /// <summary>
    /// An event after each colon, the one still being written included. An event runs to the next colon with space after it, as
    /// Mermaid's lexer reads one, so a time — <c>10:30</c> — or a link stays in the event it is written in.
    /// </summary>
    private static void Events(MermaidLine line)
    {
        line.Space();

        while (line.Token(":"))
        {
            line.Room();
            if (line.Done) return;

            var end = line.At;
            while (end < line.Written.Length && !(line.Written[end] == ':' && (end + 1 == line.Written.Length || char.IsWhiteSpace(line.Written[end + 1])))) end++;
            while (end > line.At && char.IsWhiteSpace(line.Written[end - 1])) end--;

            line.Open();
            line.Words(TimelineRoles.Event, end);
            line.Close(TimelineKinds.Text, TimelineRoles.Event);
            line.Space();
        }
    }

    /// <summary>A section: <c>section Early days</c>.</summary>
    private static ContentNode Section(MermaidLine line)
    {
        line.Word(SectionWord);
        if (!Spaced(line)) return line.Shown(Grouping);

        Text(line, TimelineRoles.Name);
        return line.Done ? line.Read(TimelineKinds.Section) : line.Shown(Grouping);
    }

    /// <summary>Which way it runs: <c>direction TD</c>.</summary>
    private static ContentNode Way(MermaidLine line)
    {
        line.Word(DirectionWord);
        if (!Spaced(line)) return line.Shown(Running);

        // The way it runs is still to write.
        if (line.Done) return line.Read(TimelineKinds.Direction);

        line.Setting(TimelineRoles.Way, Known);
        return line.Done ? line.Read(TimelineKinds.Direction) : line.Shown(Running);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own: what is written up to <paramref name="until"/>, or to the end of the line.</summary>
    private static void Text(MermaidLine line, string role, string? until = null)
    {
        line.Open();
        line.Words(role, until: until);
        line.Close(TimelineKinds.Text, role);
    }

    /// <summary>What is wrong with a way that is none of them.</summary>
    private static string? Known(string said) =>
        Ways.Any(way => way.Equals(said.Trim(), StringComparison.OrdinalIgnoreCase)) ? null : Running;

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
