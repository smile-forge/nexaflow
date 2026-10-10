using Nexaflow.Markdown.Ast;

using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>
/// What a <c>journey</c> block says beyond the lines every diagram shares: a <c>title</c>, the <c>section</c>s grouping its
/// tasks, and the tasks themselves.
///
/// <para>
/// The rules are Mermaid's. A task is what is done, how it scored, and who took part, a colon between each —
/// <c>Make tea: 5: Me, Cat</c> — the actors a list with a comma between them, and both the score and the actors optional.
/// A score runs from one, the worst, to five, the best. Every colon splits, so a colon inside what a task says is written
/// as the entity code standing for it (<see cref="MermaidText"/>), which is what typing one in writes.
/// </para>
/// <para>
/// A task is in the section written above it, or in none before any is; an actor is named wherever they take part, so a
/// rename carries to every task they are in.
/// </para>
/// </summary>
public sealed class JourneyGrammar : IMermaidGrammar
{
    public const string SectionWord = "section";

    /// <summary>The worst and best a task scores.</summary>
    public const int Worst = 1;
    public const int Best = 5;

    private const string Stepping = "A task is what is done, how it scored and who took part: Make tea: 5: Me.";
    private const string Grouping = "A section names the tasks it groups: section Go to work.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, SectionWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            SectionWord => Section(line),
            _ => Task(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Only what the front matter asks for: which section each task is in is the order the lines are written in.</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new WithConfig<JourneyConfig>(JourneyConfig.Read(block.Config))];

    /// <inheritdoc/>
    /// <remarks>Where what a task says, a section's name or an actor's is still to write.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is JourneyKinds.Text or MermaidKinds.Name;

    // ── Lines ───────────────────────────────────────────────────────────────

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
    /// One actor's name: what is written up to the comma before the next, whatever it holds — a journey has no quotes, so the
    /// only characters a name cannot hold are the ones a line is read by, and those go in as their entity codes.
    /// </summary>
    private static bool Actor(MermaidLine line)
    {
        line.Open();
        line.Words(JourneyRoles.Actor, until: ",");
        line.Close(MermaidKinds.Name);
        return true;
    }

    /// <summary>What is wrong with a score outside what a journey scores.</summary>
    private static readonly Func<string, string?> Scored =
        MermaidNumber.Where(number => number >= Worst && number <= Best, $"A task scores from {Worst}, the worst, to {Best}, the best.");
}
