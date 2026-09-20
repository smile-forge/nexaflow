using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Journey;

/// <summary>The pieces a user journey's layout is made of — its layers, and what is in them.</summary>
public static class JourneyPiece
{
    /// <summary>The bands over the tasks each named section groups, and one — standing for its <c>section</c> line.</summary>
    public const string Sections = "Sections";
    public const string Section = "Section";

    /// <summary>The faces floating over the tasks, and one — standing for the score it shows.</summary>
    public const string Faces = "Faces";
    public const string Face = "Face";

    /// <summary>What holds a face over its task.</summary>
    public const string Thread = "Thread";

    /// <summary>The tasks, and one — standing for the line it is written on.</summary>
    public const string Tasks = "Tasks";
    public const string Task = "Task";

    /// <summary>Who takes part in a task: a mark for each, standing for where they are named on its line.</summary>
    public const string Actors = "Actors";
    public const string Actor = "Actor";

    /// <summary>What is written on the journey: a section's name, what a task says, and an actor's name in the legend.</summary>
    public const string Name = "Name";
    public const string Says = "Says";
}

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
internal sealed class JourneyBuilder : MermaidBuilder<JourneyDiagram>
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

    private JourneyBuilder(EditState state, DiagramLaying laying) : base(state, laying) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(EditState state, DiagramLaying laying) => new JourneyBuilder(state, laying).Lay();

    /// <inheritdoc/>
    protected override JourneyDiagram Of(MermaidBlock block) => JourneyDiagram.Of(block);

    protected override Size Draw(JourneyDiagram diagram, LayoutBuilder build)
    {
        // A journey with no tasks written in it is the source.
        if (diagram.Empty) return AsWritten(build);

        var config = diagram.Config;
        var (wide, tall) = (config.Width ?? Wide, config.Height ?? Tall);
        var apart = config.BoxMargin ?? Apart;

        var tasks = diagram.Sections.SelectMany(section => section.Tasks.Select(task => (Task: task, Section: section))).ToList();

        var legend = Legend(diagram);
        var lane = legend.Size.Height + (legend.Size.Height > 0 ? Gap : 0) + (diagram.Sectioned ? Band + Gap : 0);
        var banded = legend.Size.Height + (legend.Size.Height > 0 ? Gap : 0);
        var top = lane + (JourneyGrammar.Best * Step) + Gap;

        var boxes = tasks.Select((_, at) => new Rect(at * (wide + apart), top, wide, tall)).ToList();

        var room = new DiagramRoom();
        room.Reach(new Rect(0, 0, Math.Max(legend.Size.Width, boxes[^1].Right), top + tall));

        Sections(build, diagram, tasks, boxes, banded, room);
        Faces(build, tasks, boxes, lane, room);
        Tasks(build, diagram, tasks, boxes, config.TaskFontSize ?? TaskSize);

        legend.Draw(build, new Point(0, 0));

        return room.Size;
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    /// <summary>The actors along the top, each a mark in the colour their tasks carry and their name as written.</summary>
    private DiagramLegend Legend(JourneyDiagram diagram)
    {
        var first = diagram.Tasks
            .SelectMany(task => task.Actors)
            .GroupBy(actor => actor.Order)
            .OrderBy(actor => actor.Key)
            .Select(actor => actor.First())
            .ToList();

        return new DiagramLegend(
            [.. first.Select(actor => new DiagramKey(actor.Part, Colour(diagram, actor.Order), [Written(actor.Says, hole: null, ActorSize, Palette.Text)]))],
            [JourneyPiece.Name], across: true, Palette.CodeBorder);
    }

    /// <summary>A band over each run of tasks the same section groups.</summary>
    private void Sections(LayoutBuilder build, JourneyDiagram diagram, IReadOnlyList<(JourneyTask Task, JourneySection Section)> tasks,
                          IReadOnlyList<Rect> boxes, double top, DiagramRoom room)
    {
        if (!diagram.Sectioned) return;

        build.Open(JourneyPiece.Sections, part: null, stops: Stops.None);

        foreach (var (order, at, _, run) in DiagramBand.Runs(tasks, task => task.Section.Order, boxes))
        {
            if (tasks[at].Section.Name is not { } name) continue;

            var band = new Rect(run.Left, top, run.Width, Band);
            var fill = Ink.Series(order, diagram.Config.SectionFill(order));
    var words = Says(name.Says, name.Hole, NameSize, Palette.Text, Math.Max(20, band.Width - (Pad * 2)), FontWeights.SemiBold);

            room.Reach(band);
            DiagramShapes.Draw(build, JourneyPiece.Section, tasks[at].Section.Part, DiagramShape.Rounded, band,
                DiagramInk.Faded(fill, Banded), new DiagramStroke(fill),
                DiagramWords.Placed(words, Rect.Inflate(band, -Pad, -Pad), JourneyPiece.Name));
        }

        build.Close();
    }

    /// <summary>Each task's face, floating as high as it scored, on a thread down to the task itself.</summary>
    private void Faces(LayoutBuilder build, IReadOnlyList<(JourneyTask Task, JourneySection Section)> tasks, IReadOnlyList<Rect> boxes,
                       double lane, DiagramRoom room)
    {
        build.Open(JourneyPiece.Faces, part: null, stops: Stops.None);

        for (var at = 0; at < tasks.Count; at++)
        {
            var task = tasks[at].Task;
            var middle = new Point(boxes[at].Left + (boxes[at].Width / 2), lane + ((JourneyGrammar.Best - task.Height) * Step) + Face);

            DiagramConnector.Draw(build, JourneyPiece.Thread, task.Part, [new Point(middle.X, middle.Y + Face), new Point(middle.X, boxes[at].Top)],
                new DiagramStroke(DiagramInk.Faded(Palette.TextMuted, 0.6), 1, DiagramStroke.Dotted), end: DiagramHead.None);

            room.Reach(new Rect(middle.X - Face, middle.Y - Face, Face * 2, Face * 2));
            Drawn(build, task, middle);
        }

        build.Close();
    }

    /// <summary>One face: a disc in the colour of its mood, two eyes, and a mouth that smiles, flattens or frowns.</summary>
    private void Drawn(LayoutBuilder build, JourneyTask task, Point middle)
    {
        var mood = task.Mood switch
        {
            JourneyMood.Happy => Palette.Success,
            JourneyMood.Neutral => Palette.Warning,
            _ => Palette.Danger,
        };

        var disc = new EllipseGeometry(middle, Face, Face);
        disc.Freeze();

        var eyes = new GeometryGroup
        {
            Children =
            {
                new EllipseGeometry(new Point(middle.X - 4.5, middle.Y - 3.5), 1.6, 1.6),
                new EllipseGeometry(new Point(middle.X + 4.5, middle.Y - 3.5), 1.6, 1.6),
            },
        };
        eyes.Freeze();

        var mouth = task.Mood switch
        {
            JourneyMood.Happy => DiagramCurve.Bowed(new Point(middle.X - 5, middle.Y + 2), new Point(middle.X, middle.Y + 9), new Point(middle.X + 5, middle.Y + 2)),
            JourneyMood.Sad => DiagramCurve.Bowed(new Point(middle.X - 5, middle.Y + 6), new Point(middle.X, middle.Y - 1), new Point(middle.X + 5, middle.Y + 6)),
            _ => Frozen(new LineGeometry(new Point(middle.X - 5, middle.Y + 4), new Point(middle.X + 5, middle.Y + 4))),
        };

        // The face stands for what scores it, so a press on it means the score — or the task's line, where nothing scores it.
        build.Open(JourneyPiece.Face, task.Scored ?? task.Part, stops: Stops.None);
        build.Draw(new GeometryMark(disc, DiagramInk.Faded(mood, Faced), Palette.Text, 1.2));
        build.Draw(new GeometryMark(eyes, Palette.Text, null, 0));
        build.Draw(new GeometryMark(mouth, null, Palette.Text, 1.4));
        build.Occupies(disc);
        build.Close();
    }

    /// <summary>The tasks in a row, each saying what it is, with a mark for everyone taking part in it.</summary>
    private void Tasks(LayoutBuilder build, JourneyDiagram diagram, IReadOnlyList<(JourneyTask Task, JourneySection Section)> tasks,
                       IReadOnlyList<Rect> boxes, double size)
    {
        build.Open(JourneyPiece.Tasks, part: null, stops: Stops.None);

        for (var at = 0; at < tasks.Count; at++)
        {
            var (task, section) = tasks[at];
            var box = boxes[at];
            var fill = Ink.Series(section.Order, diagram.Config.SectionFill(section.Order));
            var words = Says(task.Says.Says, task.Says.Hole, size, Palette.Text, Math.Max(20, box.Width - (Pad * 2)));

            DiagramShapes.Draw(build, JourneyPiece.Task, task.Part, DiagramShape.Rounded, box,
                DiagramInk.Faded(fill, Tinted), new DiagramStroke(fill),
                DiagramWords.Placed(words, Rect.Inflate(box, -Pad, -Pad), JourneyPiece.Says));

            Actors(build, diagram, task, box);
        }

        build.Close();
    }

    /// <summary>Everyone taking part in a task, as marks along the top of its box in the colour each carries.</summary>
    private void Actors(LayoutBuilder build, JourneyDiagram diagram, JourneyTask task, Rect box)
    {
        if (task.Actors.Count == 0) return;

        build.Open(JourneyPiece.Actors, part: null, stops: Stops.None);

        for (var at = 0; at < task.Actors.Count; at++)
        {
            var actor = task.Actors[at];
            var middle = new Point(box.Left + Mark + Beside + (at * ((Mark * 2) + Beside)), box.Top);

            var dot = new EllipseGeometry(middle, Mark, Mark);
            dot.Freeze();

            build.Open(JourneyPiece.Actor, actor.Part, stops: Stops.None);
            build.Draw(new GeometryMark(dot, Colour(diagram, actor.Order), Palette.CodeBg, 1.2));
            build.Occupies(dot);
            build.Close();
        }

        build.Close();
    }

    // ── Ink ─────────────────────────────────────────────────────────────────

    /// <summary>What an actor is drawn in: the colour written for them, or one of the theme's, round from the sections'.</summary>
    private Brush Colour(JourneyDiagram diagram, int actor) => Ink.Series(actor + Round, diagram.Config.ActorColour(actor));

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
