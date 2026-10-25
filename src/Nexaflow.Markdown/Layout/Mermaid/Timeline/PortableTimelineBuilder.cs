using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Timeline;

/// <summary>
/// Draws a <c>timeline</c> block: its periods on a spine — across the page, or down it where the block says <c>TD</c> —
/// each with the events written for it stacked away from the spine, and a band over the periods each section groups.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A period stands for its line, an event for what it
/// says where it says it — on its period's line or on a line going on from it — and a band for its <c>section</c> line;
/// every word is typed into where it is drawn, unless it holds an entity code (<c>#colon;</c>), which is worked out and
/// so only pressed. Colour follows Mermaid: with sections every period in one takes that section's slot, without them
/// each period takes the next, and <c>disableMulticolor</c> puts everything on the first.
/// </para>
/// </summary>
internal sealed class PortableTimelineBuilder : PortableContentBuilder
{
    /// <summary>How wide a period and its events are drawn, and how far apart one period is from the next.</summary>
    private const double Column = 150;
    private const double Between = 24;

    /// <summary>The clear air between one row of periods and the next, where a timeline runs on down the page.</summary>
    private const double Apart = 32;

    /// <summary>The least a period's box is, before what it says asks for more.</summary>
    private const double Least = 34;

    private const double Gap = 6;
    private const double Pad = 8;

    /// <summary>How far the events are from the spine, and how far a section's band is from the periods it groups.</summary>
    private const double Reach = 12;
    private const double Band = 26;

    /// <summary>How wide a section's band is drawn where the timeline runs down the page.</summary>
    private const double Strip = 110;

    private const double Mark = 4;
    private const double PeriodSize = 12;
    private const double EventSize = 11;
    private const double NameSize = 12;

    /// <summary>How solid a section's band and an event's box are tinted.</summary>
    private const double Banded = 0.16;
    private const double Tinted = 0.22;

    internal PortableTimelineBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                     PortableHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new PortableDiagramInk(style, medium);
        Wording = new PortableDiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, PortableStanding.Inline));
        Sheet = new PortableDiagramSheet(state, style, medium, isReadOnly, Wording, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private PortableDiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private PortableDiagramText Wording { get; }

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

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        var (down, sections) = Read();

        // A timeline with no periods written in it is the source.
        if (!sections.Any(section => section.Periods.Count > 0)) return Sheet.AsWritten();

        var config = PortableDiagramConfig.Of(Reading, TimelineConfig.Default);
        var sectioned = sections.Any(section => section.Part is not null);
        var pad = config.Padding ?? Pad;
        var said = Measured(config, sections, sectioned, pad);
        var room = new PortableDiagramRoom();

        if (down) Down(build, config, sectioned, said, pad, room);
        else Across(build, config, sectioned, said, pad, room);

        // Run on in rows to the room already where it reads across; a single period wider than the room, or a timeline read
        // down the page, is drawn smaller.
        return Sheet.Reduced(room.Size);
    }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>Text somebody wrote: the piece it was written as — what pressing it means — what it says, and the hole standing where it is still to write.</summary>
    private readonly record struct Phrase(ContentPart Part, ContentPart Says, ContentPart? Hole);

    /// <summary>One period: the line it was written on, what it is called, and the events written for it in the order they are written.</summary>
    private sealed record Period(ContentPart Part, Phrase Says, List<Phrase> Events);

    /// <summary>A group of periods: the <c>section</c> line naming it, or nothing for the periods written before any section.</summary>
    private sealed record Section(ContentPart? Part, Phrase? Name, List<Period> Periods, int Order);

    /// <summary>
    /// Which way the timeline runs — what the last line saying so asks, or across the page — and its sections in the order
    /// they are written, each with its periods and each period with its events: those after the colons on its own line, and
    /// on the lines going on from it. Periods written before any section are a section of their own, with nothing naming it,
    /// as Mermaid groups them.
    /// </summary>
    private (bool Down, List<Section> Sections) Read()
    {
        var down = false;
        var sections = new List<Section>();
        Period? above = null;

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case TimelineKinds.Direction when Running(part) is { } way:
                    down = way;
                    break;

                case TimelineKinds.Section:
                    sections.Add(new Section(part, Text(part, TimelineRoles.Name), [], sections.Count));
                    break;

                case TimelineKinds.Period:
                    above = Text(part, TimelineRoles.Says) is { } says ? new Period(part, says, [.. Events(part)]) : null;
                    if (above is null) break;

                    if (sections.Count == 0) sections.Add(new Section(null, null, [], 0));
                    sections[^1].Periods.Add(above);
                    break;

                case TimelineKinds.More:
                    above?.Events.AddRange(Events(part));
                    break;
            }
        }

        return (down, sections);
    }

    /// <summary>The events written on a line — what says nothing being nothing written.</summary>
    private static IEnumerable<Phrase> Events(ContentPart line)
    {
        foreach (var text in line.Children)
        {
            if (text.Kind != TimelineKinds.Text || text.Role != TimelineRoles.Event) continue;
            if (text.Words() is not { } says || (says.Length == 0 && text.Hole() is null)) continue;

            yield return new Phrase(text, says, text.Hole());
        }
    }

    /// <summary>Whether a line saying which way the timeline runs says down the page — or null where it says no way at all.</summary>
    private static bool? Running(ContentPart line)
    {
        if (line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting) is not { Trouble: null } way) return null;

        var said = way.Text.AsSpan().Trim();
        if (said.Equals("LR", StringComparison.OrdinalIgnoreCase)) return false;
        if (said.Equals("TD", StringComparison.OrdinalIgnoreCase) || said.Equals("TB", StringComparison.OrdinalIgnoreCase)) return true;

        return null;
    }

    private static Phrase? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == TimelineKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new Phrase(text, says, text.Hole())
            : null;

    // ── What each period shows ──────────────────────────────────────────────

    /// <summary>One event, its words wrapped, and the box drawn round them.</summary>
    private sealed record Happening(Phrase Event, IReadOnlyList<PortableDiagramWords> Lines, LayoutSize Size);

    /// <summary>One period as it is drawn: its words, its events, the section it is in, and the colour slot it takes.</summary>
    private sealed record Sized(Period Period, IReadOnlyList<PortableDiagramWords> Lines, LayoutSize Words,
                                IReadOnlyList<Happening> Events, Section Section, int Slot);

    /// <summary>
    /// Every period in the order it is written, measured and given the colour slot Mermaid's rule gives it: with sections,
    /// its section's; without, one of its own.
    /// </summary>
    private IReadOnlyList<Sized> Measured(TimelineConfig config, IReadOnlyList<Section> sections, bool sectioned, double pad)
    {
        var width = Math.Max(20, Column - (pad * 2));
        var shown = new List<Sized>();

        foreach (var section in sections)
        {
            foreach (var period in section.Periods)
            {
                var slot = config.DisableMulticolor ? 0 : sectioned ? section.Order : shown.Count;
                var lines = Said(period.Says, PeriodSize, Words(config, slot), width, 600);

                var events = period.Events
                    .Select(happening =>
                    {
                        var words = Said(happening, EventSize, Style.Text, width);
                        return new Happening(happening, words, new LayoutSize(Column, PortableDiagramWords.Taken(words).Height + (pad * 2)));
                    })
                    .ToList();

                shown.Add(new Sized(period, lines, PortableDiagramWords.Taken(lines), events, section, slot));
            }
        }

        return shown;
    }

    // ── Across the page ─────────────────────────────────────────────────────

    private void Across(PortableBuilder build, TimelineConfig config, bool sectioned, IReadOnlyList<Sized> said, double pad, PortableDiagramRoom room)
    {
        var banded = sectioned ? Band + Gap : 0;
        var tall = Math.Max(Least, said.Max(shown => shown.Words.Height) + (pad * 2));

        // As many periods to a row as the room holds, and the rest running on in rows under it as words run on under a line: a
        // timeline longer than its room is read on down the page before it is drawn smaller. One at the least, so a room narrower
        // than a period still has a period in every row.
        var each = double.IsInfinity(Sheet.Space) ? said.Count : Math.Clamp((int)((Sheet.Space + Between) / (Column + Between)), 1, said.Count);

        var rows = new List<(int From, int Count)>();
        var boxes = new List<LayoutRect>();
        var happenings = new List<List<LayoutRect>>();
        var top = 0.0;

        for (var from = 0; from < said.Count; from += each)
        {
            var count = Math.Min(each, said.Count - from);
            var bottom = top + banded + tall;
            rows.Add((from, count));

            for (var at = from; at < from + count; at++)
            {
                var box = new LayoutRect((at - from) * (Column + Between), top + banded, Column, tall);
                var events = new List<LayoutRect>();
                var y = box.Bottom + Reach;

                foreach (var happening in said[at].Events)
                {
                    events.Add(new LayoutRect(box.Left, y, Column, happening.Size.Height));
                    y += happening.Size.Height + Gap;
                }

                boxes.Add(box);
                happenings.Add(events);
                bottom = Math.Max(bottom, events.Count > 0 ? events[^1].Bottom : box.Bottom);
            }

            top = bottom + Apart;
        }

        if (sectioned) Bands(build, config, said, boxes, rows, band => new LayoutRect(band.Left, band.Top - banded, band.Width, Band), pad, room);

        // A spine runs through the middle of each row of periods, from the first of them to the last.
        foreach (var (from, count) in rows)
        {
            var (first, last) = (boxes[from], boxes[from + count - 1]);
            var spine = first.Top + (tall / 2);

            Spine(build, [new LayoutPoint(first.Left + (Column / 2), spine), new LayoutPoint(last.Left + (Column / 2), spine)], room);
        }

        for (var at = 0; at < said.Count; at++)
            Placed(build, said[at], config, down: false, boxes[at], happenings[at], new LayoutPoint(boxes[at].Left + (Column / 2), boxes[at].Bottom), pad, room);
    }

    // ── Down the page ───────────────────────────────────────────────────────

    private void Down(PortableBuilder build, TimelineConfig config, bool sectioned, IReadOnlyList<Sized> said, double pad, PortableDiagramRoom room)
    {
        var left = sectioned ? Strip + Gap : 0;
        var boxes = new List<LayoutRect>();
        var rows = new List<List<LayoutRect>>();
        var y = 0.0;

        foreach (var shown in said)
        {
            var events = new List<LayoutRect>();
            var at = y;

            foreach (var happening in shown.Events)
            {
                events.Add(new LayoutRect(left + Column + Reach, at, Column, happening.Size.Height));
                at += happening.Size.Height + Gap;
            }

            var tall = Math.Max(Math.Max(Least, shown.Words.Height + (pad * 2)), at - y - (events.Count > 0 ? Gap : 0));
            boxes.Add(new LayoutRect(left, y, Column, tall));
            rows.Add(events);

            y += tall + Gap;
        }

        if (sectioned) Bands(build, config, said, boxes, [(0, said.Count)], band => new LayoutRect(0, band.Top, Strip, band.Height), pad, room);

        var middle = left + (Column / 2);
        Spine(build, [new LayoutPoint(middle, boxes[0].Top), new LayoutPoint(middle, boxes[^1].Bottom)], room);

        for (var at = 0; at < said.Count; at++)
            Placed(build, said[at], config, down: true, boxes[at], rows[at], new LayoutPoint(boxes[at].Right, boxes[at].Top + (boxes[at].Height / 2)), pad, room);
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A band over each run of periods the same section groups — <paramref name="over"/> saying where it goes. A run is
    /// found in each of <paramref name="rows"/> on its own, so a section the periods run on past the end of a row is a band in
    /// each row rather than one across the rows between.
    /// </summary>
    private void Bands(PortableBuilder build, TimelineConfig config, IReadOnlyList<Sized> said, IReadOnlyList<LayoutRect> boxes,
                       IReadOnlyList<(int From, int Count)> rows, Func<LayoutRect, LayoutRect> over, double pad, PortableDiagramRoom room)
    {
        build.Open(TimelinePiece.Sections, part: null, stops: PortableStops.None);

        foreach (var (from, count) in rows)
            foreach (var (_, first, _, run) in PortableDiagramBand.Runs([.. said.Skip(from).Take(count)], shown => shown.Section.Order, [.. boxes.Skip(from).Take(count)]))
            {
                var at = from + first;
                if (said[at].Section is not { Name: { } name } group) continue;

                var band = over(run);
                // A section's name heads what it holds, so it is written in the title's colour.
                var lines = Said(name, NameSize, Style.Heading, Math.Max(20, band.Width - (pad * 2)), 600);
                var fill = Colour(config, said[at].Slot);

                room.Reach(band);
                PortableDiagramShapes.Draw(build, TimelinePiece.Section, group.Part, DiagramShape.Rounded, band,
                    PortableDiagramInk.Faded(build.Medium, fill, Banded), new PortableDiagramStroke(PortableDiagramInk.Faded(build.Medium, fill, 0.5)),
                    PortableDiagramWords.Placed(lines, LayoutRect.Inflate(band, -pad, -pad), TimelinePiece.Name));
            }

        build.Close();
    }

    /// <summary>The line the periods sit on, with a mark where each one meets it.</summary>
    private void Spine(PortableBuilder build, IReadOnlyList<LayoutPoint> line, PortableDiagramRoom room)
    {
        room.Reach(line[0], line[^1]);

        build.Open(TimelinePiece.Spine, part: null, stops: PortableStops.None);
        PortableDiagramConnector.Draw(build, MermaidPiece.Line, part: null, line, new PortableDiagramStroke(Style.CodeBorder, 2), end: DiagramHead.None);
        build.Close();
    }

    /// <summary>One period: its box on the spine, what leads from it to its events, and the events themselves.</summary>
    private void Placed(PortableBuilder build, Sized shown, TimelineConfig config, bool down, LayoutRect box, IReadOnlyList<LayoutRect> events, LayoutPoint from,
                        double pad, PortableDiagramRoom room)
    {
        var colour = Colour(config, shown.Slot);

        if (events.Count > 0)
        {
            var to = down
                ? new LayoutPoint(events[0].Left, from.Y)
                : new LayoutPoint(from.X, events[0].Top);

            build.Open(TimelinePiece.Drop, shown.Period.Part, stops: PortableStops.None);
            PortableDiagramConnector.Draw(build, MermaidPiece.Line, shown.Period.Part, [from, to],
                new PortableDiagramStroke(PortableDiagramInk.Faded(build.Medium, colour, 0.6), 1.5), end: DiagramHead.None);
            build.Close();
        }

        room.Reach(box);

        build.Open(TimelinePiece.Periods, part: null, stops: PortableStops.None);
        PortableDiagramShapes.Draw(build, TimelinePiece.Period, shown.Period.Part, DiagramShape.Rounded, box, colour, new PortableDiagramStroke(colour, 1.2),
            PortableDiagramWords.Placed(shown.Lines, LayoutRect.Inflate(box, -pad, -pad), TimelinePiece.Says));
        build.Close();

        // A dot of the period's own colour on its edge, where the line down to its events sets out: over the box, so it is seen.
        var dot = build.Medium.Ellipse(from, Mark, Mark);

        build.Open(TimelinePiece.Mark, shown.Period.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, dot, colour, Ink.Surface, 1));
        build.Occupies(dot);
        build.Close();

        if (events.Count == 0) return;

        build.Open(TimelinePiece.Events, part: null, stops: PortableStops.None);

        for (var at = 0; at < events.Count; at++)
        {
            var happening = shown.Events[at];
            room.Reach(events[at]);

            PortableDiagramShapes.Draw(build, TimelinePiece.Event, happening.Event.Part, DiagramShape.Rounded, events[at],
                PortableDiagramInk.Faded(build.Medium, colour, Tinted), new PortableDiagramStroke(colour),
                PortableDiagramWords.Placed(happening.Lines, LayoutRect.Inflate(events[at], -pad, -pad), TimelinePiece.Says, LayoutAlign.Left));
        }

        build.Close();
    }

    // ── Words and ink ───────────────────────────────────────────────────────

    /// <summary>
    /// What a piece of text is set as: the characters written, wrapped to <paramref name="width"/> and broken where a
    /// <c>&lt;br&gt;</c> says to — or, where it holds an entity code, what that code says, which is worked out and so
    /// pressed rather than typed into.
    /// </summary>
    private IReadOnlyList<PortableDiagramWords> Said(Phrase text, double size, LayoutInk ink, double width, int? weight = null) =>
        Wording.Wrapped(text.Says, text.Hole, size, ink, width, weight);

    /// <summary>The colour a slot is drawn in: the one its <c>cScale</c> writes, or the theme's own.</summary>
    private LayoutInk Colour(TimelineConfig config, int slot) => Ink.Series(slot, config.ScaleAt(slot));

    /// <summary>The ink a period's words are written in: what its <c>cScaleLabel</c> writes, or what reads over its fill.</summary>
    private LayoutInk Words(TimelineConfig config, int slot) =>
        Ink.Written(config.ScaleLabelAt(slot)) ?? Ink.Over(Colour(config, slot));
}
