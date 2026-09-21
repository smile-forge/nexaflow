using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>
/// Draws a <c>sequenceDiagram</c>. The participants stand in a row along the top, each with a lifeline running down the page,
/// and everything written under them is a row on that timeline in the order it was written.
///
/// <para>
/// <strong>The drawing nests where the source does not.</strong> The lines of a block are a list; the picture is a frame
/// holding the messages written inside it, holding the frames written inside those — so a press on the room round a message
/// means the <c>alt</c> it is under, and one on the message means the message.
/// </para>
///
/// <para>
/// <strong>A participant is a box with its name in it</strong>, or one of UML's figures with its name under it where what it
/// is says so; and a bar down its lifeline says it is working, set aside as far as the bars round it are nested.
/// </para>
/// </summary>
internal class SequenceBuilder : MermaidBuilder<SequenceDiagram>
{
    /// <summary>The air kept round what is written inside a participant's box.</summary>
    private const double Air = 8;

    /// <summary>The air between what a message says and the line it is written over.</summary>
    private const double Clear = 6;

    /// <summary>How far a message to a participant itself runs out from its own lifeline.</summary>
    private const double Loop = 40;

    /// <summary>How big the cross where a lifeline ends is drawn.</summary>
    private const double Ending = 7;

    /// <summary>How thick a box, a frame and a message are drawn.</summary>
    private const double Thick = 1.5;

    /// <summary>How big the number against a message is drawn.</summary>
    private const double Counting = 10;

    /// <summary>How much of a box's or a wash's colour is laid over what is behind it.</summary>
    private const double Wash = 0.14;

    protected SequenceBuilder(ContentReading reading, DiagramLaying laying) : base(reading, laying) { }

    /// <summary>Lays a sequence diagram's source out. Never null, and never throws.</summary>
    public static Laid Build(ContentReading reading, DiagramLaying laying) => new SequenceBuilder(reading, laying).Lay();

    /// <inheritdoc/>
    protected override SequenceDiagram Of(MermaidBlock block) => SequenceDiagram.Of(block);

    protected override Size Draw(SequenceDiagram diagram, LayoutBuilder build)
    {
        var plan = Laid(diagram);

        // A diagram with nobody in it is the source: what the reader wants back is their own lines.
        if (plan.Columns.Count == 0) return AsWritten(build);

        Grouped(build, diagram, plan);
        Lifelines(build, diagram, plan);
        Timeline(build, diagram, plan);
        plan.Key?.Draw(build, plan.KeyAt);

        return plan.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    private Plan Laid(SequenceDiagram diagram)
    {
        var config = diagram.Config;
        var plan = new Plan();

        Measured(diagram, config, plan);
        if (plan.Columns.Count == 0) return plan;

        Spread(diagram, config, plan);
        Rows(diagram, config, plan);
        Sized(diagram, config, plan);

        return plan;
    }

    /// <summary>Every participant's own box, measured for what is written in it.</summary>
    private void Measured(SequenceDiagram diagram, SequenceConfig config, Plan plan)
    {
        foreach (var one in diagram.Drawn)
        {
            var words = Naming(one, config);
            var taken = DiagramWords.Taken(words);
            var carded = one.Card?.Shape;
            var figured = carded is null && SequenceGlyphs.Figured(one.Kind);

            var column = new Column
            {
                One = one,
                Words = words,
                Links = [.. one.Links.Select(link => (link, Naming(link, config)))],
                Carded = carded,
                Width = Math.Max(taken.Width + (Air * 2) + (carded is { } wide ? DiagramCard.Wider(SequenceGlyphs.Carded(wide)) : 0),
                                 figured ? SequenceGlyphs.Size + Air : Air * 4),
                Deep = figured
                    ? SequenceGlyphs.Size + taken.Height + Air
                    : taken.Height + (Air * 2) + (carded is { } deep ? DiagramCard.Deeper(SequenceGlyphs.Carded(deep)) : 0),
                Figured = figured,
            };

            plan.Columns.Add(column);
            plan.Named[one.Id] = column;
        }

        if (plan.Columns.Count == 0) return;

        plan.HeadDeep = Math.Max(config.Tallest, plan.Columns.Max(column => column.Deep));
    // A box with nothing written on it needs no band above the lifelines, since there is nothing to write there — and a box
    // inside another needs a band of its own, so the two names do not land on one another.
    plan.Banding = config.NameText + config.Framed;
    plan.Heading = diagram.Boxes.Any(box => box.Said is { Length: > 0 })
        ? (diagram.Boxes.Max(box => Deep(diagram, box)) + 1) * plan.Banding
        : 0;
        plan.HeadTop = plan.Heading;
        plan.HeadBottom = plan.HeadTop + plan.HeadDeep;
    }

    /// <summary>
    /// What a participant's card is painted with, where the diagram grades its cards by what each one is — a C4 sequence does,
    /// by the abstraction each element sits at. Null where the diagram grades nothing, and the theme's own surface is the answer.
    /// </summary>
    protected virtual (Brush Fill, Brush Stroke, Brush Ink, Brush Muted)? Toned(SequenceCard card) => null;

    /// <summary>
    /// What is written in a participant's box: its name, and — where the diagram writes a card rather than a plain box — the
    /// line saying what kind of thing it is and the sentence saying what it does, each smaller than the name above it.
    /// </summary>
    private IReadOnlyList<DiagramWords> Naming(SequenceParticipant one, SequenceConfig config)
    {
        var room = Math.Max(20, config.Widest - (Air * 2));
        var painted = one.Card is { } graded ? Toned(graded) : null;
        var ink = painted?.Ink ?? Ink.Written(one.Card?.Ink) ?? Palette.Text;
        var name = Wrapped(one.Said, one.SaidHole, config.NameText, ink, room,
                           one.Card is null ? null : FontWeights.SemiBold);

        if (one.Card is not { } card) return name;

        var rows = new List<DiagramWords>(name);

        // The stereotype is worked out from several things written, so it is pressed rather than typed into — and a press on it
        // means what it was worked out from.
        if (card.Stereotype is { Length: > 0 } says)
            rows.Add(Worked(says, card.Technology ?? one.Part, Math.Max(8, config.NameText - 3),
                            painted?.Muted ?? Palette.TextMuted));

        if (card.Said is { Length: > 0 })
            rows.AddRange(Wrapped(card.Said, null, Math.Max(8, config.NameText - 2), ink, room));

        return rows;
    }

    /// <summary>
    /// How far apart the lifelines stand: the boxes either side and the air between them at least, and wider where what is
    /// drawn between two of them would not otherwise fit.
    /// </summary>
    private void Spread(SequenceDiagram diagram, SequenceConfig config, Plan plan)
    {
        var count = plan.Columns.Count;
        var gaps = new double[Math.Max(0, count - 1)];
        var left = new double[count];
        var right = new double[count];

        for (var at = 0; at < gaps.Length; at++) gaps[at] = config.Between;

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case SequenceMessage message when At(plan, message.From) is { } from && At(plan, message.To) is { } to:
                    var wide = DiagramWords.Taken(Saying(message, config)).Width;

                    if (from == to) right[from] = Math.Max(right[from], Loop + wide + Air);
                    else Widen(gaps, plan, Math.Min(from, to), Math.Max(from, to), wide + (Air * 2));

                    break;

                case SequenceNote note:
                    Spaced(config, plan, note, gaps, left, right);
                    break;
            }
        }

        // What hangs off one lifeline has to fit between it and the next, or past the edge of the diagram.
        for (var at = 0; at < count; at++)
        {
            if (right[at] > 0 && at < count - 1) gaps[at] = Math.Max(gaps[at], right[at] + config.Framed);
            if (left[at] > 0 && at > 0) gaps[at - 1] = Math.Max(gaps[at - 1], left[at] + config.Framed);
        }

        plan.Left = config.Across + Math.Max(0, left[0]);
        plan.Right = config.Across + Math.Max(0, right[count - 1]);

        var x = plan.Left + (plan.Columns[0].Width / 2);
        plan.Columns[0].Centre = x;

        for (var at = 1; at < count; at++)
        {
            x += (plan.Columns[at - 1].Width / 2) + gaps[at - 1] + (plan.Columns[at].Width / 2);
            plan.Columns[at].Centre = x;
        }
    }

    /// <summary>The room a note asks for: between two lifelines where it spans them, and past one where it sits beside it.</summary>
    private void Spaced(SequenceConfig config, Plan plan, SequenceNote note, double[] gaps, double[] left, double[] right)
    {
        var over = note.Over.Select(id => At(plan, id)).OfType<int>().ToList();
        if (over.Count == 0) return;

        var room = DiagramWords.Taken(Noting(note, config)).Width + (config.Noted * 2);
        var (first, last) = (over.Min(), over.Max());

        switch (note.Place)
        {
            case SequencePlace.RightOf:
                right[last] = Math.Max(right[last], room + config.Noted);
                break;

            case SequencePlace.LeftOf:
                left[first] = Math.Max(left[first], room + config.Noted);
                break;

            default:
                if (first == last)
                {
                    var over_ = Math.Max(0, (room - plan.Columns[first].Width) / 2);
                    left[first] = Math.Max(left[first], over_);
                    right[last] = Math.Max(right[last], over_);
                }
                else
                {
                    Widen(gaps, plan, first, last,
                          room - (plan.Columns[first].Width / 2) - (plan.Columns[last].Width / 2));
                }

                break;
        }
    }

    /// <summary>Widens the gaps between two lifelines until they stand far enough apart for what is drawn between them.</summary>
    private static void Widen(double[] gaps, Plan plan, int from, int to, double needed)
    {
        if (from >= to || needed <= 0) return;

        var span = 0d;
        for (var at = from; at < to; at++)
            span += (plan.Columns[at].Width / 2) + gaps[at] + (plan.Columns[at + 1].Width / 2);

        if (span >= needed) return;

        var share = (needed - span) / (to - from);
        for (var at = from; at < to; at++) gaps[at] += share;
    }

    /// <summary>
    /// How deep each row of the timeline is, worked out in the order the lines were written — which is also where each bar
    /// starts and ends, where a participant made partway down first stands, and where one that is destroyed stops.
    /// </summary>
    private void Rows(SequenceDiagram diagram, SequenceConfig config, Plan plan)
    {
        var y = plan.HeadBottom + config.Downward;
        var open = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var going = new HashSet<string>(StringComparer.Ordinal);
        var frames = new Stack<string>();

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case SequenceMessage message:
                    y = Placed(config, plan, message, y, open, going, frames);
                    break;

                case SequenceNote note:
                    y = Placed(config, plan, note, y, frames);
                    break;

                case SequenceTurn turn:
                    plan.Rows[turn] = new Row { Top = y, Bottom = y };

                    if (turn.On) Opened(open, turn.Id, y);
                    else Shut(plan, open, turn.Id, y);

                    break;

                case SequenceGone gone:
                    plan.Rows[gone] = new Row { Top = y, Bottom = y };
                    going.Add(gone.Id);

                    if (plan.Named.TryGetValue(gone.Id, out var ending)) ending.Ends = y;
                    break;

                case SequenceOpening opening:
                    var deep = opening.Kind == SequenceFrame.Rect
                        ? config.Framed
                        : Tabbed(opening, config).Height + config.Framed;

                    plan.Rows[opening] = new Row { Top = y, Bottom = y + deep };
                    plan.Spans[opening.Key] = new Span();
                    frames.Push(opening.Key);
                    y += deep;

                    break;

                case SequenceDivider divider:
                    var said = DiagramWords.Taken(Dividing(divider, config));
                    var band = said.Height + config.Framed;

                    plan.Rows[divider] = new Row { Top = y, Bottom = y + band };
                    y += band;

                    break;

                case SequenceClosing closing:
                    plan.Rows[closing] = new Row { Top = y, Bottom = y + config.Framed };
                    y += config.Framed;

                    if (frames.Count > 0) frames.Pop();
                    break;
            }
        }

        plan.FootTop = y + config.Downward;

        // A bar nothing ends runs to the foot of its lifeline.
        foreach (var bars in open)
            foreach (var from in bars.Value)
                plan.Bars.Add(new Working(bars.Key, from, plan.FootTop, 0));
    }

    /// <summary>One message's row: where its line runs, and what its <c>+</c> and <c>-</c> do to the bars at either end.</summary>
    private double Placed(SequenceConfig config, Plan plan, SequenceMessage message, double y,
                          Dictionary<string, List<double>> open, HashSet<string> going, Stack<string> frames)
    {
        var said = Saying(message, config);
        var taken = DiagramWords.Taken(said);

        var arrow = message.Self ? y + Clear : y + taken.Height + Clear;
        var drop = message.Self ? Math.Max(Loop * 0.6, taken.Height + (Clear * 2)) : 0;

        if (message.Stops) Shut(plan, open, message.From, arrow);
        if (message.Starts) Opened(open, message.To, arrow);

        var row = new Row
        {
            Top = y,
            Arrow = arrow,
            Bottom = arrow + drop + (config.Apartness / 2) + (config.Bottom / 2),
            Said = said,
            From = Edge(plan, config, open, message.From, message.To, message.FromCentre),
            To = Edge(plan, config, open, message.To, message.From, message.ToCentre),
        };

        plan.Rows[message] = row;

        // A participant made partway down first stands where the message that makes it runs, and one destroyed stops just
        // past the message that ends it.
        Standing(plan, message.From, row, plan.HeadDeep);
        Standing(plan, message.To, row, plan.HeadDeep);
        Ended(plan, going, message.From, arrow);
        Ended(plan, going, message.To, arrow);

        // A message to a participant itself reaches the other side of its own lifeline as well, so a frame round one is not
        // drawn through it.
        Reaches(plan, frames, Math.Min(row.From, row.To) - (message.Self ? Counting * 2 : 0),
                message.Self ? row.From + Loop + taken.Width : Math.Max(row.From, row.To));

        return row.Bottom;
    }

    /// <summary>One note's row, and how far across the diagram it reaches.</summary>
    private double Placed(SequenceConfig config, Plan plan, SequenceNote note, double y, Stack<string> frames)
    {
        var words = Noting(note, config);
        var taken = DiagramWords.Taken(words);
        var over = note.Over.Select(id => At(plan, id)).OfType<int>().ToList();

        if (over.Count == 0)
        {
            plan.Rows[note] = new Row { Top = y, Bottom = y };
            return y;
        }

        var first = plan.Columns[over.Min()];
        var last = plan.Columns[over.Max()];
        var room = taken.Width + (config.Noted * 2);

        var (left, right) = note.Place switch
        {
            SequencePlace.RightOf => (last.Centre + config.Noted, last.Centre + config.Noted + room),
            SequencePlace.LeftOf => (first.Centre - config.Noted - room, first.Centre - config.Noted),
            _ when ReferenceEquals(first, last) => (first.Centre - (room / 2), first.Centre + (room / 2)),
            _ => Spanning(first, last, room),
        };

        var row = new Row
        {
            Top = y + (config.Framed / 2),
            Bottom = y + config.Framed + taken.Height + (config.Noted * 2),
            Said = words,
            From = left,
            To = right,
        };

        plan.Rows[note] = row;
        Reaches(plan, frames, left, right);

        return row.Bottom;
    }

    /// <summary>How far a note spanning two lifelines runs: from box edge to box edge, and wider where its words are.</summary>
    private static (double Left, double Right) Spanning(Column first, Column last, double room)
    {
        var left = first.Centre - (first.Width / 2);
        var right = last.Centre + (last.Width / 2);
        var over = Math.Max(0, room - (right - left)) / 2;

        return (left - over, right + over);
    }

    /// <summary>The whole of what is drawn, and where each frame and each box reaches now everything inside it is placed.</summary>
    private void Sized(SequenceDiagram diagram, SequenceConfig config, Plan plan)
    {
        plan.Bottom = plan.FootTop + (config.Mirrored ? plan.HeadDeep : 0);

        var links = plan.Columns.Max(column => column.Links.Count);
        if (links > 0) plan.Bottom += config.Framed + (links * (config.NoteText + 4));

        foreach (var column in plan.Columns)
        {
            if (column.Ends <= 0) column.Ends = config.Mirrored ? plan.FootTop : plan.Bottom;
            if (column.Born <= 0) column.Born = plan.HeadTop;
        }

        foreach (var opening in diagram.Items.OfType<SequenceOpening>())
        {
            if (!plan.Rows.TryGetValue(opening, out var row) || !plan.Spans.TryGetValue(opening.Key, out var span)) continue;

            var closing = diagram.Items.OfType<SequenceClosing>()
                                 .FirstOrDefault(shut => string.Equals(shut.Key, opening.Key, StringComparison.Ordinal));

            var bottom = closing is not null && plan.Rows.TryGetValue(closing, out var shutting) ? shutting.Bottom : plan.FootTop;

            var (left, right) = span.Written
                ? (span.Left - config.Framed, span.Right + config.Framed)
                : (plan.Columns[0].Centre - config.Framed, plan.Columns[^1].Centre + config.Framed);

            plan.Frames[opening.Key] = new Rect(left, row.Top, Math.Max(0, right - left), Math.Max(0, bottom - row.Top));
        }

        var deepest = diagram.Boxes.Count == 0 ? 0 : diagram.Boxes.Max(box => Deep(diagram, box));

        foreach (var box in diagram.Boxes)
        {
            var held = Holding(diagram, plan, box).ToList();
            if (held.Count == 0) continue;

            // The air a box keeps outside what it holds falls away as the boxes nest, so a box inside another is drawn
            // inside it: the outer one holds everything the inner does and keeps a band more air round it.
            var depth = Deep(diagram, box);
            var air = config.Framed * (deepest - depth + 1);
            var left = held.Min(column => column.Centre - (column.Width / 2)) - air;
            var right = held.Max(column => column.Centre + (column.Width / 2)) + air;

            plan.Boxes[box.Key] = new Rect(left, depth * plan.Banding, Math.Max(0, right - left),
                                           Math.Max(0, plan.Bottom - (depth * plan.Banding)));
        }

        var edges = plan.Frames.Values.Concat(plan.Boxes.Values).ToList();
        var last = plan.Columns[^1];

        plan.Size = new Size(Math.Max(last.Centre + (last.Width / 2) + plan.Right,
                                      edges.Count > 0 ? edges.Max(rect => rect.Right) + config.Across : 0),
                             plan.Bottom + config.Downward);

        Keyed(diagram, config, plan);
    }

    /// <summary>Every lifeline a box is drawn round: the ones written in it, and the ones written in the boxes inside it.</summary>
    private static IEnumerable<Column> Holding(SequenceDiagram diagram, Plan plan, SequenceBox box) =>
        plan.Columns
            .Where(column => column.One.Box is { } key
                             && (string.Equals(key, box.Key, StringComparison.Ordinal) || Within(diagram, key, box.Key)));

    /// <summary>Whether a box is inside another, however deep.</summary>
    private static bool Within(SequenceDiagram diagram, string key, string over)
    {
        for (var held = diagram.Boxes.FirstOrDefault(box => string.Equals(box.Key, key, StringComparison.Ordinal));
             held?.Parent is { } parent;
             held = diagram.Boxes.FirstOrDefault(box => string.Equals(box.Key, parent, StringComparison.Ordinal)))
        {
            if (string.Equals(parent, over, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>How many boxes a box is written inside.</summary>
    private static int Deep(SequenceDiagram diagram, SequenceBox box)
    {
        var depth = 0;

        for (var held = box; held?.Parent is { } parent; depth++)
            held = diagram.Boxes.FirstOrDefault(other => string.Equals(other.Key, parent, StringComparison.Ordinal));

        return depth;
    }

    /// <summary>The key under the diagram, where it asks for one — a row per thing it draws in a colour of its own.</summary>
    private void Keyed(SequenceDiagram diagram, SequenceConfig config, Plan plan)
    {
        if (diagram.Legend.Count == 0) return;

        var rows = diagram.Legend
            // A row with no colour of its own keeps an outlined square, which is what says it is a row of the key at all.
            .Select(row => new DiagramKey(null, Ink.Written(row.Fill) ?? Ink.Written(row.Border),
                                          [Worked(row.Says, null, config.NoteText, Palette.Text)]))
            .ToList();

        plan.Key = new DiagramLegend(rows, [MermaidPiece.Words], across: false, Palette.CodeBorder);
        plan.KeyAt = new Point(config.Across, plan.Size.Height);
        plan.Size = new Size(Math.Max(plan.Size.Width, plan.Key.Size.Width + (config.Across * 2)),
                             plan.Size.Height + plan.Key.Size.Height + config.Downward);
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>The boxes grouping the participants, drawn behind everything.</summary>
    private void Grouped(LayoutBuilder build, SequenceDiagram diagram, Plan plan)
    {
        if (plan.Boxes.Count == 0) return;

        build.Open(SequencePiece.Boxes, part: null, stops: Stops.None);

        foreach (var box in diagram.Boxes)
        {
            if (!plan.Boxes.TryGetValue(box.Key, out var bounds)) continue;

            // A box with no colour written on it is see-through, as Mermaid's is: its outline is what groups the lifelines.
            var fill = Ink.Written(box.Colour) is { } tint ? DiagramInk.Faded(tint, Wash) : null;
            var words = Wrapped(box.Said, null, diagram.Config.NameText, Palette.Text, Math.Max(20, bounds.Width));
            var heading = new Rect(bounds.X, bounds.Y, bounds.Width, plan.Banding);

            build.Open(SequencePiece.Box, box.Whole, stops: Stops.None);
            DiagramShapes.Draw(build, MermaidPiece.Shape, box.Part, DiagramShape.Rounded, bounds, fill,
                               new DiagramStroke(Palette.CodeBorder, Thick),
                               DiagramWords.Placed(words, heading, MermaidPiece.Words));
            build.Close();
        }

        build.Close();
    }

    /// <summary>The lifelines: each participant's own box, the line running down from it, and the bars along it.</summary>
    private void Lifelines(LayoutBuilder build, SequenceDiagram diagram, Plan plan)
    {
        var config = diagram.Config;

        build.Open(SequencePiece.Lifelines, part: null, stops: Stops.None);

        foreach (var column in plan.Columns)
        {
            // A lifeline runs from the foot of its own head rather than from the foot of the band they all stand in: the
            // heads are set in the middle of that band, so a shallower one would otherwise hang above its own line.
            var top = Head(column, column.Born, plan.HeadDeep).Bottom;
            var ends = Math.Max(top, column.Ends);

            build.Open(SequencePiece.Lifeline, column.One.Part, stops: Stops.None);

            build.Open(MermaidPiece.Shape, column.One.Part, stops: Stops.None);
            build.Draw(new GeometryMark(new LineGeometry(new Point(column.Centre, top), new Point(column.Centre, ends)),
                                        null, Palette.TextMuted, 1) { Dashes = DiagramStroke.Dotted });

            if (column.One.Destroyed) Crossed(build, column.Centre, ends);
            build.Close();

            Standing(build, column, column.Born, plan.HeadDeep);
            if (config.Mirrored && !column.One.Destroyed) Standing(build, column, plan.FootTop, plan.HeadDeep);

            foreach (var bar in plan.Bars)
                if (string.Equals(bar.Id, column.One.Id, StringComparison.Ordinal)) Barred(build, config, column, bar);

            Leading(build, config, plan, column);
            build.Close();
        }

        build.Close();
    }

    /// <summary>
    /// Where a participant's head stands in the band they all stand in. The heads are set in the middle of the band the
    /// deepest of them makes, and what a card is above its words is taken off — so a person's card, a plain box and a
    /// cylinder line up on their boxes rather than all hanging from the same top edge with their heads and caps pushing
    /// them about.
    /// </summary>
    private static Rect Head(Column column, double from, double band)
    {
        var outline = column.Carded is { } drawn ? SequenceGlyphs.Carded(drawn) : (DiagramCardShape?)null;
        var over = outline is { } which ? DiagramCard.Above(which) : 0;
        var under = outline is { } held ? DiagramCard.Deeper(held) - DiagramCard.Above(held) : 0;

        var top = from + ((band - column.Deep) / 2) + ((under - over) / 2);

        return new Rect(column.Centre - (column.Width / 2), top, column.Width, column.Deep);
    }

    /// <summary>One participant's own box or figure, with its name in it or under it.</summary>
    private void Standing(LayoutBuilder build, Column column, double from, double band)
    {
        var bounds = Head(column, from, band);

        build.Open(SequencePiece.Head, column.One.Part, stops: Stops.None);

        if (column.Figured)
        {
            var glyph = new Rect(column.Centre - (SequenceGlyphs.Size / 2), bounds.Y, SequenceGlyphs.Size, SequenceGlyphs.Size);
            var room = new Rect(bounds.X, glyph.Bottom, bounds.Width, Math.Max(0, bounds.Bottom - glyph.Bottom));
            var placed = DiagramWords.Placed(column.Words, room, MermaidPiece.Words);

            build.Open(MermaidPiece.Shape, column.One.Part, stops: Stops.None);
            build.Draw(new GeometryMark(SequenceGlyphs.Figure(column.One.Kind, glyph), null, Palette.Text, Thick));
            build.Occupies(new RectangleGeometry(glyph));
            build.Close();

            foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        }
        else
        {
            var shape = column.Carded is { } carded
                ? DiagramCard.Outline(SequenceGlyphs.Carded(carded), bounds)
                : SequenceGlyphs.Shape(column.One.Kind, bounds);

            var room = column.Carded is { } inner
                ? DiagramCard.Inside(SequenceGlyphs.Carded(inner), bounds)
                : SequenceGlyphs.Inside(column.One.Kind, bounds);

            var placed = DiagramWords.Placed(column.Words, room, MermaidPiece.Words);

            var painted = column.One.Card is { } graded ? Toned(graded) : null;

            build.Open(MermaidPiece.Shape, column.One.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, painted?.Fill ?? Ink.Written(column.One.Card?.Fill) ?? Palette.CodeBg,
                                        painted?.Stroke ?? Ink.Written(column.One.Card?.Border) ?? Palette.CodeBorder, Thick));
            build.Occupies(Less(shape, placed));
            build.Close();

            foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        }

        build.Close();
    }

    /// <summary>The bar saying a participant is working, set aside as far as the bars it is nested in are deep.</summary>
    private void Barred(LayoutBuilder build, SequenceConfig config, Column column, Working bar)
    {
        var bounds = new Rect(column.Centre - (config.Bar / 2) + (bar.Depth * (config.Bar / 2)), bar.From,
                              config.Bar, Math.Max(1, bar.To - bar.From));

        build.Open(SequencePiece.Bar, column.One.Part, stops: Stops.None);
        build.Draw(new GeometryMark(new RectangleGeometry(bounds), Palette.CodeBg, Palette.CodeBorder, 1));
        build.Occupies(new RectangleGeometry(bounds));
        build.Close();
    }

    /// <summary>Where a participant leads, written under it — a menu of them set out, since a page here pops none.</summary>
    private void Leading(LayoutBuilder build, SequenceConfig config, Plan plan, Column column)
    {
        if (column.Links.Count == 0) return;

        var y = plan.FootTop + (config.Mirrored ? plan.HeadDeep : 0) + config.Framed;

        foreach (var (link, words) in column.Links)
        {
            build.Open(SequencePiece.Menu, link.Part, stops: Stops.None);
            build.Links(link.Url, link.Url);
            words.Set(build, new Point(column.Centre - (words.Width / 2), y), MermaidPiece.Words);
            build.Close();

            y += config.NoteText + 4;
        }
    }

    /// <summary>The timeline, frames holding the messages and the notes written inside them.</summary>
    private void Timeline(LayoutBuilder build, SequenceDiagram diagram, Plan plan)
    {
        var config = diagram.Config;
        var depth = 0;

        build.Open(SequencePiece.Timeline, part: null, stops: Stops.None);

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case SequenceOpening opening:
                    build.Open(SequencePiece.Frame, opening.Whole, stops: Stops.None);
                    depth++;
                    Framing(build, config, plan, opening);

                    break;

                case SequenceClosing when depth > 0:
                    depth--;
                    build.Close();

                    break;

                case SequenceDivider divider:
                    Divided(build, config, plan, divider);
                    break;

                case SequenceMessage message:
                    Lined(build, config, plan, message);
                    break;

                case SequenceNote note:
                    Beside(build, config, plan, note);
                    break;
            }
        }

        while (depth-- > 0) build.Close();
        build.Close();
    }

    /// <summary>A frame: its border, the tab saying what kind it is, and what it holds its messages under.</summary>
    private void Framing(LayoutBuilder build, SequenceConfig config, Plan plan, SequenceOpening opening)
    {
        if (!plan.Frames.TryGetValue(opening.Key, out var bounds)) return;

        if (opening.Kind == SequenceFrame.Rect)
        {
            var wash = Ink.Written(opening.Colour) is { } tint ? DiagramInk.Faded(tint, Wash) : null;

            build.Open(MermaidPiece.Shape, opening.Part, stops: Stops.None);
            build.Draw(new GeometryMark(new RectangleGeometry(bounds), wash, null, 0));
            build.Occupies(DiagramFrame.Round(bounds));
            build.Close();

            return;
        }

        var word = Naming(opening, config);
        var tab = DiagramFrame.Tabbed(word, config.Tabbed, new Size(config.TabWidth, config.TabHeight));
        var said = Wrapped(opening.Said, null, config.SaidText, Palette.TextMuted, Math.Max(20, bounds.Width - tab.Width));

        build.Open(MermaidPiece.Shape, opening.Part, stops: Stops.None);
        build.Draw(new GeometryMark(new RectangleGeometry(bounds), null, Palette.CodeBorder, Thick));
        build.Draw(new GeometryMark(DiagramFrame.Tab(bounds, tab), Palette.CodeBg, Palette.CodeBorder, Thick));
        build.Occupies(DiagramFrame.Round(bounds, tab));
        build.Close();

        word?.Set(build, DiagramFrame.Word(bounds, tab, word), MermaidPiece.Words);

        foreach (var (words, at, kind) in DiagramWords.Placed(said, DiagramFrame.Beside(bounds, tab, config.Tabbed),
                                                              MermaidPiece.Words, TextAlignment.Left))
            words.Set(build, at, kind);
    }

    /// <summary>A line across a frame where it is divided, and what the part under it holds.</summary>
    private void Divided(LayoutBuilder build, SequenceConfig config, Plan plan, SequenceDivider divider)
    {
        if (!plan.Rows.TryGetValue(divider, out var row)) return;
        if (!plan.Frames.TryGetValue(divider.Key, out var bounds)) return;

        var said = Dividing(divider, config);
        var at = row.Top + (config.Framed / 2);
        var room = new Rect(bounds.X + config.Framed, at, Math.Max(20, bounds.Width - (config.Framed * 2)),
                            Math.Max(0, row.Bottom - at));

        build.Open(SequencePiece.Divider, divider.Part, stops: Stops.None);

        build.Open(MermaidPiece.Shape, divider.Part, stops: Stops.None);
        build.Draw(new GeometryMark(new LineGeometry(new Point(bounds.Left, at), new Point(bounds.Right, at)),
                                    null, Palette.CodeBorder, 1) { Dashes = DiagramStroke.Dashed });
        build.Close();

        foreach (var (words, where, kind) in DiagramWords.Placed(said, room, MermaidPiece.Words, TextAlignment.Left))
            words.Set(build, where, kind);

        build.Close();
    }

    /// <summary>One message: its line between the lifelines, or the loop off one where it goes to itself.</summary>
    private void Lined(LayoutBuilder build, SequenceConfig config, Plan plan, SequenceMessage message)
    {
        if (!plan.Rows.TryGetValue(message, out var row)) return;

        var stroke = new DiagramStroke(Ink.Written(message.Ink) ?? Palette.TextMuted, Thick,
                                       message.Dotted ? DiagramStroke.Dashed : null);
        var taken = DiagramWords.Taken(row.Said);

        build.Open(SequencePiece.Message, message.Part, stops: Stops.None);

        if (message.Self)
        {
            var away = row.From + Loop;
            var drop = Math.Max(Loop * 0.6, taken.Height + (Clear * 2));

            DiagramConnector.Draw(build, SequencePiece.Line, message.Part,
                                  [new Point(row.From, row.Arrow), new Point(away, row.Arrow),
                                   new Point(away, row.Arrow + drop), new Point(row.From, row.Arrow + drop)],
                                  stroke, Headed(message.Near), Headed(message.Far), curved: !config.Square);

            var room = new Rect(away + Clear, row.Arrow, Math.Max(20, taken.Width), Math.Max(0, drop));

            foreach (var (words, at, kind) in DiagramWords.Placed(row.Said, room, MermaidPiece.Words, TextAlignment.Left))
                words.Set(build, at, kind);
        }
        else
        {
            DiagramConnector.Draw(build, SequencePiece.Line, message.Part,
                                  [new Point(row.From, row.Arrow), new Point(row.To, row.Arrow)],
                                  stroke, Headed(message.Near), Headed(message.Far));

            var room = new Rect(Math.Min(row.From, row.To), row.Arrow - Clear - taken.Height,
                                Math.Abs(row.To - row.From), taken.Height);

            foreach (var (words, at, kind) in DiagramWords.Placed(row.Said, room, MermaidPiece.Words, Aligned(config.Aligned)))
                words.Set(build, at, kind);
        }

        if (message.Number is { Length: > 0 } number) Counted(build, message, row, number);

        build.Close();
    }

    /// <summary>The number a message is given, drawn in a circle where its line sets out.</summary>
    private void Counted(LayoutBuilder build, SequenceMessage message, Row row, string number)
    {
        var words = Worked(number, message.Part, Counting, Palette.Text);
        // The number goes where the line sets out, and outside the loop where a message goes to the participant it left.
        var middle = new Point(row.From + (row.To > row.From ? Counting + 2 : -Counting - 2), row.Arrow);
        var at = new Point(middle.X - (words.Width / 2), middle.Y - (words.Height / 2));

        build.Open(SequencePiece.Number, message.Part, stops: Stops.None);
        build.Open(MermaidPiece.Shape, message.Part, stops: Stops.None);
        build.Draw(new GeometryMark(new EllipseGeometry(middle, Counting, Counting), Palette.CodeBg, Palette.CodeBorder, 1));
        build.Close();

        words.Set(build, at, MermaidPiece.Words);
        build.Close();
    }

    /// <summary>One note, beside a lifeline or spanning several.</summary>
    private void Beside(LayoutBuilder build, SequenceConfig config, Plan plan, SequenceNote note)
    {
        if (!plan.Rows.TryGetValue(note, out var row) || row.To <= row.From) return;

        var bounds = new Rect(row.From, row.Top, row.To - row.From,
                              Math.Max(1, row.Bottom - row.Top - (config.Framed / 2)));
        var placed = DiagramWords.Placed(row.Said, Inside(bounds, config.Noted), MermaidPiece.Words,
                                         Aligned(config.NoteAligned));

        build.Open(SequencePiece.Note, note.Part, stops: Stops.None);

        build.Open(MermaidPiece.Shape, note.Part, stops: Stops.None);
        build.Draw(new GeometryMark(new RectangleGeometry(bounds), DiagramInk.Faded(Palette.Accent, Wash),
                                    Palette.CodeBorder, Thick));
        build.Occupies(Less(new RectangleGeometry(bounds), placed));
        build.Close();

        foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        build.Close();
    }

    // ── What the pieces say ─────────────────────────────────────────────────

    /// <summary>
    /// What a message says, drawn over its line: what is written on it, and under that the smaller lines saying what it is done
    /// with and what it is for, which a C4 relationship writes and a sequence diagram's own message does not.
    /// </summary>
    private IReadOnlyList<DiagramWords> Saying(SequenceMessage message, SequenceConfig config)
    {
        var ink = Ink.Written(message.SaidInk) ?? Palette.Text;

        var said = message.Said is null && message.SaidHole is null
            ? []
            : Says(message.Said, message.SaidHole, config.SaidText, ink, config.Wrapping);

        if (message.Under.Count == 0) return said;

        return
        [
            .. said,
            .. message.Under.SelectMany(under => Says(under, null, Math.Max(8, config.SaidText - 4), Palette.TextMuted,
                                                      config.Wrapping)),
        ];
    }

    private IReadOnlyList<DiagramWords> Noting(SequenceNote note, SequenceConfig config) =>
        note.Said is null && note.SaidHole is null
            ? []
            : Says(note.Said, note.SaidHole, config.NoteText, Palette.Text, config.Wrapping);

    private IReadOnlyList<DiagramWords> Dividing(SequenceDivider divider, SequenceConfig config) =>
        divider.Said is null ? [] : Wrapped(divider.Said, null, config.SaidText, Palette.TextMuted, config.Wrapping);

    private DiagramWords? Naming(SequenceOpening opening, SequenceConfig config) =>
        opening.Word is null ? null : Written(opening.Word, null, config.NoteText, Palette.Text);

    private DiagramWords Naming(SequenceLink link, SequenceConfig config) =>
        link.Said is { Length: > 0 }
            ? Written(link.Said, null, config.NoteText, Palette.Accent)
            : Worked(link.Url, link.Part, config.NoteText, Palette.Accent);

    private Size Tabbed(SequenceOpening opening, SequenceConfig config) =>
        DiagramFrame.Tabbed(Naming(opening, config), config.Tabbed, new Size(config.TabWidth, config.TabHeight));

    private static TextAlignment Aligned(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "left" => TextAlignment.Left,
        "right" => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    private static int? At(Plan plan, string id) =>
        plan.Named.TryGetValue(id, out var column) ? plan.Columns.IndexOf(column) : null;

    private static Rect Inside(Rect bounds, double pad) =>
        bounds.Width > pad * 2 && bounds.Height > pad * 2 ? Rect.Inflate(bounds, -pad, -pad) : bounds;

    /// <summary>What a shape stands in, less where its words are — so a press on them means them.</summary>
    private static Geometry Less(Geometry outline, IReadOnlyList<(DiagramWords Words, Point At, string Kind)> placed)
    {
        if (placed.Count == 0) return outline;

        var covered = new GeometryGroup();
        foreach (var (words, at, _) in placed)
            covered.Children.Add(new RectangleGeometry(new Rect(at, new Size(words.Width, words.Height))));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();

        return stands;
    }

    private static DiagramHead Headed(SequenceHead head) => head switch
    {
        SequenceHead.Arrow => DiagramHead.Arrow,
        SequenceHead.Open => DiagramHead.Open,
        SequenceHead.Cross => DiagramHead.Cross,
        SequenceHead.HalfTop => DiagramHead.HalfTop,
        SequenceHead.HalfBottom => DiagramHead.HalfBottom,
        SequenceHead.StickTop => DiagramHead.StickTop,
        SequenceHead.StickBottom => DiagramHead.StickBottom,
        _ => DiagramHead.None,
    };

    private static void Opened(Dictionary<string, List<double>> open, string id, double y)
    {
        if (!open.TryGetValue(id, out var bars)) open[id] = bars = [];

        bars.Add(y);
    }

    private static void Shut(Plan plan, Dictionary<string, List<double>> open, string id, double y)
    {
        if (!open.TryGetValue(id, out var bars) || bars.Count == 0) return;

        plan.Bars.Add(new Working(id, bars[^1], y, bars.Count - 1));
        bars.RemoveAt(bars.Count - 1);
    }

    /// <summary>Where a message's line meets a lifeline: the lifeline itself, or the edge of the bar open there.</summary>
    private static double Edge(Plan plan, SequenceConfig config, Dictionary<string, List<double>> open, string id,
                               string other, bool centre)
    {
        if (!plan.Named.TryGetValue(id, out var column)) return 0;
        if (centre || !plan.Named.TryGetValue(other, out var facing)) return column.Centre;

        var deep = open.TryGetValue(id, out var bars) ? bars.Count : 0;
        if (deep == 0) return column.Centre;

        var away = (config.Bar / 2) * deep;

        return column.Centre + (facing.Centre >= column.Centre ? away : -away);
    }

    private static void Standing(Plan plan, string id, Row row, double deep)
    {
        if (!plan.Named.TryGetValue(id, out var column) || !column.One.Created || column.Born > 0) return;

        column.Born = Math.Max(0, row.Arrow - (deep / 2));
    }

    private static void Ended(Plan plan, HashSet<string> going, string id, double y)
    {
        if (!going.Remove(id) || !plan.Named.TryGetValue(id, out var column)) return;

        column.Ends = y + Ending;
    }

    /// <summary>Says how far across every frame this is written inside has to reach.</summary>
    private static void Reaches(Plan plan, Stack<string> frames, double left, double right)
    {
        foreach (var key in frames)
        {
            if (!plan.Spans.TryGetValue(key, out var span)) continue;

            span.Left = span.Written ? Math.Min(span.Left, left) : left;
            span.Right = span.Written ? Math.Max(span.Right, right) : right;
            span.Written = true;
        }
    }

    private void Crossed(LayoutBuilder build, double x, double y)
    {
        var cross = new GeometryGroup();
        cross.Children.Add(new LineGeometry(new Point(x - Ending, y - Ending), new Point(x + Ending, y + Ending)));
        cross.Children.Add(new LineGeometry(new Point(x + Ending, y - Ending), new Point(x - Ending, y + Ending)));

        build.Draw(new GeometryMark(cross, null, Palette.Text, Thick));
    }

    // ── What is worked out while it is laid ─────────────────────────────────

    private sealed class Plan
    {
        public List<Column> Columns { get; } = [];

        public Dictionary<string, Column> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<SequenceItem, Row> Rows { get; } = [];

        public Dictionary<string, Span> Spans { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Rect> Frames { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Rect> Boxes { get; } = new(StringComparer.Ordinal);

        public List<Working> Bars { get; } = [];

        /// <summary>The band above the participants where the boxes' names are written.</summary>
        public double Heading { get; set; }

        /// <summary>How deep one row of that band is, which is a box's name and the air round it.</summary>
        public double Banding { get; set; }

        public double HeadTop { get; set; }

        public double HeadDeep { get; set; }

        public double HeadBottom { get; set; }

        public double FootTop { get; set; }

        public double Bottom { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

    public Size Size { get; set; }

        /// <summary>The key under it, where the diagram asks for one, and where it goes.</summary>
        public DiagramLegend? Key { get; set; }

        public Point KeyAt { get; set; }
    }

    private sealed class Column
    {
        public SequenceParticipant One { get; init; } = null!;

        public IReadOnlyList<DiagramWords> Words { get; init; } = [];

        public IReadOnlyList<(SequenceLink Link, DiagramWords Words)> Links { get; init; } = [];

        public double Width { get; init; }

        public double Deep { get; init; }

    public bool Figured { get; init; }

        /// <summary>The outline its card is drawn with, or null for a participant written as a name in a box.</summary>
        public SequenceCardShape? Carded { get; init; }

        public double Centre { get; set; }

        /// <summary>Where its own box stands: the top for one standing from the start, and lower for one made partway down.</summary>
        public double Born { get; set; }

        public double Ends { get; set; }
    }

    private sealed class Row
    {
        public double Top { get; init; }

        public double Arrow { get; init; }

        public double Bottom { get; init; }

        public double From { get; init; }

        public double To { get; init; }

        public IReadOnlyList<DiagramWords> Said { get; init; } = [];
    }

    private sealed class Span
    {
        public bool Written { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }
    }

    private sealed record Working(string Id, double From, double To, int Depth);
}
