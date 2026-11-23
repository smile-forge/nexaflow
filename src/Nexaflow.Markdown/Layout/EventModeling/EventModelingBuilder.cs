using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Layout.EventModeling;

/// <summary>
/// Lays an <c>eventmodeling</c> block out: its frames along a timeline left to right, each in the swimlane its stage put it in, the
/// arrows from each frame to the frames it feeds, the notes and specifications under the frames they are about, and anything that
/// could not be drawn set as written beneath, with why.
///
/// <para>
/// <strong>Placed as Mermaid places them.</strong> A frame stands after the one before it in its own lane, or overlapping the frame
/// before it where that is in another lane, so the timeline reads left to right however the lanes interleave; a frame's box is as big as
/// its name and its data, within Mermaid's least and most; a lane is as tall as its tallest frame; and an arrow runs from two thirds of
/// the way across the frame feeding to a third of the way across the frame fed, out of the side facing it. What is measured is the
/// builder's; what goes where in the order of things — which lane, which frame feeds which — is the stage's
/// (<see cref="EventModeling.Stages.ResolveTimeline"/>).
/// </para>
/// <para>
/// Every word drawn is the characters written, typed into where it is drawn: a frame's name — less its lane's namespace, which its lane
/// says — its data, a note, a specification's lines. What stands for no characters — a lane's name, the word over a clause — is worked
/// out, and pressed rather than typed into.
/// </para>
/// </summary>
public sealed class EventModelingBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                         LayoutHost host)
    : ContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    // Mermaid's own measures for the diagram.
    private const double LaneLeast = 70;
    private const double LanePadding = 15;
    private const double LaneGap = 10;
    private const double BoxPadding = 10;
    private const double Overlap = 90;
    private const double Narrowest = 80;
    private const double Widest = 450;
    private const double Shortest = 80;
    private const double Tallest = 750;
    private const double TextPadding = 10;
    private const double Measure = Widest - (2 * TextPadding);
    private const double LabelColumn = 250;
    private const double LabelInset = 30;
    private const double Corner = 3;

    /// <summary>The room between a frame's name and its data, and between a title and what it is over.</summary>
    private const double Spacing = 8;

    /// <summary>How long an arrow's head is, and how wide across.</summary>
    private const double HeadLength = 9;
    private const double HeadWidth = 6;

    /// <summary>How much smaller than the words round it data is set, as a code font sets beside a text font.</summary>
    private const double DataScale = 0.9;

    /// <summary>How much larger than the words under it a title is set.</summary>
    private const double TitleScale = 1.3;

    /// <summary>How strongly a type's colour shows in the fill of its frames, where nothing writes the fill.</summary>
    private const double Wash = 0.22;

    private LayoutFace? _text;
    private LayoutFace? _bold;
    private LayoutFace? _mono;

    private LayoutFace Text => _text ??= Style.Face(Style.TextFont);
    private LayoutFace Bold => _bold ??= Style.Face(Style.TextFont, bold: true);
    private LayoutFace Mono => _mono ??= Style.Face(Style.MonoFont);

    protected override LayoutCharacters Characters => new(Mono, Style.TextSize, Style.Text);

    /// <summary>A frame measured: what is drawn on it and how big its box is, and once placed where it stands.</summary>
    private sealed class Framed(ContentPart part, EventModelingFrameNode node, Words name, IReadOnlyList<Words> data, LayoutSize size)
    {
        public ContentPart Part { get; } = part;
        public EventModelingFrameNode Node { get; } = node;
        public Words Name { get; } = name;
        public IReadOnlyList<Words> Data { get; } = data;
        public LayoutSize Size { get; } = size;
        public double X { get; set; }
        public double Y { get; set; }
        public LayoutRect Box => new(X, Y, Size.Width, Size.Height);
    }

    /// <summary>Words to set: their characters as shaped, the part they stand for, and whether they are a hole rather than words.</summary>
    private sealed record Words(IShapedText Shaped, ContentPart? Part, bool Hole, bool Maps = true);

    protected override Laid? Build()
    {
        if (Reading.Root.Node is not EventModelingBlockNode block) return AsSource([]);

        var trouble = Reading.Root.SelfAndDescendants()
                             .Where(part => part.Trouble is not null && !part.Derived)
                             .Select(part => LayoutDiagnostic.Of(part, part.Trouble!))
                             .ToList();

        var children = Reading.Root.Children;
        var data = children.Where(part => part.Node is EventModelingDataNode).ToDictionary(part => ((EventModelingDataNode)part.Node).Key);
        var frames = children.Where(part => part.Node is EventModelingFrameNode)
                             .Select(part => Measured(part, (EventModelingFrameNode)part.Node, data))
                             .OrderBy(frame => frame.Node.Order)
                             .ToList();

        var drawing = new LayoutBuilder(Medium);
        drawing.Open(EventModelingPiece.Diagram, part: null, stops: LayoutStops.None);

        var size = frames.Count == 0 ? new LayoutSize(0, 0) : Timeline(drawing, block, frames);
        size = Notes(drawing, frames, children, size);

        drawing.Close();
        var drawn = drawing.Seal();

        return Sheet(block, drawn, size, children, trouble);
    }

    // ── Measuring ───────────────────────────────────────────────────────────

    /// <summary>A frame's name and data set, and how big its box comes to.</summary>
    private Framed Measured(ContentPart frame, EventModelingFrameNode node, Dictionary<int, ContentPart> data)
    {
        var name = NameOf(frame, node);
        var rows = new List<Words>();

        // The data block the frame names is what it shows, over any written on its row, as Mermaid shows it.
        var value = node.Shows is { } shown && data.TryGetValue(shown, out var block)
            ? Child(block, EventModelingKinds.Value)
            : Child(frame, EventModelingKinds.Value);

        if (value is not null) rows.AddRange(Rows(value, Style.Text));

        var across = Math.Max(Width(name), rows.Count == 0 ? 0 : rows.Max(Width)) + (2 * TextPadding);
        var down = name.Shaped.Height + (rows.Count == 0 ? 0 : Spacing + rows.Sum(row => row.Shaped.Height)) + (2 * TextPadding);

        var size = new LayoutSize(Math.Clamp(across, Narrowest, Widest) + (2 * BoxPadding), Math.Clamp(down, Shortest, Tallest) + (2 * BoxPadding));
        return new Framed(frame, node, name, rows, size);
    }

    /// <summary>What is drawn as a frame's name: its words, less the namespace its lane says — or a hole, where none is written yet.</summary>
    private Words NameOf(ContentPart frame, EventModelingFrameNode node)
    {
        var name = Child(frame, EventModelingKinds.Name);

        if (name?.Children.FirstOrDefault(part => part.Kind == Kinds.Hole) is { } hole) return Hole(hole, Bold);

        var words = name?.Children.Where(part => part.Role == EventModelingRoles.Name && part.Length > 0).ToList() ?? [];
        var said = node.Namespaced ? words[^1] : words.Count == 1 ? words[0] : name;

        var shaped = Medium.Shape(said is null ? string.Empty : said.Print(), Bold, Style.TextSize, Style.Text);
        shaped.Bound(Measure, LayoutAlign.Centre);
        return new Words(shaped, said, Hole: false);
    }

    /// <summary>A value's rows — or a hole, where nothing is written in it yet.</summary>
    private IEnumerable<Words> Rows(ContentPart value, LayoutInk ink)
    {
        if (value.Children.FirstOrDefault(part => part.Kind == Kinds.Hole) is { } hole)
        {
            yield return Hole(hole, Mono);
            yield break;
        }

        foreach (var row in value.Children.Where(part => part.Role == EventModelingRoles.Said))
        {
            var shaped = Medium.Shape(row.Print(), Mono, Style.TextSize * DataScale, ink);
            shaped.Bound(Measure, LayoutAlign.Left);
            yield return new Words(shaped, row, Hole: false);
        }
    }

    /// <summary>A hole, set as small as a letter of <paramref name="face"/>.</summary>
    private Words Hole(ContentPart hole, LayoutFace face) => new(Medium.Shape("x", face, Style.TextSize, Style.TextMuted), hole, Hole: true);

    private static double Width(Words words) => words.Hole ? LayoutText.HoleWidth(words.Shaped) : words.Shaped.Width;

    // ── The timeline ────────────────────────────────────────────────────────

    /// <summary>The lanes, the frames in them and the arrows between them, and how big they come to.</summary>
    private LayoutSize Timeline(LayoutBuilder build, EventModelingBlockNode block, List<Framed> frames)
    {
        var labels = block.Lanes.Select(Label).ToList();
        var column = Math.Max(LabelColumn, labels.Max(label => label.Width) + (2 * LabelInset));

        // Along: after the frame before in its own lane, or overlapping the frame before where that is in another lane.
        var laneRight = new Dictionary<int, double>();
        var right = 0d;
        int? lane = null;

        foreach (var frame in frames)
        {
            frame.X = lane is null ? column
                    : lane == frame.Node.Lane && laneRight.TryGetValue(frame.Node.Lane, out var end) ? end + BoxPadding
                    : right - Overlap + BoxPadding;

            laneRight[frame.Node.Lane] = frame.X + frame.Size.Width;
            right = Math.Max(right, frame.X + frame.Size.Width + BoxPadding);
            lane = frame.Node.Lane;
        }

        // Down: each lane as tall as its tallest frame, one under the next.
        var tops = new double[block.Lanes.Count];
        var heights = new double[block.Lanes.Count];
        var down = 0d;

        for (var at = 0; at < block.Lanes.Count; at++)
        {
            var tallest = frames.Where(frame => frame.Node.Lane == at).Select(frame => frame.Size.Height).DefaultIfEmpty(0).Max();
            tops[at] = down;
            heights[at] = Math.Max(LaneLeast, tallest) + (2 * LanePadding);
            down += heights[at] + LaneGap;
        }

        foreach (var frame in frames) frame.Y = tops[frame.Node.Lane] + LanePadding;

        var wide = right + LanePadding;
        for (var at = 0; at < block.Lanes.Count; at++) Lane(build, block, labels[at], new LayoutRect(0, tops[at], wide, heights[at]));

        foreach (var frame in frames) Frame(build, block, frame);
        foreach (var frame in frames) Feeds(build, block, frame, frames);

        return new LayoutSize(wide, down - LaneGap);
    }

    /// <summary>What a lane is called: its group's name, and the namespace it is for where it is one's.</summary>
    private IShapedText Label(EventModelingLane lane)
    {
        var group = lane.Group switch
        {
            EventModelingGroup.Automation => "Automation",
            EventModelingGroup.Commands => "Commands",
            _ => "Events",
        };

        var said = lane.Namespace is null
            ? Style.Says($"Text.EventModeling.{group}")
            : string.Format(CultureInfo.CurrentCulture, Style.Says($"Text.EventModeling.{group}Of"), lane.Namespace);

        return Medium.Shape(said, Bold, Style.TextSize, Style.Text);
    }

    /// <summary>A lane's band, and its name at its left.</summary>
    private void Lane(LayoutBuilder build, EventModelingBlockNode block, IShapedText label, LayoutRect band)
    {
        build.Open(EventModelingPiece.Lane, part: null, stops: LayoutStops.None);
        build.Draw(new GeometryMark(Medium.Rectangle(band, Corner, Corner), Written(block, "emSwimlaneBackgroundOdd") ?? Style.TableAltRowBg,
                                    Medium.Stroke(Written(block, "emSwimlaneBackgroundStroke") ?? Style.TableBorder, 1)));
        build.Covers(band);
        build.Close();

        LayoutText.Words(build, label, new LayoutPoint(LabelInset, band.Y + LanePadding), Math.Max(1, label.Width), LayoutAlign.Left, part: null,
                         EventModelingPiece.LaneName, maps: false);
    }

    /// <summary>A frame's box in its type's colours, its name in the middle of its top and its data under it.</summary>
    private void Frame(LayoutBuilder build, EventModelingBlockNode block, Framed frame)
    {
        var (fill, stroke, ink) = Paint(block, frame.Node.Type);
        var box = frame.Box;
        var shape = Medium.Rectangle(box, Corner, Corner);

        build.Open(EventModelingPiece.Frame, frame.Part, stops: LayoutStops.None);
        Box(build, box, new GeometryMark(shape, fill, Medium.Stroke(stroke, 1)));

        var inside = box.Width - (2 * (BoxPadding + TextPadding));
        var top = box.Y + BoxPadding + TextPadding;

        Set(build, frame.Name, new LayoutPoint(box.X + BoxPadding + TextPadding, top), inside, LayoutAlign.Centre, EventModelingPiece.Name, ink);
        top += frame.Name.Shaped.Height + Spacing;

        foreach (var row in frame.Data)
        {
            Set(build, row, new LayoutPoint(box.X + BoxPadding + TextPadding, top), inside, LayoutAlign.Left, EventModelingPiece.Row, ink);
            top += row.Shaped.Height;
        }

        build.Close();
    }

    /// <summary>
    /// The arrows into a frame from each frame feeding it: from two thirds of the way across the frame feeding to a third of the way
    /// across the frame fed, out of whichever side faces it.
    /// </summary>
    private void Feeds(LayoutBuilder build, EventModelingBlockNode block, Framed frame, List<Framed> frames)
    {
        var ink = Written(block, "emRelationStroke") ?? Style.TextMuted;
        var head = Written(block, "emArrowhead") ?? ink;
        var written = frame.Part.Children.Where(part => part.Kind == EventModelingKinds.Feed).ToList();

        foreach (var feed in frame.Node.Feeds)
        {
            var from = frames[feed.From];
            var upwards = from.Box.Top > frame.Box.Top;

            var start = new LayoutPoint(from.Box.X + (from.Box.Width * 2 / 3), upwards ? from.Box.Top : from.Box.Bottom);
            var end = new LayoutPoint(frame.Box.X + (frame.Box.Width / 3), upwards ? frame.Box.Bottom : frame.Box.Top);

            // A feed written with ->> stands for what was written; one inferred stands for nothing.
            var part = feed.Written is { } at && at < written.Count ? written[at] : null;
            Arrow(build, start, end, ink, head, part);
        }
    }

    /// <summary>An arrow from <paramref name="start"/> to <paramref name="end"/>, its head at the end.</summary>
    private void Arrow(LayoutBuilder build, LayoutPoint start, LayoutPoint end, LayoutInk ink, LayoutInk headInk, ContentPart? part)
    {
        var (dx, dy) = (end.X - start.X, end.Y - start.Y);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1) return;

        var (ux, uy) = (dx / length, dy / length);
        var neck = new LayoutPoint(end.X - (ux * HeadLength), end.Y - (uy * HeadLength));

        var head = Medium.Outline();
        head.Begin(end);
        head.LineTo(new LayoutPoint(neck.X - (uy * HeadWidth / 2), neck.Y + (ux * HeadWidth / 2)));
        head.LineTo(new LayoutPoint(neck.X + (uy * HeadWidth / 2), neck.Y - (ux * HeadWidth / 2)));

        var line = Medium.Line(start, neck);

        build.Open(EventModelingPiece.Feed, part, stops: LayoutStops.None);
        build.Draw(new GeometryMark(line, null, Medium.Stroke(ink, 1)));
        build.Draw(new GeometryMark(head.Close(), headInk, null));
        build.Covers(new LayoutRect(start, end));
        build.Occupies(Medium.Widen(line, HeadWidth));
        build.Close();
    }

    // ── Notes and specifications ────────────────────────────────────────────

    /// <summary>
    /// The notes and specifications, each under the frame it is about, below the lanes — one under another where they would stand over
    /// each other — and how big the drawing comes to with them.
    /// </summary>
    private LayoutSize Notes(LayoutBuilder build, List<Framed> frames, IReadOnlyList<ContentPart> children, LayoutSize size)
    {
        var placed = new List<LayoutRect>();
        var floor = size.Height + (2 * LaneGap);

        foreach (var about in children.Where(part => part.Node is EventModelingAboutNode))
        {
            var frame = ((EventModelingAboutNode)about.Node).Frame;
            if (frame >= frames.Count) continue;

            var lines = about.Kind == EventModelingKinds.Note ? NoteLines(about) : SpecificationLines(about);
            if (lines.Count == 0) continue;

            var across = Math.Clamp(lines.Max(line => line.Width) + (2 * TextPadding), Narrowest, Widest);
            var down = lines.Sum(line => line.Height) + (2 * TextPadding);

            var at = new LayoutRect(frames[frame].X, floor, across, down);
            while (placed.Where(other => other.IntersectsWith(at)).ToList() is { Count: > 0 } over)
                at = new LayoutRect(at.X, over.Max(other => other.Bottom) + LaneGap, at.Width, at.Height);

            placed.Add(at);
            Card(build, about, at, lines);
        }

        return placed.Count == 0 ? size : new LayoutSize(Math.Max(size.Width, placed.Max(box => box.Right)), placed.Max(box => box.Bottom));
    }

    /// <summary>A line of a note or a specification: the runs set along it, and how big it is.</summary>
    private sealed record Line(IReadOnlyList<(Words Words, string Kind)> Runs)
    {
        public double Width => Runs.Sum(run => EventModelingBuilder.Width(run.Words)) + ((Runs.Count - 1) * 6);
        public double Height => Runs.Max(run => run.Words.Shaped.Height);
    }

    /// <summary>What a note says, a line to each of its rows.</summary>
    private List<Line> NoteLines(ContentPart note) =>
        Child(note, EventModelingKinds.Value) is { } value ? [.. Rows(value, Style.Text).Select(row => new Line([(row, EventModelingPiece.Row)]))] : [];

    /// <summary>A specification: each clause's word, then a line for each entity it names, its type before it.</summary>
    private List<Line> SpecificationLines(ContentPart specification)
    {
        var lines = new List<Line>();

        foreach (var clause in specification.Children.Where(part => part.Kind == EventModelingKinds.Clause))
        {
            var word = clause.Children.FirstOrDefault()?.Text switch
            {
                EventModelingParser.GivenWord => "Given",
                EventModelingParser.WhenWord => "When",
                _ => "Then",
            };

            var said = Medium.Shape(Style.Says($"Text.EventModeling.{word}"), Bold, Style.TextSize, Style.Text);
            lines.Add(new Line([(new Words(said, clause.Children.FirstOrDefault(), Hole: false, Maps: false), EventModelingPiece.Clause)]));

            foreach (var mention in clause.Children.Where(part => part.Kind == EventModelingKinds.Mention))
            {
                var runs = new List<(Words, string)>();

                foreach (var piece in mention.Children.Where(part => part.Role is EventModelingRoles.Type or EventModelingRoles.Entity))
                {
                    var shaped = Medium.Shape(piece.Print(), piece.Role == EventModelingRoles.Type ? Mono : Text, Style.TextSize * DataScale,
                                              piece.Role == EventModelingRoles.Type ? Style.TextMuted : Style.Text);
                    runs.Add((new Words(shaped, piece, Hole: false, Maps: piece.Role != EventModelingRoles.Type), EventModelingPiece.Row));
                }

                if (runs.Count > 0) lines.Add(new Line(runs));
            }
        }

        return lines;
    }

    /// <summary>A note or a specification: a card standing for what was written, its lines set down it.</summary>
    private void Card(LayoutBuilder build, ContentPart about, LayoutRect at, List<Line> lines)
    {
        var note = about.Kind == EventModelingKinds.Note;
        var shape = Medium.Rectangle(at, Corner, Corner);

        build.Open(note ? EventModelingPiece.Note : EventModelingPiece.Specification, about, stops: LayoutStops.None);
        Box(build, at, new GeometryMark(shape, note ? Style.QuoteBg : Style.FigureBg, Medium.Stroke(note ? Style.CodeBorder : Style.FigureBorder, 1)));

        var top = at.Y + TextPadding;
        foreach (var line in lines)
        {
            var left = at.X + TextPadding;

            foreach (var (words, kind) in line.Runs)
            {
                Set(build, words, new LayoutPoint(left, top), Math.Max(1, Width(words)), LayoutAlign.Left, kind, null);
                left += Width(words) + 6;
            }

            top += line.Height;
        }

        build.Close();
    }

    /// <summary>
    /// The box a frame, a note or a specification is drawn as — a piece of its own under the words set on it, standing for nothing itself,
    /// so a press anywhere on it clear of them means the whole of what encloses it, and a press on the words means them.
    /// </summary>
    private void Box(LayoutBuilder build, LayoutRect at, GeometryMark mark)
    {
        build.Open(EventModelingPiece.Box, part: null, stops: LayoutStops.None);
        build.Draw(mark);
        build.Covers(at);
        build.Occupies(mark.Shape);
        build.Close();
    }

    // ── The sheet ───────────────────────────────────────────────────────────

    /// <summary>
    /// The drawing, under its title, drawn smaller where it is wider than the room, round it the room its padding asks for, and under
    /// it every statement that could not be drawn, as written, with why.
    /// </summary>
    private Laid Sheet(EventModelingBlockNode block, LayoutTree drawn, LayoutSize size, IReadOnlyList<ContentPart> children, List<LayoutDiagnostic> trouble)
    {
        var padding = block.Padding;
        var room = double.IsInfinity(Room) ? double.PositiveInfinity : Math.Max(1, Room - (2 * padding));

        var build = new LayoutBuilder(Medium);
        build.Open(EventModelingPiece.Diagram, part: null, stops: LayoutStops.None);

        var top = padding;

        if (TitleOf(children) is { } title)
        {
            var shaped = Medium.Shape(title.Print(), Bold, Style.TextSize * TitleScale, Style.Heading);
            LayoutText.Words(build, shaped, new LayoutPoint(padding, top), Within(room, shaped), LayoutAlign.Left, title, EventModelingPiece.Title);
            top += shaped.Height + Spacing;
        }

        var scale = size.Width > room ? room / size.Width : 1;

        if (scale < 1)
        {
            build.Open(EventModelingPiece.Reduced, part: null, new LayoutPoint(padding, top), LayoutStops.None, paints: new LayoutPaint(Scale: scale));
            build.Graft(drawn);
            build.Close();
        }
        else
        {
            build.Graft(drawn, new LayoutPoint(padding, top));
        }

        var wide = (size.Width * scale) + (2 * padding);
        top += size.Height * scale;

        foreach (var unread in children.Where(Undrawn))
        {
            top += Spacing;

            var shaped = Medium.Shape(unread.Print(), Mono, Style.TextSize * DataScale, Style.Text);
            LayoutText.Words(build, shaped, new LayoutPoint(padding, top), Within(room, shaped), LayoutAlign.Left, unread, EventModelingPiece.Unread);
            top += shaped.Height;

            var reason = Medium.Shape(unread.Trouble!, Text, Style.TextSize * DataScale, Style.Danger);
            LayoutText.Words(build, reason, new LayoutPoint(padding, top), Within(room, reason), LayoutAlign.Left, part: null,
                             EventModelingPiece.Reason, maps: false, ink: Style.Danger);
            top += reason.Height;

            wide = Math.Max(wide, Math.Max(shaped.Width, reason.Width) + (2 * padding));
        }

        build.Covers(new LayoutRect(0, 0, wide, top + padding));
        build.Close();

        return new Laid(build.Seal(), wide, top + padding, trouble);
    }

    /// <summary>The room words are set in: all of <paramref name="room"/>, or as wide as they are where nothing bounds them.</summary>
    private static double Within(double room, IShapedText words) => double.IsInfinity(room) ? Math.Max(1, words.Width) : room;

    /// <summary>Whether a statement is one nothing in the drawing stands for, and so is set as written under it: what will not read, and a statement whose shape is wrong.</summary>
    private static bool Undrawn(ContentPart part) =>
        part.Trouble is not null && part.Node is not (EventModelingFrameNode or EventModelingAboutNode or EventModelingDataNode)
        && part.Kind is not (EventModelingKinds.FrontMatter or Kinds.Space or Kinds.Comment);

    /// <summary>The words the diagram is titled with: the last <c>title</c> statement's, or else the front matter's — or null for none.</summary>
    private static ContentPart? TitleOf(IReadOnlyList<ContentPart> children)
    {
        var said = children.LastOrDefault(part => part.Kind == EventModelingKinds.Title && part.Part(EventModelingRoles.Text) is not null);
        if (said?.Part(EventModelingRoles.Text) is { } words) return words;

        return children.FirstOrDefault(part => part.Kind == EventModelingKinds.FrontMatter)?.Children
                       .FirstOrDefault(setting => setting.Kind == EventModelingKinds.Setting && setting.Part(Roles.Name)?.Text == "title")
                       ?.Part(EventModelingRoles.Setting);
    }

    // ── Pieces ──────────────────────────────────────────────────────────────

    /// <summary>Words set at <paramref name="at"/> in <paramref name="room"/> — a hole where they are one.</summary>
    private void Set(LayoutBuilder build, Words words, LayoutPoint at, double room, LayoutAlign align, string kind, LayoutInk? ink)
    {
        if (words.Hole && words.Part is { } hole)
        {
            var offset = align == LayoutAlign.Centre ? Math.Max(0, (room - Width(words)) / 2) : 0;
            LayoutText.Hole(build, hole, new LayoutPoint(at.X + offset, at.Y), words.Shaped, Style.TextMuted);
            return;
        }

        LayoutText.Words(build, words.Shaped, at, Math.Max(1, room), align, words.Part, kind,
                         maps: words.Maps, ink: ink);
    }

    /// <summary>The fill, the outline and the ink of words a frame of <paramref name="type"/> is drawn in — what the theme writes, or the style's own.</summary>
    private (LayoutInk Fill, LayoutInk Stroke, LayoutInk Words) Paint(EventModelingBlockNode block, EventModelingType type)
    {
        var (name, colour) = type switch
        {
            EventModelingType.Ui => ("Ui", (LayoutInk?)null),
            EventModelingType.Processor => ("Processor", Style.Important),
            EventModelingType.Command => ("Command", Style.Accent),
            EventModelingType.ReadModel => ("ReadModel", Style.Success),
            _ => ("Event", Style.Warning),
        };

        var stroke = Written(block, $"em{name}Stroke") ?? colour ?? Style.FigureBorder;

        if (block.Theme.TryGetValue($"em{name}Fill", out var fill))
            return (Ink(fill), stroke, Legible(fill));

        return (colour is { } tint ? Medium.Faded(tint, Wash) : Style.FigureBg, stroke, Style.Text);
    }

    /// <summary>The colour the block's theme writes for <paramref name="variable"/>, or null where it writes none.</summary>
    private LayoutInk? Written(EventModelingBlockNode block, string variable) => block.Theme.TryGetValue(variable, out var colour) ? Ink(colour) : null;

    private LayoutInk Ink(HexColor colour) => Medium.Ink(new LayoutColor(colour.A, colour.R, colour.G, colour.B));

    /// <summary>Words that read on a fill the block writes: dark on a light fill, light on a dark one.</summary>
    private LayoutInk Legible(HexColor fill) =>
        (0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B) > 150 ? Medium.Ink(LayoutColor.Of(0, 0, 0)) : Medium.Ink(LayoutColor.Of(255, 255, 255));

    private static ContentPart? Child(ContentPart parent, string kind) => parent.Children.FirstOrDefault(part => part.Kind == kind);
}
