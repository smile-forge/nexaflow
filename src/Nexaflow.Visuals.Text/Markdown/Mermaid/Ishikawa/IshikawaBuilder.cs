using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Ishikawa;

/// <summary>The pieces an ishikawa diagram's layout is made of — its layers, and what is in them.</summary>
public static class IshikawaPiece
{
    /// <summary>The fish's head, standing for the event, with what it says in it.</summary>
    public const string Head = "Head";

    /// <summary>The causes of the event, each in its box at the end of its bone.</summary>
    public const string Causes = "Causes";
    public const string Cause = "Cause";

    /// <summary>What every cause further in says, at the end of its bone.</summary>
    public const string Words = "Words";
    public const string Label = "Label";

    /// <summary>The spine, standing for the event, and a bone — standing for its cause — for every cause.</summary>
    public const string Bones = "Bones";
    public const string Spine = "Spine";
    public const string Bone = "Bone";
}

/// <summary>
/// Draws an <c>ishikawa</c> block as Mermaid does: the event in the fish's head on the right, a spine running left from it, the
/// event's causes on bones slanting off the spine above and below in turn, each boxed at its bone's end, and every cause further
/// in on a bone of its own off its parent's — level where its parent's slants, slanting where its parent's is level.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> The head and the spine stand for the event's line, a bone and
/// its box or words for its cause's, and the words are the characters written, typed into where they are drawn. How long a side's
/// bones are is shared out by how many causes each side holds, and the causes under a bone are spaced evenly along it.
/// </para>
/// </summary>
internal sealed class IshikawaBuilder : MermaidBuilder<IshikawaChart>
{
    private const double CauseSize = 13;

    /// <summary>How long a side's bones are between them, before the causes on them share it out.</summary>
    private const double SpineLength = 250;

    /// <summary>How long a level bone is with nothing under it, and with causes under it — longer for each.</summary>
    private const double Stub = 30;
    private const double Stem = 60;
    private const double PerCause = 5;

    /// <summary>How far a bone slants from the spine.</summary>
    private static readonly double Angle = 82 * Math.PI / 180;

    /// <summary>How far the first pair of bones stands left of the head.</summary>
    private const double Neck = 20;

    /// <summary>The room round a cause's words in its box, across and up.</summary>
    private const double BoxAcross = 20;
    private const double BoxUp = 2;

    /// <summary>The room between words and the end of their bone.</summary>
    private const double Gap = 4;

    /// <summary>How many characters Mermaid wraps the event's words at, and a cause's.</summary>
    private const int HeadLetters = 13;
    private const int CauseLetters = 15;

    private IshikawaBuilder(ContentReading reading, DiagramLaying laying) : base(reading, laying) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(ContentReading reading, DiagramLaying laying) => new IshikawaBuilder(reading, laying).Lay();

    /// <inheritdoc/>
    protected override IshikawaChart Of(MermaidBlock block) => IshikawaChart.Of(block);

    protected override Size Draw(IshikawaChart chart, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source.
        if (chart.Effect is not { } effect) return AsWritten(build);

        var config = chart.Config;
        var fish = new Fish(Ink.Written(config.LineColour) ?? Palette.TextMuted, Ink.Written(config.Background) ?? Palette.CodeBg,
                            Ink.Written(config.TextColour) ?? Palette.Text, config.FontSize ?? CauseSize, config.DiagramPadding ?? 0);

        // The head, its flat side on the spine's end.
        var said = Said(fish, effect.Says, fish.Size + 1, FontWeights.SemiBold, HeadLetters);
        var (wide, tall) = (Math.Max(60, said.Size.Width + 6), Math.Max(40, (said.Size.Height * 2) + 40));
        fish.Head = (effect, said.Lines, said.Size, wide, tall);
        fish.Reach(new Rect(0, -tall / 2, wide * 1.2, tall));

        // The event's causes above the spine and below it in turn, each side's length shared out by the causes it holds.
        var causes = effect.Causes;
        var (upper, lower) = (Side(causes, 0), Side(causes, 1));
        var (above, below) = (SpineLength, SpineLength);
        if (upper.Total + lower.Total > 0)
        {
            above = Math.Max(SpineLength * 0.3, SpineLength * 2 * upper.Total / (upper.Total + lower.Total));
            below = Math.Max(SpineLength * 0.3, SpineLength * 2 * lower.Total / (upper.Total + lower.Total));
        }

        above = Math.Max(above, upper.Most * fish.Size * 2);
        below = Math.Max(below, lower.Most * fish.Size * 2);

        // A pair of bones leaves the spine together; the next pair leaves it past everything the last one drew.
        var along = causes.Count > 0 ? -Neck : 0;
        for (var pair = 0; pair < causes.Count; pair += 2)
        {
            var from = fish.Drawn;
            Branch(fish, causes[pair], new Point(along, 0), -1, above);
            if (pair + 1 < causes.Count) Branch(fish, causes[pair + 1], new Point(along, 0), 1, below);

            along = Math.Min(along, fish.Left(from));
        }

        fish.Spine = (new Point(along, 0), new Point(0, 0));
        fish.Reach(new Rect(along, 0, -along, 0));

        var shift = fish.Shift;

        Head(build, fish, shift);
        Causes(build, fish, shift);
        Labels(build, fish, shift);
        Bones(build, fish, effect, shift);

        return fish.Taken;
    }

    /// <summary>
    /// What a cause says, wrapped as Mermaid wraps it — at most <paramref name="letters"/> characters to a line — and how much room
    /// its lines take together.
    /// </summary>
    private (IReadOnlyList<DiagramWords> Lines, Size Size) Said(Fish fish, ContentPart says, double size, FontWeight? weight, int letters)
    {
        var room = Worked(new string('x', letters), null, size, fish.Text).Width;
        var lines = Wrapped(says, null, size, fish.Text, room, weight);

        return (lines, new Size(lines.Max(line => line.Width), lines.Sum(line => line.Height)));
    }

    /// <summary>A cause's bone off the spine, its box at the bone's end, and every cause under it along the bone.</summary>
    private void Branch(Fish fish, IshikawaCause cause, Point start, int direction, double length)
    {
        var reach = length * (cause.Causes.Count > 0 ? 1 : 0.2);
        var slant = new Vector(-Math.Cos(Angle) * reach, Math.Sin(Angle) * reach * direction);
        var end = start + slant;

        var words = Said(fish, cause.Says, fish.Size, null, CauseLetters);
        var box = new Rect(end.X - (words.Size.Width / 2) - BoxAcross, end.Y + (11 * direction) - (words.Size.Height / 2) - BoxUp,
                           words.Size.Width + (BoxAcross * 2), words.Size.Height + (BoxUp * 2));

        // The bone stops at the box, so what is pressed in the box is the box.
        fish.Boxes.Add((cause, words.Lines, box));
        fish.Bones.Add((cause, DiagramShapes.Edge(DiagramShape.Rectangle, box, start), start, 2));
        fish.Reach(box);
        fish.Reach(new Rect(start, end));

        if (cause.Causes.Count == 0) return;

        var entries = new List<(IshikawaCause Cause, int Depth, int Parent)>();
        var order = new List<int>();
        Flatten(cause.Causes, -1, 2, direction, entries, order);

        var heights = new double[entries.Count];
        for (var slot = 0; slot < order.Count; slot++)
            heights[order[slot]] = start.Y + (slant.Y * (slot + 1) / (entries.Count + 1));

        var bones = new Dictionary<int, Bone> { [-1] = new(start, end, cause.Causes.Count) };
        for (var index = 0; index < entries.Count; index++)
        {
            var (under, depth, parent) = entries[index];
            var y = heights[index];
            var of = bones[parent];
            var said = Said(fish, under.Says, fish.Size, null, CauseLetters);
            Point near, far;
            Rect at;

            if (depth % 2 == 0)
            {
                // Level, off the parent's slant where it reaches this cause's height, reaching left.
                var rise = of.End.Y - of.Start.Y;
                near = new Point(Lerp(of.Start.X, of.End.X, rise == 0 ? 0.5 : (y - of.Start.Y) / rise), y);
                far = new Point(near.X - (under.Causes.Count > 0 ? Stem + (under.Causes.Count * PerCause) : Stub), y);
                at = new Rect(far.X - Gap - said.Size.Width, y - (said.Size.Height / 2), said.Size.Width, said.Size.Height);
            }
            else
            {
                // Slanting, off the parent's level bone at the next of its causes' evenly spaced places, to this cause's height.
                var taken = of.Taken++;
                near = new Point(Lerp(of.Start.X, of.End.X, (double)(of.Causes - taken) / (of.Causes + 1)), of.Start.Y);
                far = new Point(near.X - (Math.Cos(Angle) * ((y - near.Y) / (Math.Sin(Angle) * direction))), y);
                at = new Rect(far.X - Gap - said.Size.Width, direction < 0 ? y - said.Size.Height : y, said.Size.Width, said.Size.Height);
            }

            fish.Labels.Add((under, said.Lines, at));
            fish.Bones.Add((under, far, near, 1));
            fish.Reach(at);
            fish.Reach(new Rect(near, far));

            if (under.Causes.Count > 0) bones[index] = new Bone(near, far, under.Causes.Count);
        }
    }

    /// <summary>
    /// The causes under a bone in the order they are drawn, and the order they take its height in: a level bone's cause nearer the
    /// spine than its own causes, a slanting bone's further. Above the spine, each bone's causes go the other way round.
    /// </summary>
    private static void Flatten(IReadOnlyList<IshikawaCause> causes, int parent, int depth, int direction,
                                List<(IshikawaCause Cause, int Depth, int Parent)> entries, List<int> order)
    {
        foreach (var cause in direction < 0 ? causes.Reverse() : causes)
        {
            var index = entries.Count;
            entries.Add((cause, depth, parent));

            if (depth % 2 == 0) order.Add(index);
            if (cause.Causes.Count > 0) Flatten(cause.Causes, index, depth + 1, direction, entries, order);
            if (depth % 2 != 0) order.Add(index);
        }
    }

    /// <summary>How many causes every other one of the event's causes holds — from <paramref name="first"/> — in all, and at most.</summary>
    private static (int Total, int Most) Side(IReadOnlyList<IshikawaCause> causes, int first)
    {
        var held = causes.Where((_, index) => index % 2 == first).Select(cause => cause.Descendants).ToList();
        return (held.Sum(), held.DefaultIfEmpty(0).Max());
    }

    private static double Lerp(double from, double to, double at) => from + ((to - from) * at);

    private static void Head(LayoutBuilder build, Fish fish, Vector shift)
    {
        var (effect, lines, words, wide, tall) = fish.Head;
        var top = new Point(shift.X, shift.Y - (tall / 2));
        var figure = new PathFigure { StartPoint = top, IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(shift.X, shift.Y + (tall / 2)), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(shift.X + (wide * 2.4), shift.Y), top, true));
        var outline = new PathGeometry([figure]);
        outline.Freeze();

        var room = new Rect(shift.X + 6, shift.Y - (words.Height / 2), words.Width, words.Height);

        build.Open(IshikawaPiece.Head, effect.Part, stops: Stops.None);
        build.Open(MermaidPiece.Shape, effect.Part, stops: Stops.None);
        build.Draw(new GeometryMark(outline, fish.Fill, fish.Line, 2));
        build.Occupies(DiagramShapes.Clear(outline, room));
        build.Close();

        foreach (var (line, at) in DiagramWords.Stack(lines, room)) line.Set(build, at, MermaidPiece.Words);
        build.Close();
    }

    private void Causes(LayoutBuilder build, Fish fish, Vector shift)
    {
        build.Open(IshikawaPiece.Causes, part: null, stops: Stops.None);

        foreach (var (cause, lines, box) in fish.Boxes)
        {
            var bounds = Rect.Offset(box, shift);
            var words = DiagramWords.Placed(lines, bounds, MermaidPiece.Words);

            DiagramShapes.Draw(build, IshikawaPiece.Cause, cause.Part, DiagramShape.Rectangle, bounds, fish.Fill, new DiagramStroke(fish.Line, 2), words);
        }

        build.Close();
    }

    private static void Labels(LayoutBuilder build, Fish fish, Vector shift)
    {
        build.Open(IshikawaPiece.Words, part: null, stops: Stops.None);

        // A cause's words are set against the end of its bone, as Mermaid sets them.
        foreach (var (_, lines, at) in fish.Labels)
            foreach (var (line, place) in DiagramWords.Stack(lines, Rect.Offset(at, shift), TextAlignment.Right))
                line.Set(build, place, IshikawaPiece.Label);

        build.Close();
    }

    private static void Bones(LayoutBuilder build, Fish fish, IshikawaCause effect, Vector shift)
    {
        build.Open(IshikawaPiece.Bones, part: null, stops: Stops.None);

        var (from, to) = fish.Spine;
        if (to.X - from.X > 0)
            DiagramConnector.Draw(build, IshikawaPiece.Spine, effect.Part, [from + shift, to + shift], new DiagramStroke(fish.Line, 2), end: DiagramHead.None);

        foreach (var (cause, far, near, thickness) in fish.Bones)
            if ((near - far).Length > 0)
                DiagramConnector.Draw(build, IshikawaPiece.Bone, cause.Part, [far + shift, near + shift], new DiagramStroke(fish.Line, thickness));

        build.Close();
    }

    /// <summary>A bone causes hang off: where it leaves its parent, where it ends, how many causes it holds, and how many are placed.</summary>
    private sealed class Bone(Point start, Point end, int causes)
    {
        public Point Start { get; } = start;
        public Point End { get; } = end;
        public int Causes { get; } = causes;
        public int Taken { get; set; }
    }

    /// <summary>Everything the diagram draws, where it draws it before it is moved clear of the edges, and the inks it draws with.</summary>
    private sealed class Fish(Brush line, Brush fill, Brush text, double size, double padding)
    {
        private readonly DiagramRoom _room = new(padding);

        public Brush Line { get; } = line;
        public Brush Fill { get; } = fill;
        public Brush Text { get; } = text;
        public double Size { get; } = size;

        public (IshikawaCause Effect, IReadOnlyList<DiagramWords> Lines, Size Words, double Wide, double Tall) Head { get; set; }
        public (Point From, Point To) Spine { get; set; }

        public List<(IshikawaCause Cause, IReadOnlyList<DiagramWords> Lines, Rect Box)> Boxes { get; } = [];
        public List<(IshikawaCause Cause, IReadOnlyList<DiagramWords> Lines, Rect At)> Labels { get; } = [];
        public List<(IshikawaCause Cause, Point Far, Point Near, double Thickness)> Bones { get; } = [];

        /// <summary>What moves everything drawn inside the box the diagram takes, and how big that box is.</summary>
        public Vector Shift => _room.Shift;
        public Size Taken => _room.Size;

        /// <summary>How much has been drawn so far — to ask later how far left what was drawn since reaches.</summary>
        public int Drawn => _room.Count;

        public void Reach(Rect rect) => _room.Reach(rect);

        /// <summary>How far left what was drawn since <paramref name="from"/> reaches.</summary>
        public double Left(int from) => _room.Left(from);
    }
}
