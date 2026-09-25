using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Journey.Stages;
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
/// Which section a task is in is a fact about the block rather than about the task's own line, so it is the stage's
/// (<see cref="ResolveTasks"/>); an actor is named wherever they take part, so a rename carries to every task they are in.
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
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, SectionWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            SectionWord => Section(line),
            _ => Task(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Under a task or the section holding it, another task, scored in the middle with its name still to write.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above?.Kind is JourneyKinds.Task or JourneyKinds.Section ? (": 3", 0) : null;

    /// <inheritdoc/>
    /// <remarks>Every colon splits a task from its score and its actors, and a <c>%%</c> closes the line, so both go in as the entity codes standing for them.</remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is not { Kind: JourneyKinds.Text or MermaidKinds.Name } || !text.Any(character => character is ':' or '%' or ',')) return null;

        var written = Escaped(text);
        return new MermaidWriting(caret, caret, written, caret + written.Length);
    }

    /// <summary>Text with the characters a line is read by — the colons between a task's parts, the commas between its actors, and a comment's per cent signs — written as the entity codes standing for them.</summary>
    private static string Escaped(string text) =>
        text.Replace(":", "#colon;", StringComparison.Ordinal)
            .Replace("%", "#37;", StringComparison.Ordinal)
            .Replace(",", "#44;", StringComparison.Ordinal);

    /// <inheritdoc/>
    /// <remarks>An actor is named wherever they take part: renaming one renames every task they are in.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var named = block.SelfAndDescendants()
            .Where(part => part is { Kind: MermaidKinds.Words, Role: JourneyRoles.Actor, Length: > 0 })
            .Select(words => (Part: words.Parent ?? words, Said: words.Text.Trim()))
            .Where(actor => actor.Said.Length > 0)
            .ToList();

        return
        [
            .. named
                .GroupBy(actor => actor.Said, StringComparer.Ordinal)
                .Select(actor => new MermaidName(actor.Key, actor.First().Part, [.. actor.Skip(1).Select(use => use.Part)])),
        ];
    }

    /// <inheritdoc/>
    /// <remarks>A name goes in as it is, but for the characters a line is read by, which go in as their entity codes.</remarks>
    public string Naming(string name) => Escaped(name);

    /// <inheritdoc/>
    /// <remarks>Which section each task is in is worked out over the whole block (<see cref="ResolveTasks"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveTasks()];

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
