using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The sheet a diagram is drawn on: the title over it, what goes under it, what could not be read set beneath that, and the card
/// it all sits on — and the room the diagram is given on it, how it is fitted to that room, and what is shown instead where there
/// is nothing to draw.
///
/// <para>
/// <strong>A diagram's builder lays its sheet out; the sheet does not lay the diagram out.</strong> The builder draws its diagram at
/// the origin into a tree of its own, asks the sheet for its room (<see cref="Space"/>) and to fit it (<see cref="LaidNarrower"/>,
/// <see cref="Reduced"/>), hands it whatever goes under the drawing (<see cref="Beneath"/>), and finishes with <see cref="Laid"/>, which
/// sets the drawing under its title once the title's height is known — so no diagram has to know where its top is, and no diagram
/// is bound to how any other is drawn.
/// </para>
/// </summary>
/// <param name="asSource">What the block is, shown as it is written with each part blamed and why — the builder's own.</param>
/// <remarks>One sheet serves a builder for as long as it lives, and each lay starts it afresh (<see cref="Start"/>).</remarks>
internal sealed class DiagramSheet(EditState state, LayoutStyle style, ILayoutMedium medium, bool isReadOnly, DiagramText text,
                                           Func<IReadOnlyList<(ContentPart Part, string Reason)>, Laid> asSource)
{
    /// <summary>The face source a diagram could not lay out is set in, as the lines it was written as.</summary>
    private const string SourceFont = "Cascadia Code, Consolas, monospace";

    private const double SourceSize = 13;
    private const double TitleSize = 15;
    private const double ReasonSize = 12;

    /// <summary>Clear air under the title, and above the reasons.</summary>
    private const double TitleGap = 8;
    private const double ReasonGap = 6;

    /// <summary>The narrowest the reasons are set to, so a small diagram does not stack them a word to a line.</summary>
    private const double ReasonRoom = 260;

    /// <summary>Clear air between the card's edge and what is drawn on it.</summary>
    private const double Pad = 12;

    /// <summary>How round the card's corners are.</summary>
    private const double Corner = 6;

    /// <summary>How many times a diagram is laid again narrower before what it came to is what it is — see <see cref="LaidNarrower"/>.</summary>
    private const int Narrowings = 4;

    /// <summary>How a diagram sets the source it could not lay out at all: as the lines it was written as.</summary>
    public static LayoutCharacters Characters(LayoutStyle style) => new(style.Face(SourceFont), SourceSize, style.Text);

    /// <summary>
    /// How wide what a diagram draws may be: the room the block was given, less the card it is drawn on. What a builder fits its
    /// drawing into, and the whole of what the room means to it.
    /// </summary>
    public double Space => double.IsInfinity(room) ? room : Math.Max(1, room - (Pad * 2));

    /// <summary>The room the block was given, for the lay under way.</summary>
    private double room = double.PositiveInfinity;

    /// <summary>
    /// Starts a lay in <paramref name="given"/> room: nothing under the drawing yet, the drawing its own size, and nothing blamed —
    /// what one lay put on the sheet is never the next one's.
    /// </summary>
    public void Start(double given)
    {
        this.room = given;
        this.beneath.Clear();
        this.reduction = 1;
        this.blamed = null;
    }

    /// <summary>
    /// What goes under the drawing — a legend, a key, a caption — each as its own tree, with the air above it.
    /// </summary>
    private readonly List<(LayoutTree Tree, LayoutSize Size, double Gap)> beneath = [];

    /// <summary>How much smaller than it was laid out the drawing is set — 1 where its diagram did not ask (<see cref="Reduced"/>).</summary>
    private double reduction = 1;

    /// <summary>What the diagram blamed in asking to be shown as it is written, or null where it drew.</summary>
    private IReadOnlyList<(ContentPart Part, string Reason)>? blamed;

    /// <summary>
    /// Adds a piece under the drawing, as one of the block's own pieces rather than part of the drawing.
    ///
    /// <para>
    /// That is what keeps it lined up with everything else. A block is a stack — its title, its drawing, whatever goes
    /// under that, and anything it could not read — and the width of the block is the widest of them, which is not known
    /// until all of them have been measured. A piece a diagram sets down inside its own drawing is placed against the
    /// drawing's width, so a legend wider than the drawing widens the block underneath a drawing already off to one side
    /// of it. Handed over instead, it is measured with the rest and every one of them is set about the same middle.
    /// </para>
    /// </summary>
    public void Beneath(LayoutTree tree, LayoutSize size, double gap) => this.beneath.Add((tree, size, gap));

    /// <summary>
    /// A diagram laid at a measure — how wide its words are set, say — and, where that comes out wider than the room, laid again
    /// at a narrower one until it fits or can go no narrower: the measure narrowed in step with how far the last lay overflowed,
    /// never below <paramref name="least"/>. One way a diagram reshapes itself to the room before it is <see cref="Reduced"/>.
    ///
    /// <para>
    /// A lay is kept only where it came out narrower than the one before, since what a measure narrows is not all of what makes a
    /// diagram wide — words set narrower stand on more lines, and a map is as wide as its branches are deep — so another pass would
    /// come to the same thing. Laid again more than once for the same reason: the second lay is not the first one in proportion.
    /// </para>
    /// </summary>
    /// <param name="lay">The diagram laid at a measure.</param>
    /// <param name="wide">How wide a lay came out.</param>
    public TLaid LaidNarrower<TLaid>(double measure, double least, Func<double, TLaid> lay, Func<TLaid, double> wide)
    {
        var laid = lay(measure);
        if (double.IsInfinity(Space)) return laid;

        for (var attempt = 0; attempt < Narrowings && wide(laid) > Space; attempt++)
        {
            var narrower = Math.Max(least, measure * Space / wide(laid));
            if (narrower >= measure) break;

            var again = lay(narrower);
            if (wide(again) >= wide(laid)) break;

            (measure, laid) = (narrower, again);
        }

        return laid;
    }

    /// <summary>
    /// Sets the drawing smaller where it is wider than the room, and hands back the size it then takes — the last thing a diagram
    /// does to fit, once it has reshaped itself to the room as far as it can.
    ///
    /// <para>
    /// Only the diagram knows how far that is, so only the diagram asks. Whatever is still too wide is drawn smaller as a whole —
    /// its words, its lines and the air between them together, about its top left, as the layout paints, presses and caret it
    /// (<see cref="LayoutPaint.Scale"/>) — and never larger.
    /// </para>
    /// </summary>
    public LayoutSize Reduced(LayoutSize drawn)
    {
        if (double.IsInfinity(Space) || drawn.Width <= Space) return drawn;

        this.reduction = Space / drawn.Width;
        return new LayoutSize(Space, drawn.Height * this.reduction);
    }

    /// <summary>
    /// Says the diagram is to be shown as it is written — what a diagram with nothing to draw shows — with each part it could
    /// make nothing of blamed, and why. Where it blames nothing and nothing else is wrong, every line it could make nothing of
    /// is. Hands back the size a diagram that draws nothing takes, which is none.
    /// </summary>
    public LayoutSize AsWritten(params (ContentPart Part, string Reason)[] blamed)
    {
        this.blamed = blamed;
        return default;
    }

    /// <summary>
    /// The block laid out: the diagram drawn into <paramref name="diagram"/>, <paramref name="body"/> big, set under its title with what
    /// goes under it beneath, on its card — or shown as it is written, where the diagram asked to be or what is wrong in it cannot be
    /// put right where it is drawn.
    /// </summary>
    /// <param name="title">The title to set over the diagram — its own where it writes one, else the front matter's.</param>
    /// <param name="titleInk">The colour the front matter asks the title to be written in, or null for the theme's heading.</param>
    /// <param name="titleSize">How big the front matter asks the title to be set, or null for the size every diagram's title is.</param>
    public Laid Laid(MermaidBlock block, LayoutBuilder diagram, LayoutSize body,
                             (ContentPart? Part, string? Says, bool AsWritten) title, LayoutInk? titleInk = null, double? titleSize = null)
    {
        var troubled = ContentNested.OwnParts(block.Reading.Root).Where(part => part.Trouble is not null && !part.Derived).ToList();

        // A diagram with nothing to draw is shown as it is written, with what was wrong and what could not be drawn marked in
        // it — or, where nothing says why, every line it could make nothing of.
        if (this.blamed is { } blamed)
        {
            IReadOnlyList<(ContentPart Part, string Reason)> said = [.. troubled.Select(part => (part, part.Trouble!)), .. blamed];
            if (said.Count == 0) said = Unread(block);
            return asSource(said);
        }

        // Something wrong in what did draw is marked where it is drawn — but only where the reader can put it right there:
        // the block is being written in, and each wrong part is drawn as words that are typed into. Otherwise the block is
        // shown as it is written, with the wrong parts marked in it.
        var drawn = diagram.Seal();
        if (troubled.Count > 0 && (isReadOnly || troubled.Any(part => !Typed(drawn, part))))
            return asSource([.. troubled.Select(part => (part, part.Trouble!))]);

        var trouble = troubled.Select(part => LayoutDiagnostic.Of(part, part.Trouble!)).ToList();

        var build = new LayoutBuilder(medium);
        // The whole block, so choosing it chooses all of it — but no place for a caret of its own: what is written in a
        // diagram is its lines, and the card they are drawn on is not somewhere to write.
        build.Open(MermaidPiece.Diagram, block.Reading.Root, stops: LayoutStops.None);

        // Everything the block stacks is measured before any of it is placed, and the width of the block is the widest of
        // them. A piece set down before that is known sits off to one side of the block as soon as a later one turns out to
        // be wider — which is how a drawing comes to stand off-centre under its own title, or beside its own legend.
        var (titlePart, titleSays, titleAsWritten) = title;

        IShapedText? heading = null;
        var maps = false;
        var ink = style.Heading;

        if (titlePart is not null && !string.IsNullOrWhiteSpace(titleSays))
        {
            // What was written, where the reader is writing in it — a front-matter title says one thing and is written
            // as another, quotes and all, and only the characters they typed can be typed into.
            var written = state.Raw is { } raw && raw.Start <= titlePart.Start && raw.End >= titlePart.End();

            var says = written ? titlePart.Text : titleSays!;
            maps = written || titleAsWritten;
            ink = titleInk ?? style.Heading;
            heading = text.Text(says, titleSize ?? TitleSize, ink, 600);
        }

        var width = body.Width;

        // The title is set no wider than the room there is; a diagram asked to be wider than that keeps its width.
        if (heading is not null) width = Math.Max(width, Math.Min(heading.WidthWithSpace, Space));
        foreach (var piece in this.beneath) width = Math.Max(width, piece.Size.Width);

        var top = Pad;

        if (heading is not null)
        {
            LayoutText.Words(build, heading, new LayoutPoint(Pad, top), width, LayoutAlign.Centre, titlePart,
                               MermaidPiece.Title, maps: maps, writes: true, ink: ink);
            top += heading.Height + TitleGap;
        }

        var at = new LayoutPoint(Pad + Math.Max(0, (width - body.Width) / 2), top);
        if (this.reduction == 1)
        {
            build.Graft(drawn, at);
        }
        else
        {
            build.Open(MermaidPiece.Reduced, part: null, at, stops: LayoutStops.None, paints: new LayoutPaint(Scale: this.reduction));
            build.Graft(drawn);
            build.Close();
        }

        top += body.Height;

        foreach (var (tree, taken, gap) in this.beneath)
        {
            top += gap;
            build.Graft(tree, new LayoutPoint(Pad + Math.Max(0, (width - taken.Width) / 2), top));
            top += taken.Height;
        }

        foreach (var reason in trouble.Select(diagnostic => diagnostic.Message).Distinct())
        {
            var said = text.Text(reason, ReasonSize, style.Danger);
            said.Bound(Math.Min(Math.Max(width, ReasonRoom), Space), LayoutAlign.Left);

            top += ReasonGap;
            build.Open(MermaidPiece.Trouble, part: null, new LayoutPoint(Pad, top), stops: LayoutStops.None);
            build.Draw(new TextMark(said, default, style.Danger));
            build.Close();

            width = Math.Max(width, said.Width);
            top += said.Height;
        }

        var size = new LayoutSize(width + (Pad * 2), top + Pad);
        Card(build, size);

        build.Close();
        return new Laid(build.Seal(), size.Width, size.Height, trouble);
    }

    /// <summary>
    /// Whether a part of the tree is drawn as words the reader types into — itself, or something inside it — anywhere in the drawing:
    /// a diagram drawn as several pieces side by side, its layers or its lanes, writes its words in any of them.
    /// </summary>
    private static bool Typed(LayoutTree drawn, ContentPart part)
    {
        var inside = part.SelfAndDescendants().ToHashSet();

        for (var at = 0; at < drawn.Count; at++)
            if (drawn.WordsOf(at) is { Maps: true } && drawn.PartOf(at) is ContentPart written && inside.Contains(written)) return true;

        return false;
    }

    /// <summary>Every line of a diagram shown as it is written that says anything — not its header, a comment or a directive.</summary>
    private IReadOnlyList<(ContentPart Part, string Reason)> Unread(MermaidBlock block)
    {
        var reason = block.Keyword?.Text is { Length: > 0 } named
            ? Formatted("Text.Mermaid.NothingDrawn", named)
            : style.Says("Text.Mermaid.NothingDrawnUnnamed");

        return [.. block.Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == MermaidKinds.Line && part.Stated() is { Kind: not (Kinds.Comment or MermaidKinds.Directive or MermaidKinds.Header) })
            .Select(line => (line, reason))];
    }

    /// <summary>What a key of the window's language says with <paramref name="value"/> in it — the key as it is, where it holds no place for one.</summary>
    private string Formatted(string key, object value)
    {
        var said = style.Says(key);
        try { return string.Format(CultureInfo.CurrentCulture, said, value); }
        catch (FormatException) { return said; }
    }

    /// <summary>The card a diagram sits on, same surface and border as a code block. Drawn last but painted first, so it can be
    /// sized to what the diagram turned out to be.</summary>
    private void Card(LayoutBuilder build, LayoutSize size)
    {
        var card = new LayoutRect(0, 0, size.Width, size.Height);
        build.Draw(new WashMark(card, style.CodeBg));
        build.Draw(DiagramMarks.Outlined(medium, medium.Rectangle(card, Corner, Corner), null, style.CodeBorder, 1));
    }
}
