using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Sequence;

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
/// <strong>A participant is a box with its name in it</strong> — with a mark of what it is before the name where it is a
/// boundary, a database or another kind of its own — or a figure with its name under it where it is an actor; and a bar down
/// its lifeline says it is working, set aside as far as the bars round it are nested.
/// </para>
/// </summary>
internal sealed partial class PortableSequenceDrawing
{
    /// <summary>
    /// The lengths a sequence diagram is drawn with at its own size. Read through the properties below rather than
    /// directly, so that a diagram too wide for the room it is given can be drawn smaller — see <see cref="Fitted"/>.
    /// </summary>
    private static class Metric
    {
        public const double Air = 8;
        public const double Clear = 6;
        public const double Loop = 40;
        public const double Ending = 7;
        public const double Thick = 1.5;
        public const double Counting = 10;
        public const double Titled = 60;
    }

    /// <summary>The air kept round what is written inside a participant's box.</summary>
    private double Air => Metric.Air * this.scale;

    /// <summary>The air between what a message says and the line it is written over.</summary>
    private double Clear => Metric.Clear * this.scale;

    /// <summary>How far a message to a participant itself runs out from its own lifeline.</summary>
    private double Loop => Metric.Loop * this.scale;

    /// <summary>How big the cross where a lifeline ends is drawn.</summary>
    private double Ending => Metric.Ending * this.scale;

    /// <summary>How thick a box, a frame and a message are drawn.</summary>
    private double Thick => Metric.Thick * this.scale;

    /// <summary>How big the number against a message is drawn.</summary>
    private double Counting => Metric.Counting * this.scale;

    /// <summary>The least room across the middle of a frame its title is set in, clear of the tab either side.</summary>
    private double Titled => Metric.Titled * this.scale;

    /// <summary>How much of a box's or a wash's colour is laid over what is behind it.</summary>
    private const double Wash = 0.14;

    /// <summary>How big a participant's standing figure is drawn, and the little icon on a plain participant's box.</summary>
    private double Figuring => PortableSequenceGlyphs.Size * this.scale;
    private double Icon => PortableSequenceGlyphs.Icon * this.scale;

    /// <summary>
    /// The fraction of its own size the diagram is drawn at — one, until it turns out not to fit the room it was
    /// given. Set by <see cref="Fitted"/>, and read by every length above.
    /// </summary>
    private double scale = 1;

    /// <summary>
    /// How big the page sets body text, against the size the lengths above are written at. One at the reader's
    /// default, and what the zoom chip moves (TextZoom.FontSize, MarkdownSurface.BaseFontSize, PortableStyle.TextSize).
    ///
    /// <para>
    /// Prose works every length it draws out from the body size — its gaps, indents, pads and rules are all multiples
    /// of it — so a diagram ignoring it is the one thing on the page that does not answer the zoom.
    /// </para>
    /// </summary>
    private double Page => Style.TextSize / PortableStyle.DefaultTextSize;

    /// <param name="ink">What the builder draws in.</param>
    /// <param name="words">What the builder makes its words with.</param>
    /// <param name="sheet">The sheet the builder draws on.</param>
    /// <param name="folded">How much of the diagram the builder draws.</param>
    /// <param name="dialect">How the diagram differs from a sequence diagram, where it is one written in another language's words.</param>
    internal PortableSequenceDrawing(ContentReading reading, EditState state, PortableStyle style, ILayoutMedium medium, PortableDiagramInk ink,
                                     PortableDiagramText words, PortableDiagramSheet sheet, PortableDiagramFolding folded,
                                   Dialect dialect)
    {
        Reading = reading;
        State = state;
        Style = style;
        Medium = medium;
        Ink = ink;
        Words = words;
        Sheet = sheet;
        Folded = folded;
        Spoken = dialect;
    }

    private ContentReading Reading { get; }

    private EditState State { get; }

    private PortableStyle Style { get; }

    private ILayoutMedium Medium { get; }

    private PortableDiagramInk Ink { get; }

    private PortableDiagramText Words { get; }

    private PortableDiagramSheet Sheet { get; }

    private PortableDiagramFolding Folded { get; }

    /// <summary>How the diagram differs from a sequence diagram — in nothing, for one.</summary>
    private Dialect Spoken { get; }


    public LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        // PortableLaid at the size the page is set at, so it grows and shrinks with the words around it.
        this.scale = Page;

        var diagram = Spoken.Read(Reading.Root, PortableDiagramConfig.Of(Reading, SequenceConfig.Default).Scaled(Page));
        var plan = Laid(diagram);

        // A diagram with nobody in it is the source: what the reader wants back is their own lines.
        if (plan.Columns.Count == 0) return Sheet.AsWritten();

        // Drawn at whatever size it takes to fit the room, which is the only way a sequence diagram can fit one.
        (diagram, plan) = Fitted(diagram, plan);

        Grouped(build, diagram, plan);
        Lifelines(build, diagram, plan);
        Timeline(build, diagram, plan);
        plan.Key?.Draw(build, plan.KeyAt);

        return plan.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    private Plan Laid(Diagram diagram)
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

    /// <summary>How many times the diagram is laid again to fit the room before it is drawn as it last came out.</summary>
    private const int Fittings = 4;

    /// <summary>
    /// The diagram at the size it fits the room in, with the plan it was laid to.
    ///
    /// <para>
    /// Every other kind of diagram too wide for its room is reflowed into it — its boxes narrow, its rows wrap. A
    /// sequence diagram cannot be: its lifelines stand exactly as far apart as the messages between them are wide (see
    /// <see cref="Spread"/>), so taking the room away would run the words of one message over the next. What will not
    /// fit is therefore drawn smaller instead.
    /// </para>
    /// <para>
    /// PortableLaid again at the smaller size rather than drawn scaled, so that every word is measured and hinted at the size it
    /// is set in and the names break where they fall at that size. PortableLaid again more than once, because the second lay is
    /// not the first one multiplied: words at a smaller size are not proportionally narrower, and a name that wrapped may
    /// now stand on one line. So each pass takes its scale from what the one before it actually came to.
    /// </para>
    /// </summary>
    private (Diagram Diagram, Plan Plan) Fitted(Diagram whole, Plan plan)
    {
        if (double.IsInfinity(Sheet.Space)) return (whole, plan);

        var diagram = whole;

        // What it has been narrowed by so far. Kept apart from the size the page is set at, which the diagram handed
        // in is already drawn at: the two multiply, and only this one is worked out again each pass.
        var fit = 1.0;

        for (var attempt = 0; attempt < Fittings && plan.Size.Width > Sheet.Space; attempt++)
        {
            var was = (Fit: fit, Scale: this.scale);

            fit *= Sheet.Space / plan.Size.Width;
            this.scale = Page * fit;

            var smaller = whole.Sized(whole.Config.Scaled(fit));
            var laid = Laid(smaller);

            // Kept only if it came out narrower: a diagram held to a least width by what is written in it stays that
            // wide however small the scale, and another pass would come to the same thing. The scale goes back to the
            // one the plan being kept was laid at, since every length the drawing reads is taken from it.
            if (laid.Columns.Count == 0 || laid.Size.Width >= plan.Size.Width)
            {
                (fit, this.scale) = was;
                break;
            }

            (diagram, plan) = (smaller, laid);
        }

        return (diagram, plan);
    }

    /// <summary>Every participant's own box, measured for what is written in it.</summary>
    private void Measured(Diagram diagram, SequenceConfig config, Plan plan)
    {
        foreach (var one in diagram.Drawn)
        {
            var words = Naming(one, config);
            var taken = PortableDiagramWords.Taken(words);
            var carded = one.Card?.Shape;
            var figured = carded is null && PortableSequenceGlyphs.Figured(one.Kind);
            var marked = carded is null && PortableSequenceGlyphs.Iconed(one.Kind) ? Icon + Air : 0;

            var column = new Column
            {
                One = one,
                Words = words,
                Links = [.. one.Links.Select(link => (link, Naming(link, config)))],
                Carded = carded,
                Width = Math.Max(taken.Width + (Air * 2) + marked + (carded is { } wide ? PortableDiagramCard.Wider(wide) : 0),
                                 figured ? Figuring + Air : Air * 4),
                Deep = figured
                    ? Figuring + taken.Height + Air
                    : taken.Height + (Air * 2) + (carded is { } deep ? PortableDiagramCard.Deeper(deep) : 0),
                Figured = figured,
                Marked = marked > 0,
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
    /// How a diagram drawn as a sequence diagram but written in another language's words differs from one: how its lines are read,
    /// and how its cards and its key are painted. A sequence diagram's own differs in nothing, and is this as it is.
    /// </summary>
    internal class Dialect
    {
        /// <summary>The block's lines read into what is drawn — a sequence diagram's, by the sequence diagram's own reader.</summary>
        public virtual Diagram Read(ContentPart root, SequenceConfig config) => new Reader().Read(root, config);

        /// <summary>
        /// What a participant's card is painted with, where the diagram grades its cards by what each one is — a C4 sequence does,
        /// by the abstraction each element sits at. Null where the diagram grades nothing, and the theme's own surface is the answer.
        /// </summary>
        public virtual (LayoutInk Fill, LayoutInk Stroke, LayoutInk Ink, LayoutInk Muted)? Toned(Card card) => null;

        /// <summary>
        /// What a row of the key is swatched with where the diagram grades what it draws and the row names one of its bands — so the
        /// key shows the colour the cards it names are painted. Null where the diagram grades nothing.
        /// </summary>
        public virtual LayoutInk? Swatch(Legend row) => null;
    }

    /// <summary>
    /// What is written in a participant's box: its name, and — where the diagram writes a card rather than a plain box — the
    /// line saying what kind of thing it is and the sentence saying what it does, each smaller than the name above it.
    /// </summary>
    private IReadOnlyList<PortableDiagramWords> Naming(Participant one, SequenceConfig config)
    {
        var room = Math.Max(20, config.Widest - (Air * 2));
        var painted = one.Card is { } graded ? Spoken.Toned(graded) : null;
        var ink = painted?.Ink ?? Ink.Written(one.Card?.Ink) ?? Style.Text;
        var name = Words.Wrapped(one.Said, one.SaidHole, config.NameText, ink, room,
                                 one.Card is null ? null : 600);

        if (one.Card is not { } card) return name;

        var rows = new List<PortableDiagramWords>(name);

        // The stereotype is worked out from several things written, so it is pressed rather than typed into — and a press on it
        // means what it was worked out from.
        if (card.Stereotype is { Length: > 0 } says)
            rows.Add(Words.Worked(says, card.Technology ?? one.Part, Math.Max(8, config.NameText - 3),
                                  painted?.Muted ?? Style.TextMuted));

        if (card.Said is { Length: > 0 })
            rows.AddRange(Words.Wrapped(card.Said, null, Math.Max(8, config.NameText - 2), ink, room));

        return rows;
    }

    /// <summary>
    /// How far apart the lifelines stand: the boxes either side and the air between them at least, and wider where what is
    /// drawn between two of them would not otherwise fit.
    /// </summary>
    private void Spread(Diagram diagram, SequenceConfig config, Plan plan)
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
                case Message message when ColumnOf(plan, message.From) is { } from && ColumnOf(plan, message.To) is { } to:
                    var wide = PortableDiagramWords.Taken(Saying(message, config)).Width;

                    if (from == to) right[from] = Math.Max(right[from], Loop + wide + Air);
                    else Widen(gaps, plan, Math.Min(from, to), Math.Max(from, to), wide + (Air * 2));

                    break;

                case Note note:
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
    private void Spaced(SequenceConfig config, Plan plan, Note note, double[] gaps, double[] left, double[] right)
    {
        var over = note.Over.Select(id => ColumnOf(plan, id)).OfType<int>().ToList();
        if (over.Count == 0) return;

        var room = PortableDiagramWords.Taken(Noting(note, config)).Width + (config.Noted * 2);
        var (first, last) = (over.Min(), over.Max());

        switch (note.Place)
        {
            case Place.RightOf:
                right[last] = Math.Max(right[last], room + config.Noted);
                break;

            case Place.LeftOf:
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
    private void Rows(Diagram diagram, SequenceConfig config, Plan plan)
    {
        var y = plan.HeadBottom + config.Downward;
        var open = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var going = new HashSet<string>(StringComparer.Ordinal);
        var frames = new Stack<string>();

        // An activate or a deactivate written straight after a message is that message's own, as a + or - on it is: the bar starts
        // or ends where its line runs, rather than a row further down, and the line meets the bar rather than the lifeline.
        var turned = new HashSet<Turn>(ReferenceEqualityComparer.Instance);
        double? after = null;

        for (var at = 0; at < diagram.Items.Count; at++)
        {
            var item = diagram.Items[at];

            switch (item)
            {
                case Message message:
                    var next = diagram.Items.Skip(at + 1).TakeWhile(following => following is Turn).Cast<Turn>().ToList();
                    var starting = next.Where(turn => turn.On && string.Equals(turn.Id, message.To, StringComparison.Ordinal)).Take(1).ToList();
                    var stopping = next.Where(turn => !turn.On && string.Equals(turn.Id, message.From, StringComparison.Ordinal)).Take(1).ToList();

                    turned.UnionWith(starting);
                    turned.UnionWith(stopping);

                    y = Placed(config, plan, message, y, open, going, frames, message.Starts || starting.Count > 0, message.Stops || stopping.Count > 0);
                    after = plan.Rows[message].Arrow;
                    break;

                case Note note:
                    y = Placed(config, plan, note, y, frames);
                    break;

                case Turn turn:
                    var where = after ?? y;
                    plan.Rows[turn] = new Row { Top = where, Bottom = where };

                    if (turned.Contains(turn)) break;

                    if (turn.On) Opened(open, turn.Id, where);
                    else Shut(plan, open, turn.Id, where);

                    break;

                case Gone gone:
                    plan.Rows[gone] = new Row { Top = y, Bottom = y };
                    going.Add(gone.Id);

                    if (plan.Named.TryGetValue(gone.Id, out var ending)) ending.Ends = y;
                    break;

                case Opening opening:
                    var deep = opening.Kind == Frame.LayoutRect
                        ? config.Framed
                        : Tabbed(opening, config).Height + config.Framed;

                    plan.Rows[opening] = new Row { Top = y, Bottom = y + deep };
                    plan.Spans[opening.Key] = new Span();
                    frames.Push(opening.Key);
                    y += deep;

                    break;

                case Divider divider:
                    var said = PortableDiagramWords.Taken(Dividing(divider, config));
                    var band = said.Height + config.Framed;

                    plan.Rows[divider] = new Row { Top = y, Bottom = y + band };
                    y += band;

                    break;

                case Closing closing:
                    plan.Rows[closing] = new Row { Top = y, Bottom = y + config.Framed };
                    y += config.Framed;

                    if (frames.Count > 0) frames.Pop();
                    break;
            }

            if (item is not (Message or Turn)) after = null;
        }

        plan.FootTop = y + config.Downward;

        // A bar nothing ends runs to the foot of its lifeline.
        foreach (var bars in open)
            foreach (var from in bars.Value)
                plan.Bars.Add(new Working(bars.Key, from, plan.FootTop, 0));
    }

    /// <summary>One message's row: where its line runs, and whether it <paramref name="starts"/> a bar where it arrives or <paramref name="stops"/> the one it leaves.</summary>
    private double Placed(SequenceConfig config, Plan plan, Message message, double y,
                          Dictionary<string, List<double>> open, HashSet<string> going, Stack<string> frames, bool starts, bool stops)
    {
        var said = Saying(message, config);
        var taken = PortableDiagramWords.Taken(said);

        var arrow = message.Self ? y + Clear : y + taken.Height + Clear;
        var drop = message.Self ? Math.Max(Loop * 0.6, taken.Height + (Clear * 2)) : 0;

        if (stops) Shut(plan, open, message.From, arrow);
        if (starts) Opened(open, message.To, arrow);

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
    private double Placed(SequenceConfig config, Plan plan, Note note, double y, Stack<string> frames)
    {
        var words = Noting(note, config);
        var taken = PortableDiagramWords.Taken(words);
        var over = note.Over.Select(id => ColumnOf(plan, id)).OfType<int>().ToList();

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
            Place.RightOf => (last.Centre + config.Noted, last.Centre + config.Noted + room),
            Place.LeftOf => (first.Centre - config.Noted - room, first.Centre - config.Noted),
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
    private void Sized(Diagram diagram, SequenceConfig config, Plan plan)
    {
        plan.Bottom = plan.FootTop + (config.Mirrored ? plan.HeadDeep : 0);

        var links = plan.Columns.Max(column => column.Links.Count);
        if (links > 0) plan.Bottom += config.Framed + (links * (config.NoteText + 4));

        foreach (var column in plan.Columns)
        {
            // A lifeline runs down to the top of its own foot, wherever that stands in the band the feet share — not to the top of
            // the band, which is the top of the deepest of them.
            if (column.Ends <= 0) column.Ends = config.Mirrored ? Head(column, plan.FootTop, plan.HeadDeep).Top : plan.Bottom;
            if (column.Born <= 0) column.Born = plan.HeadTop;
        }

        foreach (var opening in diagram.Items.OfType<Opening>())
        {
            if (!plan.Rows.TryGetValue(opening, out var row) || !plan.Spans.TryGetValue(opening.Key, out var span)) continue;

            var closing = diagram.Items.OfType<Closing>()
                                 .FirstOrDefault(shut => string.Equals(shut.Key, opening.Key, StringComparison.Ordinal));

            var bottom = closing is not null && plan.Rows.TryGetValue(closing, out var shutting) ? shutting.Bottom : plan.FootTop;

            var (left, right) = span.Written
                ? (span.Left - config.Framed, span.Right + config.Framed)
                : (plan.Columns[0].Centre - config.Framed, plan.Columns[^1].Centre + config.Framed);

            plan.Frames[opening.Key] = new LayoutRect(left, row.Top, Math.Max(0, right - left), Math.Max(0, bottom - row.Top));
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

            plan.Boxes[box.Key] = new LayoutRect(left, depth * plan.Banding, Math.Max(0, right - left),
                                                 Math.Max(0, plan.Bottom - (depth * plan.Banding)));
        }

        var edges = plan.Frames.Values.Concat(plan.Boxes.Values).ToList();
        var last = plan.Columns[^1];

        plan.Size = new LayoutSize(Math.Max(last.Centre + (last.Width / 2) + plan.Right,
                                            edges.Count > 0 ? edges.Max(rect => rect.Right) + config.Across : 0),
                                   plan.Bottom + config.Downward);

        Keyed(diagram, config, plan);
    }

    /// <summary>Every lifeline a box is drawn round: the ones written in it, and the ones written in the boxes inside it.</summary>
    private static IEnumerable<Column> Holding(Diagram diagram, Plan plan, Box box) =>
        plan.Columns
            .Where(column => column.One.Box is { } key
                             && (string.Equals(key, box.Key, StringComparison.Ordinal) || Within(diagram, key, box.Key)));

    /// <summary>Whether a box is inside another, however deep.</summary>
    private static bool Within(Diagram diagram, string key, string over)
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
    private static int Deep(Diagram diagram, Box box)
    {
        var depth = 0;

        for (var held = box; held?.Parent is { } parent; depth++)
            held = diagram.Boxes.FirstOrDefault(other => string.Equals(other.Key, parent, StringComparison.Ordinal));

        return depth;
    }

    /// <summary>The key under the diagram, where it asks for one — a row per thing it draws in a colour of its own.</summary>
    private void Keyed(Diagram diagram, SequenceConfig config, Plan plan)
    {
        if (diagram.Legend.Count == 0) return;

        var rows = diagram.Legend
            // A row with no colour of its own keeps an outlined square, which is what says it is a row of the key at all.
            .Select(row => new PortableDiagramKey(null, Spoken.Swatch(row) ?? Ink.Written(row.Fill) ?? Ink.Written(row.Border),
                                                  [Words.Worked(row.Says, null, config.NoteText, Style.Text)]))
            .ToList();

        // Across the foot of the diagram rather than down it: the key reads as a caption, and does not make the diagram deeper by a
        // row for everything it names.
        plan.Key = new PortableDiagramLegend(rows, [MermaidPiece.Words], across: true, Style.CodeBorder);
        plan.KeyAt = new LayoutPoint(config.Across, plan.Size.Height);
        plan.Size = new LayoutSize(Math.Max(plan.Size.Width, plan.Key.Size.Width + (config.Across * 2)),
                                   plan.Size.Height + plan.Key.Size.Height + config.Downward);
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>The boxes grouping the participants, drawn behind everything.</summary>
    private void Grouped(PortableBuilder build, Diagram diagram, Plan plan)
    {
        if (plan.Boxes.Count == 0) return;

        build.Open(SequencePiece.Boxes, part: null, stops: PortableStops.None);

        foreach (var box in diagram.Boxes)
        {
            if (!plan.Boxes.TryGetValue(box.Key, out var bounds)) continue;

            // A box with no colour written on it is see-through, as Mermaid's is: its outline is what groups the lifelines.
            var fill = Ink.Written(box.Colour) is { } tint ? PortableDiagramInk.Faded(build.Medium, tint, Wash) : null;
            var words = Words.Wrapped(box.Said, null, diagram.Config.NameText, Style.Text, Math.Max(20, bounds.Width));
            var heading = new LayoutRect(bounds.X, bounds.Y, bounds.Width, plan.Banding);

            build.Open(SequencePiece.Box, box.Whole, stops: PortableStops.None);
            PortableDiagramShapes.Draw(build, MermaidPiece.Shape, box.Part, DiagramShape.Rounded, bounds, fill,
                                       new PortableDiagramStroke(Style.CodeBorder, Thick),
                                       PortableDiagramWords.Placed(words, heading, MermaidPiece.Words));
            build.Close();
        }

        build.Close();
    }

    /// <summary>The lifelines: each participant's own box, the line running down from it, and the bars along it.</summary>
    private void Lifelines(PortableBuilder build, Diagram diagram, Plan plan)
    {
        var config = diagram.Config;

        build.Open(SequencePiece.Lifelines, part: null, stops: PortableStops.None);

        foreach (var column in plan.Columns)
        {
            // A lifeline runs from the foot of its own head rather than from the foot of the band they all stand in: the
            // heads are set in the middle of that band, so a shallower one would otherwise hang above its own line.
            var top = Head(column, column.Born, plan.HeadDeep).Bottom;
            var ends = Math.Max(top, column.Ends);

            build.Open(SequencePiece.Lifeline, column.One.Part, stops: PortableStops.None);

            build.Open(MermaidPiece.Shape, column.One.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Line(new LayoutPoint(column.Centre, top), new LayoutPoint(column.Centre, ends)),
                                                     null, Style.TextMuted, 1, dashes: PortableDiagramStroke.Dotted));

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
    private static LayoutRect Head(Column column, double from, double band)
    {
        var outline = column.Carded;
        var over = outline is { } which ? PortableDiagramCard.Above(which) : 0;
        var under = outline is { } held ? PortableDiagramCard.Deeper(held) - PortableDiagramCard.Above(held) : 0;

        var top = from + ((band - column.Deep) / 2) + ((under - over) / 2);

        return new LayoutRect(column.Centre - (column.Width / 2), top, column.Width, column.Deep);
    }

    /// <summary>One participant's own box or figure, with its name in it or under it.</summary>
    private void Standing(PortableBuilder build, Column column, double from, double band)
    {
        var bounds = Head(column, from, band);

        build.Open(SequencePiece.Head, column.One.Part, stops: PortableStops.None);

        if (column.Figured)
        {
            var glyph = new LayoutRect(column.Centre - (Figuring / 2), bounds.Y, Figuring, Figuring);
            var room = new LayoutRect(bounds.X, glyph.Bottom, bounds.Width, Math.Max(0, bounds.Bottom - glyph.Bottom));
            var placed = PortableDiagramWords.Placed(column.Words, room, MermaidPiece.Words);

            build.Open(MermaidPiece.Shape, column.One.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, PortableSequenceGlyphs.Figure(build.Medium, column.One.Kind, glyph), null, Style.Text, Thick));
            build.Occupies(build.Medium.Rectangle(glyph));
            build.Close();

            foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        }
        else
        {
            var shape = column.Carded is { } carded
                ? PortableDiagramCard.Outline(build.Medium, carded, bounds)
                : PortableSequenceGlyphs.Shape(build.Medium, bounds);

            var mark = new LayoutRect(bounds.X + Air, bounds.Y + ((bounds.Height - Icon) / 2), Icon, Icon);
            var room = column.Carded is { } inner ? PortableDiagramCard.Inside(inner, bounds)
                : column.Marked ? new LayoutRect(mark.Right, bounds.Y, Math.Max(0, bounds.Right - mark.Right), bounds.Height)
                : bounds;

            var placed = PortableDiagramWords.Placed(column.Words, room, MermaidPiece.Words);

            var painted = column.One.Card is { } graded ? Spoken.Toned(graded) : null;

            build.Open(MermaidPiece.Shape, column.One.Part, stops: PortableStops.None);
            var edge = painted?.Stroke ?? Ink.Written(column.One.Card?.Border) ?? Ink.NodeEdge;
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, shape, painted?.Fill ?? Ink.Written(column.One.Card?.Fill) ?? Ink.Node, edge, Thick));
            if (column.Marked) build.Draw(PortableDiagramMarks.Outlined(build.Medium, PortableSequenceGlyphs.Mark(build.Medium, column.One.Kind, mark), null, edge, Thick * 0.8));
            build.Occupies(Less(build.Medium, shape, placed));
            build.Close();

            foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        }

        build.Close();
    }

    /// <summary>The bar saying a participant is working, set aside as far as the bars it is nested in are deep.</summary>
    private void Barred(PortableBuilder build, SequenceConfig config, Column column, Working bar)
    {
        var bounds = new LayoutRect(column.Centre - (config.Bar / 2) + (bar.Depth * (config.Bar / 2)), bar.From,
                                    config.Bar, Math.Max(1, bar.To - bar.From));

        build.Open(SequencePiece.Bar, column.One.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Rectangle(bounds), Style.CodeBg, Style.CodeBorder, 1));
        build.Occupies(build.Medium.Rectangle(bounds));
        build.Close();
    }

    /// <summary>Where a participant leads, written under it — a menu of them set out, since a page here pops none.</summary>
    private void Leading(PortableBuilder build, SequenceConfig config, Plan plan, Column column)
    {
        if (column.Links.Count == 0) return;

        var y = plan.FootTop + (config.Mirrored ? plan.HeadDeep : 0) + config.Framed;

        foreach (var (link, words) in column.Links)
        {
            build.Open(SequencePiece.Menu, link.Part, stops: PortableStops.None);
            build.Links(link.Url, link.Url);
            words.Set(build, new LayoutPoint(column.Centre - (words.Width / 2), y), MermaidPiece.Words);
            build.Close();

            y += config.NoteText + 4;
        }
    }

    /// <summary>The timeline, frames holding the messages and the notes written inside them.</summary>
    private void Timeline(PortableBuilder build, Diagram diagram, Plan plan)
    {
        var config = diagram.Config;
        var depth = 0;

        build.Open(SequencePiece.Timeline, part: null, stops: PortableStops.None);

        foreach (var item in diagram.Items)
        {
            switch (item)
            {
                case Opening opening:
                    build.Open(SequencePiece.Frame, opening.Whole, stops: PortableStops.None);
                    depth++;
                    Framing(build, config, plan, opening);

                    break;

                case Closing when depth > 0:
                    depth--;
                    build.Close();

                    break;

                case Divider divider:
                    Divided(build, config, plan, divider);
                    break;

                case Message message:
                    Lined(build, config, plan, message);
                    break;

                case Note note:
                    Beside(build, config, plan, note);
                    break;
            }
        }

        while (depth-- > 0) build.Close();
        build.Close();
    }

    /// <summary>A frame: its border, the tab saying what kind it is, and what it holds its messages under.</summary>
    private void Framing(PortableBuilder build, SequenceConfig config, Plan plan, Opening opening)
    {
        if (!plan.Frames.TryGetValue(opening.Key, out var bounds)) return;

        if (opening.Kind == Frame.LayoutRect)
        {
            var wash = Ink.Written(opening.Colour) is { } tint ? PortableDiagramInk.Faded(build.Medium, tint, Wash) : null;

            build.Open(MermaidPiece.Shape, opening.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Rectangle(bounds), wash, null, 0));
            build.Occupies(PortableDiagramFrame.Round(build.Medium, bounds));
            build.Close();

            return;
        }

        var word = Naming(opening, config);
        var tab = PortableDiagramFrame.Tabbed(word, config.Tabbed, new LayoutSize(config.TabWidth, config.TabHeight));
        // What a frame is about is its title, so it is set across the middle of the frame, clear of the tab on either side — and
        // beside the tab where the frame is too narrow to leave room for it there.
        var beside = PortableDiagramFrame.Beside(bounds, tab, config.Tabbed);
        var clear = tab.Width + config.Tabbed;
        var centred = bounds.Width - (clear * 2) >= Titled;
        var title = centred ? new LayoutRect(bounds.X + clear, beside.Y, bounds.Width - (clear * 2), beside.Height) : beside;
        var said = Words.Wrapped(opening.Said, null, config.SaidText, Style.TextMuted, Math.Max(20, title.Width));

        build.Open(MermaidPiece.Shape, opening.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Rectangle(bounds), null, Style.CodeBorder, Thick));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, PortableDiagramFrame.Tab(build.Medium, bounds, tab), Style.CodeBg, Style.CodeBorder, Thick));
        build.Occupies(PortableDiagramFrame.Round(build.Medium, bounds, tab));
        build.Close();

        word?.Set(build, PortableDiagramFrame.Word(bounds, tab, word), MermaidPiece.Words);

        foreach (var (words, at, kind) in PortableDiagramWords.Placed(said, title, MermaidPiece.Words,
                                                                      centred ? LayoutAlign.Centre : LayoutAlign.Left))
            words.Set(build, at, kind);
    }

    /// <summary>A line across a frame where it is divided, and what the part under it holds.</summary>
    private void Divided(PortableBuilder build, SequenceConfig config, Plan plan, Divider divider)
    {
        if (!plan.Rows.TryGetValue(divider, out var row)) return;
        if (!plan.Frames.TryGetValue(divider.Key, out var bounds)) return;

        var said = Dividing(divider, config);
        var at = row.Top + (config.Framed / 2);
        var room = new LayoutRect(bounds.X + config.Framed, at, Math.Max(20, bounds.Width - (config.Framed * 2)),
                                  Math.Max(0, row.Bottom - at));

        build.Open(SequencePiece.Divider, divider.Part, stops: PortableStops.None);

        build.Open(MermaidPiece.Shape, divider.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Line(new LayoutPoint(bounds.Left, at), new LayoutPoint(bounds.Right, at)),
                                                 null, Style.CodeBorder, 1, dashes: PortableDiagramStroke.Dashed));
        build.Close();

        foreach (var (words, where, kind) in PortableDiagramWords.Placed(said, room, MermaidPiece.Words))
            words.Set(build, where, kind);

        build.Close();
    }

    /// <summary>One message: its line between the lifelines, or the loop off one where it goes to itself.</summary>
    private void Lined(PortableBuilder build, SequenceConfig config, Plan plan, Message message)
    {
        if (!plan.Rows.TryGetValue(message, out var row)) return;

        var stroke = new PortableDiagramStroke(Ink.Written(message.Ink) ?? Ink.Link, Thick,
                                               message.Dotted ? PortableDiagramStroke.Dashed : null);
        var taken = PortableDiagramWords.Taken(row.Said);

        build.Open(SequencePiece.Message, message.Part, stops: PortableStops.None);

        if (message.Self)
        {
            var away = row.From + Loop;
            var drop = Math.Max(Loop * 0.6, taken.Height + (Clear * 2));

            PortableDiagramConnector.Draw(build, SequencePiece.Line, message.Part,
                                          [new LayoutPoint(row.From, row.Arrow), new LayoutPoint(away, row.Arrow),
                                   new LayoutPoint(away, row.Arrow + drop), new LayoutPoint(row.From, row.Arrow + drop)],
                                          stroke, Headed(message.Near), Headed(message.Far), curved: !config.Square);

            var room = new LayoutRect(away + Clear, row.Arrow, Math.Max(20, taken.Width), Math.Max(0, drop));

            foreach (var (words, at, kind) in PortableDiagramWords.Placed(row.Said, room, MermaidPiece.Words, LayoutAlign.Left))
                words.Set(build, at, kind);
        }
        else
        {
            PortableDiagramConnector.Draw(build, SequencePiece.Line, message.Part,
                                          [new LayoutPoint(row.From, row.Arrow), new LayoutPoint(row.To, row.Arrow)],
                                          stroke, Headed(message.Near), Headed(message.Far));

            var room = new LayoutRect(Math.Min(row.From, row.To), row.Arrow - Clear - taken.Height,
                                      Math.Abs(row.To - row.From), taken.Height);

            foreach (var (words, at, kind) in PortableDiagramWords.Placed(row.Said, room, MermaidPiece.Words, Aligned(config.Aligned)))
                words.Set(build, at, kind);
        }

        if (message.Number is { Length: > 0 } number) Counted(build, message, row, number);

        build.Close();
    }

    /// <summary>The number a message is given, drawn in a circle on its line where it sets out — on the lifeline it leaves.</summary>
    private void Counted(PortableBuilder build, Message message, Row row, string number)
    {
        var words = Words.Worked(number, message.Part, Counting, Style.Text);
        var middle = new LayoutPoint(row.From, row.Arrow);
        var at = new LayoutPoint(middle.X - (words.Width / 2), middle.Y - (words.Height / 2));

        build.Open(SequencePiece.Number, message.Part, stops: PortableStops.None);
        build.Open(MermaidPiece.Shape, message.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Ellipse(middle, Counting, Counting), Ink.Surface, Ink.Link, 1));
        build.Close();

        words.Set(build, at, MermaidPiece.Words);
        build.Close();
    }

    /// <summary>One note, beside a lifeline or spanning several.</summary>
    private void Beside(PortableBuilder build, SequenceConfig config, Plan plan, Note note)
    {
        if (!plan.Rows.TryGetValue(note, out var row) || row.To <= row.From) return;

        var bounds = new LayoutRect(row.From, row.Top, row.To - row.From,
                                    Math.Max(1, row.Bottom - row.Top - (config.Framed / 2)));
        var placed = PortableDiagramWords.Placed(row.Said, Inside(bounds, config.Noted), MermaidPiece.Words,
                                                 Aligned(config.NoteAligned));

        build.Open(SequencePiece.Note, note.Part, stops: PortableStops.None);

        build.Open(MermaidPiece.Shape, note.Part, stops: PortableStops.None);
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Rectangle(bounds), Ink.Note, Ink.NoteEdge, Thick));
        build.Occupies(Less(build.Medium, build.Medium.Rectangle(bounds), placed));
        build.Close();

        foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        build.Close();
    }

    // ── What the pieces say ─────────────────────────────────────────────────

    /// <summary>
    /// What a message says, drawn over its line: what is written on it, and under that the smaller lines saying what it is done
    /// with and what it is for, which a C4 relationship writes and a sequence diagram's own message does not.
    /// </summary>
    private IReadOnlyList<PortableDiagramWords> Saying(Message message, SequenceConfig config)
    {
        var ink = Ink.Written(message.SaidInk) ?? Style.Text;

        var said = message.Said is null && message.SaidHole is null
            ? []
            : Words.Wrapped(message.Said, message.SaidHole, config.SaidText, ink, config.Wrapping);

        if (message.Under.Count == 0) return said;

        return
        [
            .. said,
            .. message.Under.SelectMany(under => Words.Wrapped(under, null, Math.Max(8, config.SaidText - 4), Style.TextMuted,
                                                               config.Wrapping)),
        ];
    }

    private IReadOnlyList<PortableDiagramWords> Noting(Note note, SequenceConfig config) =>
        note.Said is null && note.SaidHole is null
            ? []
            : Words.Wrapped(note.Said, note.SaidHole, config.NoteText, Style.Text, config.Wrapping);

    private IReadOnlyList<PortableDiagramWords> Dividing(Divider divider, SequenceConfig config) =>
        divider.Said is null ? [] : Words.Wrapped(divider.Said, null, config.SaidText, Style.TextMuted, config.Wrapping);

    private PortableDiagramWords? Naming(Opening opening, SequenceConfig config) =>
        opening.Word is null ? null : Words.Written(opening.Word, null, config.NoteText, Style.Text);

    private PortableDiagramWords Naming(Link link, SequenceConfig config) =>
        link.Said is { Length: > 0 }
            ? Words.Written(link.Said, null, config.NoteText, Style.Accent)
            : Words.Worked(link.Url, link.Part, config.NoteText, Style.Accent);

    private LayoutSize Tabbed(Opening opening, SequenceConfig config) =>
        PortableDiagramFrame.Tabbed(Naming(opening, config), config.Tabbed, new LayoutSize(config.TabWidth, config.TabHeight));

    private static LayoutAlign Aligned(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "left" => LayoutAlign.Left,
        "right" => LayoutAlign.Right,
        _ => LayoutAlign.Centre,
    };

    private static int? ColumnOf(Plan plan, string id) =>
        plan.Named.TryGetValue(id, out var column) ? plan.Columns.IndexOf(column) : null;

    private static LayoutRect Inside(LayoutRect bounds, double pad) =>
        bounds.Width > pad * 2 && bounds.Height > pad * 2 ? LayoutRect.Inflate(bounds, -pad, -pad) : bounds;

    /// <summary>What a shape stands in, less where its words are — so a press on them means them.</summary>
    private static LayoutShape Less(ILayoutMedium medium, LayoutShape outline, IReadOnlyList<(PortableDiagramWords Words, LayoutPoint At, string Kind)> placed)
    {
        if (placed.Count == 0) return outline;

        var covered = new List<LayoutShape>();
        foreach (var (words, at, _) in placed)
            covered.Add(medium.Rectangle(new LayoutRect(at, new LayoutSize(words.Width, words.Height))));

        var stands = medium.Combine(outline, medium.Group(covered), ShapeCombine.Exclude);

        return stands;
    }

    private static DiagramHead Headed(Tip head) => head switch
    {
        Tip.Arrow => DiagramHead.Arrow,
        Tip.Open => DiagramHead.Open,
        Tip.Cross => DiagramHead.Cross,
        Tip.HalfTop => DiagramHead.HalfTop,
        Tip.HalfBottom => DiagramHead.HalfBottom,
        Tip.StickTop => DiagramHead.StickTop,
        Tip.StickBottom => DiagramHead.StickBottom,
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

    private void Ended(Plan plan, HashSet<string> going, string id, double y)
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

    private void Crossed(PortableBuilder build, double x, double y)
    {
        var cross = new List<LayoutShape>();
        cross.Add(build.Medium.Line(new LayoutPoint(x - Ending, y - Ending), new LayoutPoint(x + Ending, y + Ending)));
        cross.Add(build.Medium.Line(new LayoutPoint(x + Ending, y - Ending), new LayoutPoint(x - Ending, y + Ending)));

        build.Draw(PortableDiagramMarks.Outlined(build.Medium, build.Medium.Group(cross), null, Style.Text, Thick));
    }

    // ── What is worked out while it is laid ─────────────────────────────────

    private sealed class Plan
    {
        public List<Column> Columns { get; } = [];

        public Dictionary<string, Column> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<Item, Row> Rows { get; } = [];

        public Dictionary<string, Span> Spans { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LayoutRect> Frames { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LayoutRect> Boxes { get; } = new(StringComparer.Ordinal);

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

    public LayoutSize Size { get; set; }

        /// <summary>The key under it, where the diagram asks for one, and where it goes.</summary>
        public PortableDiagramLegend? Key { get; set; }

        public LayoutPoint KeyAt { get; set; }
    }

    private sealed class Column
    {
        public Participant One { get; init; } = null!;

        public IReadOnlyList<PortableDiagramWords> Words { get; init; } = [];

        public IReadOnlyList<(Link Link, PortableDiagramWords Words)> Links { get; init; } = [];

        public double Width { get; init; }

        public double Deep { get; init; }

    public bool Figured { get; init; }

    /// <summary>Whether a mark of what it is is set before its name, in its box.</summary>
    public bool Marked { get; init; }

        /// <summary>The outline its card is drawn with, or null for a participant written as a name in a box.</summary>
        public DiagramCardShape? Carded { get; init; }

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

        public IReadOnlyList<PortableDiagramWords> Said { get; init; } = [];
    }

    private sealed class Span
    {
        public bool Written { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }
    }

    private sealed record Working(string Id, double From, double To, int Depth);
}