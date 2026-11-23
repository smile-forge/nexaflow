using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>
/// Reads a user journey — a <c>journey</c> block: a <c>title</c>, the <c>section</c>s grouping its tasks, and the tasks themselves —
/// and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a journey. The header is optional, since a <c>journey</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A task is what is done, how it scored, and who took part, a colon between each —
/// <c>Make tea: 5: Me, Cat</c> — the actors a list with a comma between them, and both the score and the actors optional. A score
/// runs from one, the worst, to five, the best — though Mermaid takes any number, and one past either end is drawn there. A # starts a
/// comment, as Mermaid's lexer reads one, but for an entity code. Every colon splits, so a colon inside what a task says is written as the entity code
/// standing for it (<see cref="MermaidText"/>), which is what typing one in writes. Nothing a journey writes runs past its line, so
/// each statement is its row.
/// </para>
/// <para>
/// A task is in the section written above it, or in none before any is — the order the lines are written in, which the builder
/// reads, so the only stage a journey's language offers the engine is what its front matter asks for. An actor is named wherever
/// they take part, so a rename carries to every task they are in.
/// </para>
/// </summary>
public sealed class JourneyParser : ITranspile
{
    private JourneyParser()
    {
    }

    /// <summary>The language a journey is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "journey";

    /// <summary>The words a journey's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    public const string SectionWord = "section";

    /// <summary>The worst and best a task scores.</summary>
    public const int Worst = 1;
    public const int Best = 5;

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    private const string Stepping = "A task is what is done, how it scored and who took part: Make tea: 5: Me.";
    private const string Grouping = "A section names the tasks it groups: section Go to work.";

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where what a task says, a section's name or an actor's is still to write.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is JourneyKinds.Text or MermaidKinds.Name;

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a journey writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
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

        // A binding standing on a line of its own after the header is lines supplied by whatever the journey is shown against. Held
        // as written; read into its place before the journey is worked over.
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

    /// <summary>
    /// The header — <c>journey</c>, and anything after it held as written — or null where the line says something else, which a
    /// <c>journey</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>One statement: a title, a section or a task — or null for a title with nothing written after it yet.</summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, written => MermaidLine.HashComment(written));

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, SectionWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            SectionWord => Section(line),
            _ => Task(line),
        };
    }

    /// <summary>A task: <c>Make tea: 5: Me, Cat</c> — its score and its actors each still to write until a colon opens them.</summary>
    private static ContentNode Task(MermaidLine line)
    {
        Text(line, JourneyRoles.Says, until: ":");

        line.Space();
        if (!line.Token(":")) return line.Shown(Stepping);

        line.Room();
        if (!line.Done)
        {
            line.Amount(JourneyRoles.Score, Scored, until: ":");
            line.Space();

            if (line.Token(":"))
            {
                line.Room();
                if (!line.Names(Actor, JourneyRoles.Actors, JourneyRoles.Actor)) return line.Shown(Stepping);
            }
        }

        return line.Done ? line.Read(JourneyKinds.Task) : line.Shown(Stepping);
    }

    /// <summary>A section: <c>section Go to work</c>.</summary>
    private static ContentNode Section(MermaidLine line)
    {
        line.Word(SectionWord);

        var at = line.At;
        line.Room();
        if (line.At == at) return line.Shown(Grouping);

        Text(line, JourneyRoles.Name);
        return line.Done ? line.Read(JourneyKinds.Section) : line.Shown(Grouping);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own: what is written up to <paramref name="until"/>, or to the end of the line.</summary>
    private static void Text(MermaidLine line, string role, string? until = null)
    {
        line.Open();
        line.Words(role, until: until);
        line.Close(JourneyKinds.Text, role);
    }

    /// <summary>
    /// One actor's name: what is written up to the comma before the next, whatever it holds — a journey has no quotes, so the only
    /// characters a name cannot hold are the ones a line is read by, and those go in as their entity codes.
    /// </summary>
    private static bool Actor(MermaidLine line)
    {
        line.Open();
        line.Words(JourneyRoles.Actor, until: ",");
        line.Close(MermaidKinds.Name);
        return true;
    }

    /// <summary>What is wrong with a score that is no number. Mermaid takes any number, as scores past one and five are drawn at the ends.</summary>
    private static readonly Func<string, string?> Scored =
        MermaidNumber.Where(_ => true, "A task scores a number, from 1, the worst, to 5, the best.");
}
