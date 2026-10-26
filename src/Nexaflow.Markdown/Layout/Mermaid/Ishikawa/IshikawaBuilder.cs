using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Ishikawa;

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
internal sealed class IshikawaBuilder : ContentBuilder
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

    /// <summary>How big a fine bone's arrowhead is against a cause's: Mermaid sizes its heads by the line they end.</summary>
    private const double FineHeads = 0.5;

    /// <summary>The room between words and the end of their bone.</summary>
    private const double Gap = 4;

    /// <summary>How many characters Mermaid wraps the event's words at, and a cause's.</summary>
    private const int HeadLetters = 13;
    private const int CauseLetters = 15;

    internal IshikawaBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                     LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private DiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private DiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), null, null);
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    /// <summary>The event, or one cause of it: the line as written — what pressing it means — what it says, and the causes under it.</summary>
    private sealed class Cause(ContentPart part, ContentPart says)
    {
        public ContentPart Part { get; } = part;

        public ContentPart Says { get; } = says;

        /// <summary>The causes written under it, in the order they are written.</summary>
        public List<Cause> Causes { get; } = [];

        /// <summary>How many causes are under it, however deep.</summary>
        public int Descendants => Causes.Sum(cause => 1 + cause.Descendants);
    }

    /// <summary>
    /// The event the diagram is about — the fish's head — with the causes of it as their indentation nests them, or null where
    /// nothing is written.
    ///
    /// <para>
    /// The nesting is Mermaid's. The first line is the event, however far it is indented. The first cause's indentation is
    /// where causes start, so the event may be indented more or less than they are; each later line is under the nearest line
    /// before it indented less, and a line indented less than the first cause is a cause of the event itself.
    /// </para>
    /// </summary>
    private Cause? Read()
    {
        var lines = Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == IshikawaKinds.Cause && part.Words() is { Length: > 0 })
            .Select(part => (part.Indent(), part))
            .ToList();

        // The event's own indentation counts for nothing, since it may be written further in than its causes.
        var nested = MermaidOutline.Nested(lines, floor: true);
        var causes = new List<Cause>(nested.Count);
        Cause? effect = null;

        foreach (var (part, parent) in nested.Select(line => (line.Item, line.Parent)))
        {
            var cause = new Cause(part, part.Words()!);
            causes.Add(cause);

            if (parent is { } over) causes[over].Causes.Add(cause);
            else effect ??= cause;
        }

        return effect;
    }

    private LayoutSize Draw(MermaidBlock block, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source.
        if (Read() is not { } effect) return Sheet.AsWritten();

        var config = DiagramConfig.Of(Reading, IshikawaConfig.Default);
        if (config.SingleBone) return SingleBoned(config, effect, build);

        var fish = new Fish(Ink.Written(config.LineColour) ?? Style.TextMuted, Ink.Written(config.Background) ?? Style.CodeBg,
                            Ink.Written(config.TextColour) ?? Style.Text, config.FontSize ?? CauseSize, config.DiagramPadding ?? 0);

        // The head, its flat side on the spine's end.
        var said = Said(fish, effect.Says, fish.Size + 1, 600, HeadLetters);
        var (wide, tall) = (Math.Max(60, said.Size.Width + 6), Math.Max(40, (said.Size.Height * 2) + 40));
        fish.Head = (effect, said.Lines, said.Size, wide, tall);
        fish.Reach(new LayoutRect(0, -tall / 2, wide * 1.2, tall));

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
            Branch(fish, causes[pair], new LayoutPoint(along, 0), -1, above);
            if (pair + 1 < causes.Count) Branch(fish, causes[pair + 1], new LayoutPoint(along, 0), 1, below);

            along = Math.Min(along, fish.Left(from));
        }

        fish.Spine = (new LayoutPoint(along, 0), new LayoutPoint(0, 0));
        fish.Reach(new LayoutRect(along, 0, -along, 0));

        var shift = fish.Shift;

        Head(build, fish, shift);
        Causes(build, fish, shift);
        Labels(build, fish, shift);
        Bones(build, fish, effect, shift);

        return fish.Taken;
    }

    /// <summary>
    /// Draws the diagram as Nexaflow's single bones (<c>singleBone</c>): the spine running right into the event in an accented box;
    /// each of the event's causes on one short bone off it, above and below in turn, named in a chip of its own colour; and
    /// everything under a cause listed beside a stem running out from its chip, each level further in and quieter than the last.
    /// </summary>
    private LayoutSize SingleBoned(IshikawaConfig config, Cause effect, LayoutBuilder build)
    {
        var size = config.FontSize ?? CauseSize;
        var line = Ink.Written(config.LineColour) ?? Style.TextMuted;
        var text = Ink.Written(config.TextColour) ?? Style.Text;
        var room = new DiagramRoom(config.DiagramPadding ?? 0);

        // Each cause's chip and outline first, since they say how wide its slot along the spine is.
        var along = 0.0;
        var clusters = new List<Cluster>();
        for (var index = 0; index < effect.Causes.Count; index++)
        {
            var cause = effect.Causes[index];
            var name = Words.Wrapped(cause.Says, null, size, text, Widest, 600);
            var chip = new LayoutSize(name.Max(said => said.Width) + (2 * ChipAcross), name.Sum(said => said.Height) + (2 * ChipUp));

            var rows = new List<Row>();
            Listed(cause.Causes, 1, size - 1, text, rows);
            var outline = new LayoutSize(rows.Select(row => Indented(row.Depth) + row.Size.Width).DefaultIfEmpty(0).Max(),
                                         rows.Sum(row => row.Size.Height + RowGap));

            var wide = Math.Max(Math.Max(chip.Width, outline.Width), Narrowest) + Between;
            clusters.Add(new Cluster(cause, Ink.Series(index), name, chip, rows, outline, along + (wide / 2), index % 2 == 0 ? -1 : 1));
            along += wide;
        }

        var said = Words.Wrapped(effect.Says, null, size + 1, text, Widest, 600);
        var head = new LayoutRect(along + HeadGap, 0, said.Max(one => one.Width) + (2 * HeadAcross), said.Sum(one => one.Height) + (2 * HeadUp));
        head = new LayoutRect(head.X, -head.Height / 2, head.Width, head.Height);
        room.Reach(head);
        room.Reach(new LayoutRect(0, 0, head.Left, 0));

        // Where every cause's bone, chip, stem and outline go.
        foreach (var cluster in clusters)
        {
            var sign = cluster.Side;
            cluster.Tip = new LayoutPoint(cluster.Middle - Lean, sign * Rise);
            cluster.Box = new LayoutRect(cluster.Tip.X - (cluster.Chip.Width / 2), sign < 0 ? cluster.Tip.Y - cluster.Chip.Height : cluster.Tip.Y, cluster.Chip.Width, cluster.Chip.Height);

            var left = cluster.Tip.X - (Math.Max(cluster.Chip.Width, cluster.Outline.Width) / 2);
            var y = sign < 0 ? cluster.Box.Top - Drop - cluster.Outline.Height : cluster.Box.Bottom + Drop;
            foreach (var row in cluster.Rows)
            {
                row.At = new LayoutPoint(left + Indented(row.Depth), y);
                row.Dot = new LayoutPoint(row.At.X - Bullet, y + (row.Lines[0].Height / 2));
                y += row.Size.Height + RowGap;
            }

            room.Reach(cluster.Box);
            room.Reach(new LayoutRect(new LayoutPoint(cluster.Middle, 0), cluster.Tip));
            foreach (var row in cluster.Rows) room.Reach(new LayoutRect(row.Dot.X - Bullet, row.At.Y, row.Size.Width + (2 * Bullet), row.Size.Height));
        }

        var shift = room.Shift;

        build.Open(IshikawaPiece.Bones, part: null, stops: LayoutStops.None);
        DiagramConnector.Draw(build, IshikawaPiece.Spine, effect.Part, [new LayoutPoint(0, 0) + shift, new LayoutPoint(head.Left, 0) + shift], new DiagramStroke(line, 2));
        foreach (var cluster in clusters) Boned(build, cluster, shift);
        build.Close();

        var accent = Style.Accent;
        var box = LayoutRect.Offset(head, shift);
        DiagramShapes.Draw(build, IshikawaPiece.Head, effect.Part, DiagramShape.Rounded, box, accent, new DiagramStroke(accent, 1.5),
                                   DiagramWords.Placed(Words.Wrapped(effect.Says, null, size + 1, Ink.Over(accent), Widest, 600), box, MermaidPiece.Words));

        build.Open(IshikawaPiece.Causes, part: null, stops: LayoutStops.None);
        foreach (var cluster in clusters)
        {
            var bounds = LayoutRect.Offset(cluster.Box, shift);
            DiagramShapes.Draw(build, IshikawaPiece.Cause, cluster.Cause.Part, DiagramShape.Rounded, bounds,
                                       DiagramInk.Faded(build.Medium, cluster.Ink, ChipWash), new DiagramStroke(cluster.Ink, 1.5), DiagramWords.Placed(cluster.Name, bounds, MermaidPiece.Words));
        }

        build.Close();

        build.Open(IshikawaPiece.Words, part: null, stops: LayoutStops.None);
        foreach (var row in clusters.SelectMany(cluster => cluster.Rows))
            foreach (var (words, at) in DiagramWords.Stack(row.Lines, new LayoutRect(row.At + shift, row.Size), LayoutAlign.Left))
                words.Set(build, at, IshikawaPiece.Label);
        build.Close();

        return room.Size;
    }

    /// <summary>
    /// A cause's bone off the spine, the stem out from its chip past every cause directly under it, and a dot for each cause
    /// listed — each standing for its cause.
    /// </summary>
    private void Boned(LayoutBuilder build, Cluster cluster, LayoutVector shift)
    {
        DiagramConnector.Draw(build, IshikawaPiece.Bone, cluster.Cause.Part, [new LayoutPoint(cluster.Middle, 0) + shift, cluster.Tip + shift],
                                      new DiagramStroke(cluster.Ink, 2), end: DiagramHead.None);

        var firsts = cluster.Rows.Where(row => row.Depth == 1).ToList();
        if (firsts.Count > 0)
        {
            var edge = cluster.Side < 0 ? cluster.Box.Top : cluster.Box.Bottom;
            var furthest = cluster.Side < 0 ? firsts[0] : firsts[^1];
            DiagramConnector.Draw(build, IshikawaPiece.Bone, cluster.Cause.Part, [new LayoutPoint(furthest.Dot.X, edge) + shift, furthest.Dot + shift],
                                          new DiagramStroke(DiagramInk.Faded(build.Medium, cluster.Ink, StemWash), 1), end: DiagramHead.None);
        }

        foreach (var row in cluster.Rows)
        {
            var (radius, fill, stroke) = row.Depth switch
            {
                1 => (3.0, cluster.Ink, (LayoutInk?)null),
                2 => (2.5, Ink.Surface, cluster.Ink),
                _ => (1.8, Style.TextMuted, null),
            };

            var dot = build.Medium.Ellipse(row.Dot + shift, radius, radius);

            build.Open(IshikawaPiece.Bone, row.Cause.Part, stops: LayoutStops.None);
            build.Draw(DiagramMarks.Outlined(build.Medium, dot, fill, stroke, stroke is null ? 0 : 1.2));
            build.Occupies(dot);
            build.Close();
        }
    }

    /// <summary>Every cause under a cause, depth first, each wrapped — the first level in the diagram's ink, the rest quieter.</summary>
    private void Listed(IReadOnlyList<Cause> causes, int depth, double size, LayoutInk text, List<Row> rows)
    {
        foreach (var cause in causes)
        {
            var lines = Words.Wrapped(cause.Says, null, size, depth == 1 ? text : Style.TextMuted, Widest - Indented(depth));
            rows.Add(new Row(cause, depth, lines, new LayoutSize(lines.Max(said => said.Width), lines.Sum(said => said.Height))));
            Listed(cause.Causes, depth + 1, size, text, rows);
        }
    }

    /// <summary>How far in a cause's words stand at a depth of the outline: past its dot, and further for each level.</summary>
    private static double Indented(int depth) => (2 * Bullet) + ((depth - 1) * Indent);

    /// <summary>One of the event's causes on its single bone: its chip, its outline, and where they went.</summary>
    private sealed class Cluster(Cause cause, LayoutInk ink, IReadOnlyList<DiagramWords> name, LayoutSize chip, IReadOnlyList<Row> rows, LayoutSize outline, double middle, int side)
    {
        public Cause Cause { get; } = cause;
        public LayoutInk Ink { get; } = ink;
        public IReadOnlyList<DiagramWords> Name { get; } = name;
        public LayoutSize Chip { get; } = chip;
        public IReadOnlyList<Row> Rows { get; } = rows;
        public LayoutSize Outline { get; } = outline;

        /// <summary>Where its bone leaves the spine, and which side of it the bone goes: up is negative.</summary>
        public double Middle { get; } = middle;
        public int Side { get; } = side;

        public LayoutPoint Tip { get; set; }
        public LayoutRect Box { get; set; }
    }

    /// <summary>A cause in an outline: how deep, what it says, and where its words and its dot went.</summary>
    private sealed class Row(Cause cause, int depth, IReadOnlyList<DiagramWords> lines, LayoutSize size)
    {
        public Cause Cause { get; } = cause;
        public int Depth { get; } = depth;
        public IReadOnlyList<DiagramWords> Lines { get; } = lines;
        public LayoutSize Size { get; } = size;
        public LayoutPoint At { get; set; }
        public LayoutPoint Dot { get; set; }
    }

    /// <summary>How far a single bone rises off the spine, and leans back from where it leaves it.</summary>
    private const double Rise = 26;
    private const double Lean = 16;

    /// <summary>How wide a slot along the spine is at least, and the room between slots.</summary>
    private const double Narrowest = 56;
    private const double Between = 26;

    /// <summary>The room round a chip's words, and round the event's in its box, and how far the box stands off the last slot.</summary>
    private const double ChipAcross = 8;
    private const double ChipUp = 4;
    private const double HeadAcross = 12;
    private const double HeadUp = 8;
    private const double HeadGap = 16;

    /// <summary>How wide words run before they wrap, in the single-bone outline.</summary>
    private const double Widest = 180;

    /// <summary>The gap between a chip and its outline, between rows, how far in each level goes, and how much room a dot takes.</summary>
    private const double Drop = 8;
    private const double RowGap = 3;
    private const double Indent = 12;
    private const double Bullet = 6;

    /// <summary>How strongly a chip is washed in its cause's colour, and how faint a stem is drawn.</summary>
    private const double ChipWash = 0.2;
    private const double StemWash = 0.6;

    /// <summary>
    /// What a cause says, wrapped as Mermaid wraps it — at most <paramref name="letters"/> characters to a line — and how much room
    /// its lines take together.
    /// </summary>
    private (IReadOnlyList<DiagramWords> Lines, LayoutSize Size) Said(Fish fish, ContentPart says, double size, int? weight, int letters)
    {
        var room = Words.Worked(new string('x', letters), null, size, fish.Text).Width;
        var lines = Words.Wrapped(says, null, size, fish.Text, room, weight);

        return (lines, new LayoutSize(lines.Max(line => line.Width), lines.Sum(line => line.Height)));
    }

    /// <summary>A cause's bone off the spine, its box at the bone's end, and every cause under it along the bone.</summary>
    private void Branch(Fish fish, Cause cause, LayoutPoint start, int direction, double length)
    {
        var reach = length * (cause.Causes.Count > 0 ? 1 : 0.2);
        var slant = new LayoutVector(-Math.Cos(Angle) * reach, Math.Sin(Angle) * reach * direction);
        var end = start + slant;

        var words = Said(fish, cause.Says, fish.Size, null, CauseLetters);
        var box = new LayoutRect(end.X - (words.Size.Width / 2) - BoxAcross, end.Y + (11 * direction) - (words.Size.Height / 2) - BoxUp,
                                 words.Size.Width + (BoxAcross * 2), words.Size.Height + (BoxUp * 2));

        // The bone stops at the box, so what is pressed in the box is the box.
        fish.Boxes.Add((cause, words.Lines, box));
        fish.Bones.Add((cause, DiagramShapes.Edge(DiagramShape.Rectangle, box, start), start, 2));
        fish.Reach(box);
        fish.Reach(new LayoutRect(start, end));

        if (cause.Causes.Count == 0) return;

        var entries = new List<(Cause Cause, int Depth, int Parent)>();
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
            LayoutPoint near, far;
            LayoutRect at;

            if (depth % 2 == 0)
            {
                // Level, off the parent's slant where it reaches this cause's height, reaching left.
                var rise = of.End.Y - of.Start.Y;
                near = new LayoutPoint(Lerp(of.Start.X, of.End.X, rise == 0 ? 0.5 : (y - of.Start.Y) / rise), y);
                far = new LayoutPoint(near.X - (under.Causes.Count > 0 ? Stem + (under.Causes.Count * PerCause) : Stub), y);
                at = new LayoutRect(far.X - Gap - said.Size.Width, y - (said.Size.Height / 2), said.Size.Width, said.Size.Height);
            }
            else
            {
                // Slanting, off the parent's level bone at the next of its causes' evenly spaced places, to this cause's height.
                var taken = of.Taken++;
                near = new LayoutPoint(Lerp(of.Start.X, of.End.X, (double)(of.Causes - taken) / (of.Causes + 1)), of.Start.Y);
                far = new LayoutPoint(near.X - (Math.Cos(Angle) * ((y - near.Y) / (Math.Sin(Angle) * direction))), y);
                at = new LayoutRect(far.X - Gap - said.Size.Width, direction < 0 ? y - said.Size.Height : y, said.Size.Width, said.Size.Height);
            }

            fish.Labels.Add((under, said.Lines, at));
            fish.Bones.Add((under, far, near, 1));
            fish.Reach(at);
            fish.Reach(new LayoutRect(near, far));

            if (under.Causes.Count > 0) bones[index] = new Bone(near, far, under.Causes.Count);
        }
    }

    /// <summary>
    /// The causes under a bone in the order they are drawn, and the order they take its height in: a level bone's cause nearer the
    /// spine than its own causes, a slanting bone's further. Above the spine, each bone's causes go the other way round.
    /// </summary>
    private static void Flatten(IReadOnlyList<Cause> causes, int parent, int depth, int direction,
                                List<(Cause Cause, int Depth, int Parent)> entries, List<int> order)
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
    private static (int Total, int Most) Side(IReadOnlyList<Cause> causes, int first)
    {
        var held = causes.Where((_, index) => index % 2 == first).Select(cause => cause.Descendants).ToList();
        return (held.Sum(), held.DefaultIfEmpty(0).Max());
    }

    private static double Lerp(double from, double to, double at) => from + ((to - from) * at);

    private static void Head(LayoutBuilder build, Fish fish, LayoutVector shift)
    {
        var (effect, lines, words, wide, tall) = fish.Head;
        var top = new LayoutPoint(shift.X, shift.Y - (tall / 2));
        var pen = build.Medium.Outline();
        pen.Begin(top, filled: true, closed: true);
        pen.LineTo(new LayoutPoint(shift.X, shift.Y + (tall / 2)));
        pen.QuadraticTo(new LayoutPoint(shift.X + (wide * 2.4), shift.Y), top);
        var outline = pen.Close();

        var room = new LayoutRect(shift.X + 6, shift.Y - (words.Height / 2), words.Width, words.Height);

        build.Open(IshikawaPiece.Head, effect.Part, stops: LayoutStops.None);
        build.Open(MermaidPiece.Shape, effect.Part, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(build.Medium, outline, fish.Fill, fish.Line, 2));
        build.Occupies(DiagramShapes.Clear(build.Medium, outline, room));
        build.Close();

        foreach (var (line, at) in DiagramWords.Stack(lines, room)) line.Set(build, at, MermaidPiece.Words);
        build.Close();
    }

    private void Causes(LayoutBuilder build, Fish fish, LayoutVector shift)
    {
        build.Open(IshikawaPiece.Causes, part: null, stops: LayoutStops.None);

        foreach (var (cause, lines, box) in fish.Boxes)
        {
            var bounds = LayoutRect.Offset(box, shift);
            var words = DiagramWords.Placed(lines, bounds, MermaidPiece.Words);

            DiagramShapes.Draw(build, IshikawaPiece.Cause, cause.Part, DiagramShape.Rectangle, bounds, fish.Fill, new DiagramStroke(fish.Line, 2), words);
        }

        build.Close();
    }

    private static void Labels(LayoutBuilder build, Fish fish, LayoutVector shift)
    {
        build.Open(IshikawaPiece.Words, part: null, stops: LayoutStops.None);

        // A cause's words are set against the end of its bone, as Mermaid sets them.
        foreach (var (_, lines, at) in fish.Labels)
            foreach (var (line, place) in DiagramWords.Stack(lines, LayoutRect.Offset(at, shift), LayoutAlign.Right))
                line.Set(build, place, IshikawaPiece.Label);

        build.Close();
    }

    private static void Bones(LayoutBuilder build, Fish fish, Cause effect, LayoutVector shift)
    {
        build.Open(IshikawaPiece.Bones, part: null, stops: LayoutStops.None);

        var (from, to) = fish.Spine;
        if (to.X - from.X > 0)
            DiagramConnector.Draw(build, IshikawaPiece.Spine, effect.Part, [from + shift, to + shift], new DiagramStroke(fish.Line, 2), end: DiagramHead.None);

        foreach (var (cause, far, near, thickness) in fish.Bones)
            if ((near - far).Length > 0)
    DiagramConnector.Draw(build, IshikawaPiece.Bone, cause.Part, [far + shift, near + shift], new DiagramStroke(fish.Line, thickness),
                                      heads: thickness < 2 ? FineHeads : 1);

        build.Close();
    }

    /// <summary>A bone causes hang off: where it leaves its parent, where it ends, how many causes it holds, and how many are placed.</summary>
    private sealed class Bone(LayoutPoint start, LayoutPoint end, int causes)
    {
        public LayoutPoint Start { get; } = start;
        public LayoutPoint End { get; } = end;
        public int Causes { get; } = causes;
        public int Taken { get; set; }
    }

    /// <summary>Everything the diagram draws, where it draws it before it is moved clear of the edges, and the inks it draws with.</summary>
    private sealed class Fish(LayoutInk line, LayoutInk fill, LayoutInk text, double size, double padding)
    {
        private readonly DiagramRoom _room = new(padding);

        public LayoutInk Line { get; } = line;
        public LayoutInk Fill { get; } = fill;
        public LayoutInk Text { get; } = text;
        public double Size { get; } = size;

        public (Cause Effect, IReadOnlyList<DiagramWords> Lines, LayoutSize Words, double Wide, double Tall) Head { get; set; }
        public (LayoutPoint From, LayoutPoint To) Spine { get; set; }

        public List<(Cause Cause, IReadOnlyList<DiagramWords> Lines, LayoutRect Box)> Boxes { get; } = [];
        public List<(Cause Cause, IReadOnlyList<DiagramWords> Lines, LayoutRect At)> Labels { get; } = [];
        public List<(Cause Cause, LayoutPoint Far, LayoutPoint Near, double Thickness)> Bones { get; } = [];

        /// <summary>What moves everything drawn inside the box the diagram takes, and how big that box is.</summary>
        public LayoutVector Shift => _room.Shift;
        public LayoutSize Taken => _room.Size;

        /// <summary>How much has been drawn so far — to ask later how far left what was drawn since reaches.</summary>
        public int Drawn => _room.Count;

        public void Reach(LayoutRect rect) => _room.Reach(rect);

        /// <summary>How far left what was drawn since <paramref name="from"/> reaches.</summary>
        public double Left(int from) => _room.Left(from);
    }
}
