using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Timeline;

/// <summary>The pieces a timeline's layout is made of — its layers, and what is in them.</summary>
public static class TimelinePiece
{
    /// <summary>The bands over the periods each section groups, and one — standing for its <c>section</c> line.</summary>
    public const string Sections = "Sections";
    public const string Section = "Section";

    /// <summary>The line the periods sit on, with a mark where each one meets it.</summary>
    public const string Spine = "Spine";

    /// <summary>The periods, and one — standing for the line it is written on.</summary>
    public const string Periods = "Periods";
    public const string Period = "Period";

    /// <summary>What leads from a period to the events written for it.</summary>
    public const string Drop = "Drop";

    /// <summary>The dot on a period's edge where the line down to its events sets out.</summary>
    public const string Mark = "Mark";

    /// <summary>The events, and one — standing for what it says, where it says it.</summary>
    public const string Events = "Events";
    public const string Event = "Event";

    /// <summary>What is written on the timeline: a section's name, a period's, and what an event says.</summary>
    public const string Name = "Name";
    public const string Says = "Says";
}

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
internal sealed class TimelineBuilder : MermaidBuilder<TimelineChart>
{
    /// <summary>How wide a period and its events are drawn, and how far apart one period is from the next.</summary>
    private const double Column = 150;
    private const double Between = 24;

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

    internal TimelineBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override TimelineChart Of(MermaidBlock block) => TimelineChart.Of(block);

    protected override Size Draw(TimelineChart chart, LayoutBuilder build)
    {
        // A timeline with no periods written in it is the source.
        if (chart.Empty) return AsWritten(build);

        var pad = chart.Config.Padding ?? Pad;
        var said = Measured(chart, pad);
        var room = new DiagramRoom();

        if (chart.Way == TimelineWay.TopDown) Down(build, chart, said, pad, room);
        else Across(build, chart, said, pad, room);

        return room.Size;
    }

    // ── What each period shows ──────────────────────────────────────────────

    /// <summary>One event, its words wrapped, and the box drawn round them.</summary>
    private sealed record Happening(TimelineEvent Event, IReadOnlyList<DiagramWords> Lines, Size Size);

    /// <summary>One period as it is drawn: its words, its events, the section it is in, and the colour slot it takes.</summary>
    private sealed record Shown(TimelinePeriod Period, IReadOnlyList<DiagramWords> Lines, Size Words,
                                IReadOnlyList<Happening> Events, int Section, int Slot);

    /// <summary>Every period in the order it is written, measured and given the colour slot Mermaid's rule gives it.</summary>
    private IReadOnlyList<Shown> Measured(TimelineChart chart, double pad)
    {
        var width = Math.Max(20, Column - (pad * 2));
        var shown = new List<Shown>();

        foreach (var section in chart.Sections)
        {
            foreach (var period in section.Periods)
            {
                var slot = chart.Config.DisableMulticolor ? 0 : chart.Sectioned ? section.Order : shown.Count;
                var lines = Said(period.Says, PeriodSize, Words(chart, slot), width, FontWeights.SemiBold);

                var events = period.Events
                    .Select(happening =>
                    {
                        var words = Said(happening.Says, EventSize, Palette.Text, width);
                        return new Happening(happening, words, new Size(Column, DiagramWords.Taken(words).Height + (pad * 2)));
                    })
                    .ToList();

                shown.Add(new Shown(period, lines, DiagramWords.Taken(lines), events, section.Order, slot));
            }
        }

        return shown;
    }

    // ── Across the page ─────────────────────────────────────────────────────

    private void Across(LayoutBuilder build, TimelineChart chart, IReadOnlyList<Shown> said, double pad, DiagramRoom room)
    {
        var banded = chart.Sectioned ? Band + Gap : 0;
        var tall = Math.Max(Least, said.Max(shown => shown.Words.Height) + (pad * 2));
        var top = banded;

        var boxes = said.Select((shown, at) => new Rect(at * (Column + Between), top, Column, tall)).ToList();
        var spine = top + (tall / 2);

        Bands(build, chart, said, boxes, band => new Rect(band.Left, 0, band.Width, Band), pad, room);

        // The spine runs through the middle of the periods, from the first to the last.
        Spine(build, [new Point(boxes[0].Left + (Column / 2), spine), new Point(boxes[^1].Left + (Column / 2), spine)], room);

        for (var at = 0; at < said.Count; at++)
        {
            var box = boxes[at];
            var events = new List<Rect>();
            var y = box.Bottom + Reach;

            foreach (var happening in said[at].Events)
            {
                events.Add(new Rect(box.Left, y, Column, happening.Size.Height));
                y += happening.Size.Height + Gap;
            }

            Period(build, said[at], chart, box, events, new Point(box.Left + (Column / 2), box.Bottom), pad, room);
        }
    }

    // ── Down the page ───────────────────────────────────────────────────────

    private void Down(LayoutBuilder build, TimelineChart chart, IReadOnlyList<Shown> said, double pad, DiagramRoom room)
    {
        var left = chart.Sectioned ? Strip + Gap : 0;
        var boxes = new List<Rect>();
        var rows = new List<List<Rect>>();
        var y = 0.0;

        foreach (var shown in said)
        {
            var events = new List<Rect>();
            var at = y;

            foreach (var happening in shown.Events)
            {
                events.Add(new Rect(left + Column + Reach, at, Column, happening.Size.Height));
                at += happening.Size.Height + Gap;
            }

            var tall = Math.Max(Math.Max(Least, shown.Words.Height + (pad * 2)), at - y - (events.Count > 0 ? Gap : 0));
            boxes.Add(new Rect(left, y, Column, tall));
            rows.Add(events);

            y += tall + Gap;
        }

        Bands(build, chart, said, boxes, band => new Rect(0, band.Top, Strip, band.Height), pad, room);

        var middle = left + (Column / 2);
        Spine(build, [new Point(middle, boxes[0].Top), new Point(middle, boxes[^1].Bottom)], room);

        for (var at = 0; at < said.Count; at++)
            Period(build, said[at], chart, boxes[at], rows[at], new Point(boxes[at].Right, boxes[at].Top + (boxes[at].Height / 2)), pad, room);
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    /// <summary>A band over each run of periods the same section groups — <paramref name="over"/> saying where it goes.</summary>
    private void Bands(LayoutBuilder build, TimelineChart chart, IReadOnlyList<Shown> said, IReadOnlyList<Rect> boxes,
                       Func<Rect, Rect> over, double pad, DiagramRoom room)
    {
        if (!chart.Sectioned) return;

        build.Open(TimelinePiece.Sections, part: null, stops: Stops.None);

        foreach (var (section, at, _, run) in DiagramBand.Runs(said, shown => shown.Section, boxes))
        {
            if (chart.Sections.FirstOrDefault(group => group.Order == section) is not { Name: { } name } group) continue;

            var band = over(run);
            // A section's name heads what it holds, so it is written in the title's colour.
            var lines = Said(name, NameSize, TitleInk, Math.Max(20, band.Width - (pad * 2)), FontWeights.SemiBold);
            var fill = Colour(chart, said[at].Slot);

            room.Reach(band);
            DiagramShapes.Draw(build, TimelinePiece.Section, group.Part, DiagramShape.Rounded, band,
                DiagramInk.Faded(fill, Banded), new DiagramStroke(DiagramInk.Faded(fill, 0.5)),
                DiagramWords.Placed(lines, Rect.Inflate(band, -pad, -pad), TimelinePiece.Name));
        }

        build.Close();
    }

    /// <summary>The line the periods sit on, with a mark where each one meets it.</summary>
    private void Spine(LayoutBuilder build, IReadOnlyList<Point> line, DiagramRoom room)
    {
        room.Reach(line[0], line[^1]);

        build.Open(TimelinePiece.Spine, part: null, stops: Stops.None);
        DiagramConnector.Draw(build, MermaidPiece.Line, part: null, line, new DiagramStroke(Palette.CodeBorder, 2), end: DiagramHead.None);
        build.Close();
    }

    /// <summary>One period: its box on the spine, what leads from it to its events, and the events themselves.</summary>
    private void Period(LayoutBuilder build, Shown shown, TimelineChart chart, Rect box, IReadOnlyList<Rect> events, Point from,
                        double pad, DiagramRoom room)
    {
        var colour = Colour(chart, shown.Slot);

        if (events.Count > 0)
        {
            var to = chart.Way == TimelineWay.TopDown
                ? new Point(events[0].Left, from.Y)
                : new Point(from.X, events[0].Top);

            build.Open(TimelinePiece.Drop, shown.Period.Part, stops: Stops.None);
            DiagramConnector.Draw(build, MermaidPiece.Line, shown.Period.Part, [from, to],
                new DiagramStroke(DiagramInk.Faded(colour, 0.6), 1.5), end: DiagramHead.None);
            build.Close();
        }

        room.Reach(box);

        build.Open(TimelinePiece.Periods, part: null, stops: Stops.None);
        DiagramShapes.Draw(build, TimelinePiece.Period, shown.Period.Part, DiagramShape.Rounded, box, colour, new DiagramStroke(colour, 1.2),
            DiagramWords.Placed(shown.Lines, Rect.Inflate(box, -pad, -pad), TimelinePiece.Says));
        build.Close();

        // A dot of the period's own colour on its edge, where the line down to its events sets out: over the box, so it is seen.
        var dot = new EllipseGeometry(from, Mark, Mark);
        dot.Freeze();

        build.Open(TimelinePiece.Mark, shown.Period.Part, stops: Stops.None);
        build.Draw(new GeometryMark(dot, colour, Ink.Surface, 1));
        build.Occupies(dot);
        build.Close();

        if (events.Count == 0) return;

        build.Open(TimelinePiece.Events, part: null, stops: Stops.None);

        for (var at = 0; at < events.Count; at++)
        {
            var happening = shown.Events[at];
            room.Reach(events[at]);

            DiagramShapes.Draw(build, TimelinePiece.Event, happening.Event.Says.Part, DiagramShape.Rounded, events[at],
                DiagramInk.Faded(colour, Tinted), new DiagramStroke(colour),
                DiagramWords.Placed(happening.Lines, Rect.Inflate(events[at], -pad, -pad), TimelinePiece.Says, TextAlignment.Left));
        }

        build.Close();
    }

    // ── Words and ink ───────────────────────────────────────────────────────

    /// <summary>
    /// What a piece of text is set as: the characters written, wrapped to <paramref name="width"/> and broken where a
    /// <c>&lt;br&gt;</c> says to — or, where it holds an entity code, what that code says, which is worked out and so
    /// pressed rather than typed into.
    /// </summary>
    private IReadOnlyList<DiagramWords> Said(TimelineText text, double size, Brush ink, double width, FontWeight? weight = null) =>
        Wrapped(text.Says, text.Hole, size, ink, width, weight);

    /// <summary>The colour a slot is drawn in: the one its <c>cScale</c> writes, or the theme's own.</summary>
    private Brush Colour(TimelineChart chart, int slot) => Ink.Series(slot, chart.Config.ScaleAt(slot));

    /// <summary>The ink a period's words are written in: what its <c>cScaleLabel</c> writes, or what reads over its fill.</summary>
    private Brush Words(TimelineChart chart, int slot) =>
        Ink.Written(chart.Config.ScaleLabelAt(slot)) ?? Ink.Over(Colour(chart, slot));
}
