using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>The face a task shows for how it scored: the best two smile, the middle one is flat, the worst two frown.</summary>
public enum JourneyMood { Sad, Neutral, Happy }

/// <summary>Text somebody wrote: what it says, and the hole standing where it is still to write.</summary>
/// <param name="Part">The piece it was written as — what pressing it means.</param>
public sealed record JourneyText(ContentPart Part, ContentPart Says, ContentPart? Hole);

/// <summary>One actor taking part in a task: their name as written there, and where they stand among the actors.</summary>
public sealed record JourneyActor(ContentPart Part, ContentPart Says, string Name, int Order);

/// <summary>One task: what is done, how it scored, and who took part.</summary>
/// <param name="Part">The line it was written on — what pressing it means.</param>
/// <param name="Scored">The score as written — what pressing the face means — or null where none is written.</param>
/// <param name="Score">How it scored, or null where that is not written, or is not a score.</param>
public sealed record JourneyTask(ContentPart Part, JourneyText Says, ContentPart? Scored, double? Score,
                                 IReadOnlyList<JourneyActor> Actors, int Order)
{
    /// <summary>The face it shows: its score, or the middle one where it has none.</summary>
    public JourneyMood Mood => JourneyDiagram.MoodOf(Score ?? JourneyDiagram.Middling);

    /// <summary>How high it sits: how it scored, or the middle where nothing scores it — a score nobody can reach being no score at all.</summary>
    public double Height => Score ?? JourneyDiagram.Middling;
}

/// <summary>A group of tasks: the <c>section</c> line naming it, or nothing for the tasks written before any section.</summary>
public sealed record JourneySection(ContentPart? Part, JourneyText? Name, IReadOnlyList<JourneyTask> Tasks, int Order);

/// <summary>
/// A <c>journey</c> block, read: the sections grouping its tasks, each task with its score and its actors, and the actors
/// themselves in the order they first take part — which is the order they are given their colours, and the legend.
///
/// <para>
/// Tasks written before any section are a group of their own with no name, as Mermaid groups them. An actor is whoever is
/// named, ignoring the space round the name, so <c>Me, Cat</c> and <c>Me,Cat</c> name the same two.
/// </para>
/// </summary>
public sealed class JourneyDiagram
{
    /// <summary>The score a task with none written for it is drawn at.</summary>
    public const double Middling = 3;

    private JourneyDiagram(MermaidBlock block, JourneyConfig config) => (Block, Config) = (block, config);

    

    public static JourneyDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static JourneyDiagram Of(MermaidBlock block)
    {
        var diagram = new JourneyDiagram(block, JourneyConfig.Read(block.Config));

        var named = new List<(ContentPart Part, JourneyText? Name)>();
        var tasks = new List<(JourneyTask Task, int Section)>();
        var actors = new List<string>();

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case JourneyKinds.Section:
                    named.Add((part, Text(part, JourneyRoles.Name)));
                    break;

                case JourneyKinds.Task when Text(part, JourneyRoles.Says) is { } says:
                    var scored = part.Inner(MermaidKinds.Amount);
                    tasks.Add((new JourneyTask(part, says, scored, scored.Number(), [.. TakingPart(part, actors)], tasks.Count),
                               Number(part.Fact(JourneyRoles.In))));
                    break;
            }
        }

        var sections = new List<JourneySection>();

        // Tasks written before any section are a group of their own, with nothing naming it.
        if (tasks.Any(task => task.Section < 0))
            sections.Add(new JourneySection(null, null, [.. tasks.Where(task => task.Section < 0).Select(task => task.Task)], 0));

        for (var at = 0; at < named.Count; at++)
            sections.Add(new JourneySection(named[at].Part, named[at].Name,
                [.. tasks.Where(task => task.Section == at).Select(task => task.Task)], sections.Count));

        diagram.Sections = sections;
        diagram.Sectioned = named.Count > 0;
        diagram.Actors = actors;
        return diagram;
    }

    public MermaidBlock Block { get; }

    public JourneyConfig Config { get; }

    /// <summary>The sections, in the order they are written.</summary>
    public IReadOnlyList<JourneySection> Sections { get; private set; } = [];

    /// <summary>Whether anything is written as a section, which is what puts a band over the tasks in one.</summary>
    public bool Sectioned { get; private set; }

    /// <summary>Every actor, in the order they first take part — the order of the legend, and of their colours.</summary>
    public IReadOnlyList<string> Actors { get; private set; } = [];

    /// <summary>Every task, in the order it is written.</summary>
    public IEnumerable<JourneyTask> Tasks => Sections.SelectMany(section => section.Tasks);

    /// <summary>Whether nothing is written for the diagram to draw.</summary>
    public bool Empty => !Tasks.Any();

    /// <summary>Mermaid's rule: the best two scores smile, the middle one is flat, and the worst two frown.</summary>
    public static JourneyMood MoodOf(double score) =>
        score >= 4 ? JourneyMood.Happy : score >= 3 ? JourneyMood.Neutral : JourneyMood.Sad;

    /// <summary>The actors a task names, each given where they stand among the actors — a name first taken part in here going on the end.</summary>
    private static IEnumerable<JourneyActor> TakingPart(ContentPart task, List<string> actors)
    {
        foreach (var name in task.Inner(MermaidKinds.Names).Named())
        {
            if (name.Words() is not { Length: > 0 } says || says.Text.Trim() is not { Length: > 0 } said) continue;

            var at = actors.IndexOf(said);
            if (at < 0)
            {
                at = actors.Count;
                actors.Add(said);
            }

            yield return new JourneyActor(name, says, said, at);
        }
    }

    private static int Number(string? said) => MermaidNumber.Read(said) is { } number ? (int)number : -1;

    private static JourneyText? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == JourneyKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new JourneyText(text, says, text.Hole())
            : null;
}
