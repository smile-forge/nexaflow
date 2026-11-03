using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using System.Linq.Expressions;
using System.Reflection;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Painting;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud.Stages;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What turns content into a layout, and the only thing that does: the language it is written in parses it, whatever it holds
/// written in another language is parsed by that language, the parse is worked over by the language's stages, and the
/// language's builder lays it out.
///
/// <para>
/// <strong>Every step is the engine's, and each happens once.</strong> A language describes its parser, its stages and its
/// builder and calls none of them (<see cref="ContentLanguage"/>). A builder parses nothing: where it meets content in
/// another language it asks <see cref="Nesting"/> — the engine — to lay that out at the room it is giving it, and sets
/// down what comes back.
/// </para>
/// <para>
/// <strong>What another language wrote is parsed before anything is worked over.</strong> Each piece is read by its own
/// parser and put in the body of the node holding it (<see cref="ContentNested"/>), and so on down, until nothing is left
/// unparsed — so the tree a builder is handed holds every language it contains. Each language's stages see only their own
/// language's nodes: the characters of a piece in another language are what they work over, and the piece's tree goes back
/// in once they are done, where it was written.
/// </para>
/// <para>
/// One per showing of some content, because what it keeps is that content's: the parse of a document read again as it is
/// written, the blocks that read as they did last time, and what was read of every piece in another language, so a keystroke
/// in a paragraph does not read the diagram under it again.
/// </para>
/// </summary>
/// <param name="inputs">What the host says about the content it shows — see <see cref="Inputs"/>.</param>
public sealed partial class ContentEngine(ContentInputs? inputs = null)
{
    /// <summary>What a piece of content read to: its language's tree, a block in that language.</summary>
    private sealed record Parsed(ContentLanguage Language, string Named, ContentDocument Document, ContentNode Tree);

    private readonly WithUnchanged _unchanged = new();

    /// <summary>Whether the engine was told that nothing laid is to be set down again as it was, since it last laid anything.</summary>
    private bool _unkept;

    /// <summary>
    /// The places the links of the content being shown can name, as its stages found them (<see cref="LinkTargets"/>) — and, for a
    /// long document laid a stretch at a time, as working all of it over found them past what is laid.
    /// </summary>
    private readonly LinkTargets _targets = new();

    /// <summary>The parse each language keeps for the content asked for, and the one for what is written inside it.</summary>
    private readonly Dictionary<ContentLanguage, Func<string, ContentParse>> _parses = [];
    private readonly Dictionary<ContentLanguage, Func<string, ContentParse>> _nestedParses = [];
    /// <summary>
    /// What the reader has opened and chosen in each diagram, in the order the diagrams are written — kept for as long as the
    /// engine is, so it outlives every reading of the content an edit or a press causes.
    /// </summary>
    private readonly DiagramViewStates _opened = new();

    /// <summary>What the reader has opened in the content itself, where it is a diagram rather than a document holding some.</summary>
    private DiagramViewState? _top;

    /// <summary>For each style laid in and what the reader opened, by what was opened, that style with it — see <see cref="OpenedIn"/>.</summary>
    private readonly ConditionalWeakTable<Dictionary<string, bool>, StrongBox<(LayoutStyle From, LayoutStyle Opened)>> _openedStyles = new();

    /// <summary>
    /// Which of a markdown document's sections the reader has folded away, by the name each heading answers to — kept for as long
    /// as the engine is, so a section stays folded through every edit and every reading, and handed to the document's builder the
    /// way a diagram's openings are handed to its own.
    /// </summary>
    private readonly DiagramViewState _sections = new();

    private ContentInputs _inputs = inputs ?? ContentInputs.None;

    /// <summary>
    /// <paramref name="state"/>'s source laid out at <paramref name="room"/>, written in the language called
    /// <paramref name="named"/> — or in markdown, where nothing names one.
    /// </summary>
    public Laid Lay(string? named, EditState state, LayoutStyle style, double room, bool readOnly)
    {
        var language = Language(named);
        var whole = _shown = ReadAgain(language, named, state.Document);

        return LaidFrom(language, named, whole, state, style, room, readOnly, new KeptInContent(whole, state.Document), static tops => [.. Enumerable.Range(0, tops.Length - 1)]);
    }

    /// <summary>
    /// What <paramref name="window"/> has read, laid out: every unit standing on the page at a height (<see cref="Placed"/>), and the units
    /// <paramref name="wanted"/> picks out at those places worked over and laid on their own where what they were worked over and laid
    /// with no longer stands (<see cref="Stage"/>, <see cref="Composed"/>), what every unit read says brought into the outline, and what
    /// the reader folded away of it worked out — the whole put together as one layout.
    /// </summary>
    private Laid LaidFrom(ContentLanguage language, string? named, ContentWindow window, EditState state, LayoutStyle style, double room,
                          bool readOnly, Keeping holding, Func<double[], IReadOnlyCollection<int>> wanted)
    {
        Listing(window);

        // What the reader opened in each diagram, or in the content itself where it is a diagram.
        Views(window, language);
        if (_top is { } opened) style = this.OpenedIn(style, opened);

        var showing = Showing(named ?? string.Empty, style, !readOnly, state.Raw, 0) with
        {
            Unchanged = _unchanged,
            Targets = window.Targets,
            Syntax = window.LaidSyntax is { } syntax ? (syntax, 0) : null,
            WrapsLines = _wrapsLines,
        };

        return this.Laying(window, language, showing, room, readOnly, holding, LayoutStanding.Alone, wanted,
                           window.Units.Done ? 0 : window.Document.Length - window.Units.Reached);
    }

    /// <summary>
    /// <paramref name="style"/> with what the reader opened (<paramref name="opened"/>) — in the content itself, or in a diagram or body it
    /// holds — the same style while both stand, since what is kept by the style it was laid in (a unit's height, said once) is kept only
    /// while that is the same style.
    /// </summary>
    private LayoutStyle OpenedIn(LayoutStyle style, DiagramViewState opened)
    {
        var kept = _openedStyles.GetValue(opened.Expansion, static _ => new StrongBox<(LayoutStyle From, LayoutStyle Opened)>());
        if (ReferenceEquals(kept.Value.From, style)) return kept.Value.Opened;

        kept.Value = (style, style with { Expansion = opened.Expansion });

        return kept.Value.Opened;
    }



    /// <summary>Whether a reader reads the content as what is drawn rather than as its source (<see cref="IContentLanguage.ReadsAsDrawn"/>).</summary>
    internal bool ReadsAsDrawn => Language(_named).Editing.ReadsAsDrawn;

    /// <summary>
    /// <paramref name="source"/> read and worked over as the language called <paramref name="named"/> works it over — with
    /// nothing laid out, for whatever wants to know what content says rather than to draw it.
    /// </summary>
    public ContentReading Read(string? named, string source, bool writing = false)
    {
        var top = Parse(Language(named), named, ContentDocument.Of(source));

        return ContentReading.Of(Staged(top, Showing(named ?? string.Empty, WpfStyles.Dark, writing, null, 0)), 0, top.Document);
    }

    /// <summary>
    /// Forgets what was laid, so the next layout works over and lays everything again — for content shown against something else.
    /// </summary>
    public void Forget()
    {
        _unchanged.Forget();
        _unkept = true;
        _forgotten++;
    }

    /// <summary>
    /// What the host says about the content (<see cref="ContentInputs"/>). Nothing laid by what it said before is set down again
    /// as it was.
    /// </summary>
    public ContentInputs Inputs
    {
        get => _inputs;
        set
        {
            if (_inputs == value) return;

            if (_inputs.Data != value.Data) Unbind();

            _inputs = value;
            Forget();
        }
    }

    /// <summary>Forgets what the reader opened and chose in every diagram, so each is drawn as its source says.</summary>
    public void CloseDiagrams()
    {
        _opened.Clear();
        Forget();
    }

    /// <summary>What language content called <paramref name="named"/> is written in: markdown where nothing names one, and plain code where nothing reads what does.</summary>
    internal static ContentLanguage Language(string? named) =>
        named is null ? ContentLanguages.Markdown : ContentLanguages.For(named) ?? ContentLanguages.Code;

    /// <summary>
    /// The content asked for, parsed whole by its language — and nothing written in another language in it, which is read when a
    /// builder asks for it. A parse reads one string, so a document held as a rope is made one here: content laid whole costs what
    /// it holds to read anyway.
    /// </summary>
    private Parsed Parse(ContentLanguage language, string? named, ContentDocument document)
    {
        if (!_parses.TryGetValue(language, out var parse)) _parses[language] = parse = language.Parser();

        return new Parsed(language, named ?? string.Empty, document, parse(document.ToString()).Tree);
    }

    private ContentShowing Showing(string named, LayoutStyle style, bool writing, RawZone? shown, int at) =>
        new(named, style, writing, shown, at, _inputs) { Reads = ContentLanguages.Reads };

    /// <summary>
    /// The builder <paramref name="language"/> is laid out by, made for this showing — here, and nowhere else. A language only names
    /// its builder, and every builder is made from the same six things: what was read, what is being written, what it is drawn in,
    /// whether it is only looked at, what it is laid out in, and what hosts it.
    /// </summary>
    private ContentBuilder Builder(ContentLanguage language, ContentReading reading, ContentShowing showing, LayoutStanding standing,
                                   Keeping holding, Func<ContentPart, LayoutFold>? folds = null, LayoutReach? reach = null)
    {
        var readOnly = !(language.Writable && showing.Writing);

        return Makers.GetOrAdd(language.Builder, Maker)(
            reading, EditState.For(reading.Document) with { Raw = showing.Shown }, showing.Style, readOnly, _medium,
            Hosting(showing, standing, writing: !readOnly, holding, folds) with { Reach = reach, WrapsLines = showing.WrapsLines });
    }

    /// <summary>
    /// What hosts content laid for <paramref name="showing"/>: where it stands, the engine laying out whatever it holds in another
    /// language — found in <paramref name="holding"/>, shown as written where this showing is, and written in where it is — and what the
    /// stages found its parts to name.
    /// </summary>
    private LayoutHost Hosting(ContentShowing showing, LayoutStanding standing, bool writing, Keeping holding,
                               Func<ContentPart, LayoutFold>? folds = null)
    {
        var host = new LayoutHost(standing, (holder, room, asked) => Nested(holder, room, asked, showing.Style, showing.Shown, writing, holding))
        {
            Pictures = Pictured,
            Links = Linked,
            ShownBefore = ShownBefore,
            BoxedAsWritten = static holder => ContentNested.Language(holder) is { } named && ContentLanguages.For(named) is { BoxedAsWritten: true },
        };

        return folds is null ? host : host with { Folds = folds };
    }

    /// <summary>The picture a part was found to name — an image, a diagram node's, a cloud's mask — as the medium draws it.</summary>
    private static LayoutPicture? Pictured(ContentPart part) =>
        (WithImages.Of(part) ?? WithDiagramPictures.Of(part) ?? part.Node.HeldAs(WithPictures.Picture) as ImageSource) is { } picture
            ? WpfMedium.OnThisThread.Picture(picture)
            : null;

    /// <summary>How the host wants a link to look, as its stage settled it, in the medium's inks.</summary>
    private static LayoutLinkLook? Linked(ContentPart part) =>
        WithLinks.Of(part) is { } look ? Looks.GetValue(look, Look) : null;

    /// <summary>Each look a host gave, said in the medium's terms once.</summary>
    private static readonly ConditionalWeakTable<LinkLook, LayoutLinkLook> Looks = new();

    private static LayoutLinkLook Look(LinkLook look) =>
        new(look.Ink is { } ink ? WpfMedium.OnThisThread.Ink(ink) : null, look.Underline, look.Before, look.After, look.Says)
        {
            MarkFont = look.MarkFont?.Source,
        };

    /// <summary>
    /// <paramref name="source"/> laid out as content nothing could read: shown as it is written, with <paramref name="why"/>
    /// said beneath it. The stopgap the pipeline's promise rests on.
    ///
    /// <para>
    /// Made through <see cref="Maker"/> like any other builder, and deliberately not through <see cref="ContentLanguages"/>:
    /// a language whose builder is not one is among the things that land here, so this must not need the table to be right.
    /// </para>
    /// </summary>
    /// <param name="at">Where <paramref name="source"/> begins in the document holding it, so what is said of it is said where it is.</param>
    private Laid Unreadable(string source, int at, string why, LayoutStyle style, bool readOnly, double room) =>
        Makers.GetOrAdd(typeof(UnreadBuilder), Maker)(
            ContentReading.Of(ContentNode.Shown(source, why), at, ContentDocument.Of(source)), EditState.For(source), style, readOnly, _medium,
            new LayoutHost(LayoutStanding.Alone, static (_, _, _) => null)).Lay(room);

    /// <summary>Makes a builder from the six things every builder is made from.</summary>
    private delegate ContentBuilder Make(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly,
                                                 ILayoutMedium medium, LayoutHost host);

    /// <summary>What makes each builder a language names, found once.</summary>
    private static readonly ConcurrentDictionary<Type, Make> Makers = new();

    /// <summary>What makes a <paramref name="builder"/>: its one constructor, taking the six things every builder is made from.</summary>
    private static Make Maker(Type builder)
    {
        Type[] shape = [typeof(ContentReading), typeof(EditState), typeof(LayoutStyle), typeof(bool), typeof(ILayoutMedium), typeof(LayoutHost)];

        if (builder.IsAbstract || !typeof(ContentBuilder).IsAssignableFrom(builder)
            || builder.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, shape) is not { } made)
            throw new InvalidOperationException(
                $"{builder.FullName} is not a builder made from ({string.Join(", ", shape.Select(type => type.Name))}).");

        var given = shape.Select(type => Expression.Parameter(type)).ToArray();
        return Expression.Lambda<Make>(Expression.New(made, given), given).Compile();
    }

    /// <summary>
    /// A read worked over by its language's stages. What is written in another language in it is its characters, which no stage
    /// reads: that language's stages work it over when a builder asks for it.
    /// </summary>
    private ContentNode Staged(Parsed read, ContentShowing showing)
    {
        var tree = Bound(read.Language, read.Tree);
        var stages = read.Language.Stages(tree, showing).OfType<IAstStage>()
                         .Select(stage => stage is ISlowStage slow ? Staging(read.Language, read.Document, slow) : stage)
                         .ToList();

        return stages.Count == 0 ? tree : new AstPipeline(stages).Run(tree);
    }

    /// <summary>
    /// What the reader has opened in the diagram <paramref name="holder"/> holds — or in the content itself, where
    /// <paramref name="holder"/> is the whole of it and it is a diagram — or null where nothing says.
    /// </summary>
    public DiagramViewState? Opened(ContentPart holder) =>
        holder.Parent is null ? _top
        : holder.Part(Roles.Body) is { } body ? Diagram(body.Start) : null;

    /// <summary>
    /// What <paramref name="holder"/> holds in another language, read by that language, worked over and laid out at
    /// <paramref name="room"/> — or null where nothing reads the language it names. The builder asking is handed the layout; the
    /// tree it was drawn from is that language's own, and its root says so.
    ///
    /// <para>
    /// The body's reading is the one kept where its holder was read (<paramref name="holding"/>), read the first time it is asked for;
    /// what it holds in turn is kept in it, so a builder laying the body asks of it as this one asked of the content.
    /// </para>
    /// <para>
    /// A failure here is the block's, not the document's. Reading or working over nested content happens while the builder
    /// asking is part-way through laying its own, so letting a throw out would blame a whole document for one block of it.
    /// What could not be read comes back as that block shown as it is written, with why — which is what the builder asking
    /// already does with a block it cannot draw. Null keeps its own meaning: nothing is nested here.
    /// </para>
    /// </summary>
    private LayoutInset? Nested(ContentPart holder, double room, LayoutStanding standing, LayoutStyle style, RawZone? shown, bool writing,
                                Keeping holding)
    {
        if (ContentNested.Language(holder) is not { } named || holder.Part(Roles.Body) is not { } body
            || ContentLanguages.For(named) is not { } language) return null;

        try
        {
            // Found where its holder was read; content a binding supplied was read nowhere, and is read here, for this laying out.
            var at = holding.At(holder.Start);
            var reading = (body.Supplied ? null : Body(holding, at, named, language, body.Length)) ?? Unheld(named, language, body);

            // Where its own characters stand in the document: past the marks of every container they are written in, at every level up.
            var placed = body.Supplied ? ContentSpace.Of(ContentDocument.Of(body.Node.Print()), 0, body.Node)
                                       : ContentSpace.Of(holding.Document, body.Start, body.Node);
            Keeping within = holding.Held(at) == reading ? new KeptInBody(holding, at, body.Start, placed) : new KeptNowhere(holding.Document);

            // Its own openings, or those of the diagram holding it.
            if (Opened(holder) is { } view) style = this.OpenedIn(style, view);

            // Read a stretch at a time, it is laid so too: its units put together, each the tree it was laid as, put down where it stands.
            if (!body.Supplied && reading.Reader.SoftMaxLength != int.MaxValue)
                return new LayoutInset(this.BodyLaid(reading, language, named, holder, body, placed, within, holding, style, shown, writing, standing, room));

            // What is shown as written is where it is among the characters the language was handed, which a container's marks
            // between its lines are not among.
            var showing = Showing(named, style, writing,
                                  shown is { } zone && ContentNested.Holds(body, zone.Start, zone.End)
                                      ? zone with { Start = body.Start + placed.OwnAt(zone.Start - body.Start), End = body.Start + placed.OwnAt(zone.End - body.Start) }
                                      : null,
                                  body.Start) with
                                  {
                                      Syntax = reading.Units.Syntax is { } syntax ? (syntax, 0) : null,
                                      WrapsLines = language.WrapsLines,
                                  };

            // What it read is given those marks back where they stand, so every part names its characters in the document.
            var read = new Parsed(language, named, Stretched(reading, holding.Document, body), reading.Tree);
            var root = ContentNested.Spliced(Staged(read, showing), placed);
            _readings.AddOrUpdate(root, within.Address);

            return new LayoutInset(Builder(language, ContentReading.Of(root, body.Start, read.Document), showing, standing, within).Lay(room));
        }
        catch (Exception error)
        {
            var written = body.Node.Print()[..ContentNested.Own(body).Length];

            return new LayoutInset(Unreadable(written, body.Start, Str.Format("Text.Content.CouldNotRead", error.Message),
                                               style, !(language.Writable && writing), room));
        }
    }
}
