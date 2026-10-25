using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Journey;

/// <summary>
/// Draws a <c>journey</c> block: its tasks in a row under a band per section, a face floating over each one — higher for a
/// better score — and a mark on every task for each actor taking part, explained by a legend along the top.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A task stands for its line, a band for its
/// <c>section</c> line, a face for the score it shows, and an actor's mark for where they are named on that task's line;
/// what a task says, and each actor's name in the legend, are typed into where they are drawn — and a rename in the legend
/// carries to every task that actor takes part in. A task nothing scores shows the middling face, and says so on its line
/// where what is written is no score.
/// </para>
/// </summary>
internal sealed class PortableJourneyBuilder : PortableContentBuilder
{
    /// <summary>How big a task is drawn, and how far one is from the next, before the front matter asks otherwise.</summary>
    private const double Wide = 150;
    private const double Tall = 50;
    private const double Apart = 10;

    private const double TaskSize = 12;
    private const double NameSize = 12;
    private const double ActorSize = 11;

    /// <summary>How far a face climbs for each point it scores, and how big one is drawn.</summary>
    private const double Step = 22;
    private const double Face = 12;

    /// <summary>How big an actor's mark is on a task, and how far the marks are from each other.</summary>
    private const double Mark = 6;
    private const double Beside = 3;

    private const double Band = 28;
    private const double Gap = 8;
    private const double Pad = 6;

    /// <summary>How solid a section's band and a task's box are tinted, and a face's own colour.</summary>
    private const double Banded = 0.4;
    private const double Tinted = 0.2;
    private const double Faced = 0.7;

    /// <summary>How far round the colours an actor's is from the sections', so the two are told apart.</summary>
    private const int Round = 4;

    internal PortableJourneyBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                    PortableHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new PortableDiagramInk(style, medium);
        Words = new PortableDiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, PortableStanding.Inline));
        Sheet = new PortableDiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private PortableDiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private PortableDiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private PortableDiagramSheet Sheet { get; }

    protected override PortableLaid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new PortableBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), null, null);
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);

    /// <summary>The score a task with none written for it is drawn at.</summary>
    private const double Middling = 3;

    /// <summary>The face a task shows for how it scored: the best two smile, the middle one is flat, the worst two frown.</summary>
    private enum Mood { Sad, Neutral, Happy }

    /// <summary>Text somebody wrote: the piece it was written as — what pressing it means — what it says, and the hole standing where it is still to write.</summary>
    private readonly record struct Phrase(ContentPart Part, ContentPart Says, ContentPart? Hole);

    /// <summary>One actor taking part in a task: their name as written there, and where they stand among the actors.</summary>
    private sealed record Actor(ContentPart Part, ContentPart Says, int Order);

    /// <summary>One task: the line it was written on, what is done, the score as written and what it comes to, and who took part.</summary>
    /// <param name="Score">How it scored, or null where that is not written, or is not a score.</param>
    private sealed record Job(ContentPart Part, Phrase Says, ContentPart? Scored, double? Score, IReadOnlyList<Actor> Actors)
    {
        /// <summary>How high it sits: how it scored, or the middle where nothing scores it — a score nobody can reach being no score at all.</summary>
        public double Height => Score ?? Middling;

        /// <summary>Mermaid's rule: the best two scores smile, the middle one is flat, and the worst two frown.</summary>
        public Mood Mood => Height >= 4 ? Mood.Happy : Height >= 3 ? Mood.Neutral : Mood.Sad;
    }

    /// <summary>A group of tasks: the <c>section</c> line naming it, or nothing for the tasks written before any section.</summary>
    private sealed record Group(ContentPart? Part, Phrase? Name, List<Job> Tasks, int Order);

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        var sections = Read();
        var tasks = sections.SelectMany(section => section.Tasks.Select(task => (Task: task, Section: section))).ToList();

        // A journey with no tasks written in it is the source.
        if (tasks.Count == 0) return Sheet.AsWritten();

        var config = PortableDiagramConfig.Of(Reading, JourneyConfig.Default);
        var sectioned = sections.Any(section => section.Part is not null);
        var (wide, tall) = (config.Width ?? Wide, config.Height ?? Tall);
        var apart = config.BoxMargin ?? Apart;

        var legend = Legend(config, tasks);
        var lane = legend.Size.Height + (legend.Size.Height > 0 ? Gap : 0) + (sectioned ? Band + Gap : 0);
        var banded = legend.Size.Height + (legend.Size.Height > 0 ? Gap : 0);
        var top = lane + (JourneyGrammar.Best * Step) + Gap;

        var boxes = tasks.Select((_, at) => new LayoutRect(at * (wide + apart), top, wide, tall)).ToList();

        var room = new PortableDiagramRoom();
        room.Reach(new LayoutRect(0, 0, Math.Max(legend.Size.Width, boxes[^1].Right), top + tall));

        if (sectioned) Sections(build, config, tasks, boxes, banded, room);
        Faces(build, tasks, boxes, lane, room);
        Tasks(build, config, tasks, boxes, config.TaskFontSize ?? TaskSize);

        legend.Draw(build, new LayoutPoint(0, 0));

        return room.Size;
    }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>
    /// The sections in the order they are written, each with the tasks written under it. Tasks written before any section are
    /// a group of their own with no name, as Mermaid groups them. An actor is whoever is named, ignoring the space round the
    /// name, so <c>Me, Cat</c> and <c>Me,Cat</c> name the same two; they stand in the order they first take part, which is
    /// the order of their colours and of the legend.
    /// </summary>
    private List<Group> Read()
    {
        var sections = new List<Group>();
        var actors = new List<string>();

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case JourneyKinds.Section:
                    sections.Add(new Group(part, Text(part, JourneyRoles.Name), [], sections.Count));
                    break;

                case JourneyKinds.Task when Text(part, JourneyRoles.Says) is { } says:
                    if (sections.Count == 0) sections.Add(new Group(null, null, [], 0));

                    var scored = part.Inner(MermaidKinds.Amount);
                    sections[^1].Tasks.Add(new Job(part, says, scored, scored.Number(), [.. TakingPart(part, actors)]));
                    break;
            }
        }

        return sections;
    }

    /// <summary>The actors a task names, each given where they stand among the actors — a name first taken part in here going on the end.</summary>
    private static IEnumerable<Actor> TakingPart(ContentPart task, List<string> actors)
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

            yield return new Actor(name, says, at);
        }
    }

    private static Phrase? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == JourneyKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new Phrase(text, says, text.Hole())
            : null;

    // ── Layers ──────────────────────────────────────────────────────────────

    /// <summary>The actors along the top, each a mark in the colour their tasks carry and their name as written.</summary>
    private PortableDiagramLegend Legend(JourneyConfig config, IReadOnlyList<(Job Task, Group Section)> tasks)
    {
        var first = tasks
            .SelectMany(task => task.Task.Actors)
            .GroupBy(actor => actor.Order)
            .OrderBy(actor => actor.Key)
            .Select(actor => actor.First())
            .ToList();

        return new PortableDiagramLegend(
            [.. first.Select(actor => new PortableDiagramKey(actor.Part, Colour(config, actor.Order), [Words.Written(actor.Says, hole: null, ActorSize, Style.Text)]))],
            [JourneyPiece.Name], across: true, Style.CodeBorder);
    }

    /// <summary>A band over each run of tasks the same section groups.</summary>
    private void Sections(PortableBuilder build, JourneyConfig config, IReadOnlyList<(Job Task, Group Section)> tasks,
                          IReadOnlyList<LayoutRect> boxes, double top, PortableDiagramRoom room)
    {
        build.Open(JourneyPiece.Sections, part: null, stops: PortableStops.None);

        foreach (var (order, at, _, run) in PortableDiagramBand.Runs(tasks, task => task.Section.Order, boxes))
        {
            if (tasks[at].Section.Name is not { } name) continue;

            var band = new LayoutRect(run.Left, top, run.Width, Band);
            var fill = Ink.Series(order, config.SectionFill(order));
            var words = Words.Wrapped(name.Says, name.Hole, NameSize, Style.Text, Math.Max(20, band.Width - (Pad * 2)), 600);

            room.Reach(band);
            PortableDiagramShapes.Draw(build, JourneyPiece.Section, tasks[at].Section.Part, DiagramShape.Rounded, band,
                PortableDiagramInk.Faded(build.Medium, fill, Banded), new PortableDiagramStroke(fill),
                PortableDiagramWords.Placed(words, LayoutRect.Inflate(band, -Pad, -Pad), JourneyPiece.Name));
        }

        build.Close();
    }

    /// <summary>Each task's face, floating as high as it scored, on a thread down to the task itself.</summary>
    private void Faces(PortableBuilder build, IReadOnlyList<(Job Task, Group Section)> tasks, IReadOnlyList<LayoutRect> boxes,
                       double lane, PortableDiagramRoom room)
    {
        build.Open(JourneyPiece.Faces, part: null, stops: PortableStops.None);

        for (var at = 0; at < tasks.Count; at++)
        {
            var task = tasks[at].Task;
            var middle = new LayoutPoint(boxes[at].Left + (boxes[at].Width / 2), lane + ((JourneyGrammar.Best - task.Height) * Step) + Face);

            PortableDiagramConnector.Draw(build, JourneyPiece.Thread, task.Part, [new LayoutPoint(middle.X, middle.Y + Face), new LayoutPoint(middle.X, boxes[at].Top)],
                new PortableDiagramStroke(PortableDiagramInk.Faded(build.Medium, Style.TextMuted, 0.6), 1, PortableDiagramStroke.Dotted), end: DiagramHead.None);

            room.Reach(new LayoutRect(middle.X - Face, middle.Y - Face, Face * 2, Face * 2));
            Drawn(build, task, middle);
        }

        build.Close();
    }

    /// <summary>One face: a disc in the colour of its mood, two eyes, and a mouth that smiles, flattens or frowns.</summary>
    private void Drawn(PortableBuilder build, Job task, LayoutPoint middle)
    {
        var mood = task.Mood switch
        {
            Mood.Happy => Style.Success,
            Mood.Neutral => Style.Warning,
            _ => Style.Danger,
        };

        var disc = build.Medium.Ellipse(middle, Face, Face);

        var eyes = new List<LayoutShape>
        {
            build.Medium.Ellipse(new LayoutPoint(middle.X - 4.5, middle.Y - 3.5), 1.6, 1.6),
            build.Medium.Ellipse(new LayoutPoint(middle.X + 4.5, middle.Y - 3.5), 1.6, 1.6),
        };

        var mouth = task.Mood switch
        {
            Mood.Happy => PortableDiagramCurve.Bowed(build.Medium, new LayoutPoint(middle.X - 5, middle.Y + 2), new LayoutPoint(middle.X, middle.Y + 9), new LayoutPoint(middle.X + 5, middle.Y + 2)),
            Mood.Sad => PortableDiagramCurve.Bowed(build.Medium, new LayoutPoint(middle.X - 5, middle.Y + 6), new LayoutPoint(middle.X, middle.Y - 1), new LayoutPoint(middle.X + 5, middle.Y + 6)),
            _ => build.Medium.Line(new LayoutPoint(middle.X - 5, middle.Y + 4), new LayoutPoint(middle.X + 5, middle.Y + 4)),
        };

        // The face stands for what scores it, so a press on it means the score — or the task's line, where nothing scores it.
        build.Open(JourneyPiece.Face, task.Scored ?? task.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, disc, PortableDiagramInk.Faded(build.Medium, mood, Faced), Style.Text, 1.2));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Group(eyes), Style.Text, null, 0));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, mouth, null, Style.Text, 1.4));
        build.Occupies(disc);
        build.Close();
    }

    /// <summary>The tasks in a row, each saying what it is, with a mark for everyone taking part in it.</summary>
    private void Tasks(PortableBuilder build, JourneyConfig config, IReadOnlyList<(Job Task, Group Section)> tasks,
                       IReadOnlyList<LayoutRect> boxes, double size)
    {
        build.Open(JourneyPiece.Tasks, part: null, stops: PortableStops.None);

        for (var at = 0; at < tasks.Count; at++)
        {
            var (task, section) = tasks[at];
            var box = boxes[at];
            var fill = Ink.Series(section.Order, config.SectionFill(section.Order));
            var words = Words.Wrapped(task.Says.Says, task.Says.Hole, size, Style.Text, Math.Max(20, box.Width - (Pad * 2)));

            PortableDiagramShapes.Draw(build, JourneyPiece.Task, task.Part, DiagramShape.Rounded, box,
                PortableDiagramInk.Faded(build.Medium, fill, Tinted), new PortableDiagramStroke(fill),
                PortableDiagramWords.Placed(words, LayoutRect.Inflate(box, -Pad, -Pad), JourneyPiece.Says));

            Actors(build, config, task, box);
        }

        build.Close();
    }

    /// <summary>Everyone taking part in a task, as marks along the top of its box in the colour each carries.</summary>
    private void Actors(PortableBuilder build, JourneyConfig config, Job task, LayoutRect box)
    {
        if (task.Actors.Count == 0) return;

        build.Open(JourneyPiece.Actors, part: null, stops: PortableStops.None);

        for (var at = 0; at < task.Actors.Count; at++)
        {
            var actor = task.Actors[at];
            var middle = new LayoutPoint(box.Left + Mark + Beside + (at * ((Mark * 2) + Beside)), box.Top);

            var dot = build.Medium.Ellipse(middle, Mark, Mark);

            build.Open(JourneyPiece.Actor, actor.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, dot, Colour(config, actor.Order), Style.CodeBg, 1.2));
            build.Occupies(dot);
            build.Close();
        }

        build.Close();
    }

    // ── Ink ─────────────────────────────────────────────────────────────────

    /// <summary>What an actor is drawn in: the colour written for them, or one of the theme's, round from the sections'.</summary>
    private LayoutInk Colour(JourneyConfig config, int actor) => Ink.Series(actor + Round, config.ActorColour(actor));

}
