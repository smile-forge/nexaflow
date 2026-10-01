using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using System.Linq.Expressions;
using System.Reflection;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

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
    private sealed record Parsed(ContentLanguage Language, string Named, string Text, ContentNode Tree);

    private readonly Stages.WithUnchanged _unchanged = new();

    private Nesting? _nesting;

    /// <summary>The parse each language keeps for the content asked for, and the one for what is written inside it.</summary>
    private readonly Dictionary<ContentLanguage, Func<string, ContentParse>> _parses = [];
    private readonly Dictionary<ContentLanguage, Func<string, ContentParse>> _nestedParses = [];

    /// <summary>What every piece in another language read to last time and this time, by what it was written as.</summary>
    private Dictionary<(ContentLanguage, string, string), Parsed> _before = [];
    private Dictionary<(ContentLanguage, string, string), Parsed> _now = [];

    /// <summary>What the reader has opened in each diagram, by where its body starts.</summary>
    private readonly Dictionary<int, DiagramViewState> _views = [];

    /// <summary>
    /// What the reader has opened and chosen in each diagram, in the order the diagrams are written — kept for as long as the
    /// engine is, so it outlives every reading of the content an edit or a press causes.
    /// </summary>
    private readonly DiagramViewStates _opened = new();

    /// <summary>What the reader has opened in the content itself, where it is a diagram rather than a document holding some.</summary>
    private DiagramViewState? _top;

    private ContentInputs _inputs = inputs ?? ContentInputs.None;

    /// <summary>
    /// What the content read to when it was last laid out — its worked-over tree, the one the layout was drawn from — or null
    /// before it has been.
    /// </summary>
    public ContentReading? Reading { get; private set; }

    /// <summary>
    /// <paramref name="state"/>'s source laid out at <paramref name="room"/>, written in the language called
    /// <paramref name="named"/> — or in markdown, where nothing names one.
    /// </summary>
    public Laid Lay(string? named, EditState state, StyleFormat style, double room, bool readOnly)
    {
        var language = Language(named);

        Turn();
        var top = Parse(language, named, state.Source);

        Views(top);
        if (_top is { } opened) style = style with { Expansion = opened };

        var showing = Showing(named ?? string.Empty, style, !readOnly, state.Raw, 0) with { Unchanged = _unchanged };
        var staged = Staged(top, showing);

        Reading = ContentReading.Of(staged, 0, state.Source);

        return Builder(language, Reading, showing).Lay(room);
    }

    /// <summary>
    /// <paramref name="source"/> read and worked over as the language called <paramref name="named"/> works it over — with
    /// nothing laid out, for whatever wants to know what content says rather than to draw it.
    /// </summary>
    public ContentReading Read(string? named, string source, bool writing = false)
    {
        Turn();
        var top = Parse(Language(named), named, source);

        return ContentReading.Of(Staged(top, Showing(named ?? string.Empty, StyleFormat.Dark, writing, null, 0)), 0, source);
    }

    /// <summary>Starts another reading: what the last one read in other languages is kept for it, and nothing older.</summary>
    private void Turn()
    {
        _before = _now;
        _now = [];
    }

    /// <summary>
    /// Forgets what was read and laid, so the next layout reads everything again — for content shown against something else, or
    /// read again because a slower reading of it has landed.
    /// </summary>
    public void Forget()
    {
        _unchanged.Forget();
        _before = [];
        _now = [];
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

    /// <summary>The content asked for, parsed by its language — and nothing written in another language in it, which is read when a builder asks for it.</summary>
    private Parsed Parse(ContentLanguage language, string? named, string source)
    {
        if (!_parses.TryGetValue(language, out var parse)) _parses[language] = parse = language.Parser();

        return new Parsed(language, named ?? string.Empty, source, Read(language, parse, source).Tree);
    }

    /// <summary>A piece written in another language, read by that language — or, written as it was last time, what it read to then.</summary>
    private Parsed Nested(ContentLanguage language, string named, string text)
    {
        var key = (language, named, text);

        if (_now.TryGetValue(key, out var kept) || _before.TryGetValue(key, out kept))
        {
            _now[key] = kept;
            return kept;
        }

        if (!_nestedParses.TryGetValue(language, out var parse)) _nestedParses[language] = parse = language.Parser();

        var read = new Parsed(language, named, text, Read(language, parse, text).Tree);

        _now[key] = read;
        return read;
    }

    private ContentShowing Showing(string named, StyleFormat style, bool writing, RawZone? shown, int at) =>
        new(named, style, writing, shown, at, _inputs) { Reads = ContentLanguages.Reads };

    /// <summary>
    /// The builder <paramref name="language"/> is laid out by, made for this showing — here, and nowhere else. A language only names
    /// its builder, and every builder is made from the same five things: what was read, what is being written, what it is drawn in,
    /// whether it is only looked at, and what lays out what it holds in another language.
    /// </summary>
    private ContentBuilder Builder(ContentLanguage language, ContentReading reading, ContentShowing showing) =>
        Makers.GetOrAdd(language.Builder, Maker)(
            reading, EditState.For(reading.Source) with { Raw = showing.Shown }, showing.Style,
            !(language.Writable && showing.Writing), _nesting ??= new Nesting(this));

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
    private Laid Unreadable(string source, int at, string why, StyleFormat style, bool readOnly, double room) =>
        Makers.GetOrAdd(typeof(UnreadBuilder), Maker)(
            ContentReading.Of(ContentNode.Shown(source, why), at, source), EditState.For(source), style, readOnly,
            _nesting ??= new Nesting(this)).Lay(room);

    /// <summary>Makes a builder from the five things every builder is made from.</summary>
    private delegate ContentBuilder Make(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting);

    /// <summary>What makes each builder a language names, found once.</summary>
    private static readonly ConcurrentDictionary<Type, Make> Makers = new();

    /// <summary>What makes a <paramref name="builder"/>: its one constructor, taking the five things every builder is made from.</summary>
    private static Make Maker(Type builder)
    {
        Type[] shape = [typeof(ContentReading), typeof(EditState), typeof(StyleFormat), typeof(bool), typeof(Nesting)];

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
                         .Select(stage => stage is ISlowStage slow ? Staging(read, slow) : stage)
                         .ToList();

        return stages.Count == 0 ? tree : new AstPipeline(stages).Run(tree);
    }

    /// <summary>
    /// What the reader has opened in each diagram, handed out afresh in the order the diagrams are written: the one thing a
    /// reading of the same document again keeps, where the source is exactly what may have changed. Content that is itself a
    /// diagram, rather than a document holding some, is the first.
    /// </summary>
    private void Views(Parsed top)
    {
        _views.Clear();
        _opened.Rewind();

        _top = top.Language == ContentLanguages.Markdown ? null : _opened.Next();
        if (_top is not null) return;

        Held(top.Tree, 0);

        void Held(ContentNode node, int at)
        {
            if (ContentNested.Language(node) is { } language)
            {
                if (!ContentLanguages.Reads(language)) return;

                foreach (var child in node.Children)
                {
                    if (child.Kind == Kinds.Nested) { _views[at] = _opened.Next(); return; }
                    at += child.Width;
                }

                return;
            }

            foreach (var child in node.Children)
            {
                Held(child, at);
                at += child.Width;
            }
        }
    }

    /// <summary>
    /// What the reader has opened in the diagram <paramref name="holder"/> holds — or in the content itself, where
    /// <paramref name="holder"/> is the whole of it and it is a diagram — or null where nothing says.
    /// </summary>
    public DiagramViewState? Opened(ContentPart holder) =>
        holder.Parent is null ? _top
        : holder.Part(Roles.Body) is { } body && _views.TryGetValue(body.Start, out var view) ? view : null;

    /// <summary>
    /// What <paramref name="holder"/> holds in another language, read by that language, worked over and laid out at
    /// <paramref name="room"/> — or null where nothing reads the language it names. The builder asking is handed the layout; the
    /// tree it was drawn from is that language's own, and its root says so.
    ///
    /// <para>
    /// A failure here is the block's, not the document's. Reading or working over nested content happens while the builder
    /// asking is part-way through laying its own, so letting a throw out would blame a whole document for one block of it.
    /// What could not be read comes back as that block shown as it is written, with why — which is what the builder asking
    /// already does with a block it cannot draw. Null keeps its own meaning: nothing is nested here.
    /// </para>
    /// </summary>
    internal ContentInset? Nested(ContentPart holder, double room, StyleFormat style, RawZone? shown, bool writing)
    {
        if (ContentNested.Language(holder) is not { } named || holder.Part(Roles.Body) is not { } body
            || ContentLanguages.For(named) is not { } language) return null;

        var text = ContentNested.Own(body.Node);

        try
        {
            var read = Nested(language, named, text);

            if (Opened(holder) is { } view) style = style with { Expansion = view };

            var showing = Showing(named, style, writing,
                                  shown is { } zone && ContentNested.Holds(body, zone.Start, zone.End) ? zone : null,
                                  body.Start);

            var reading = ContentReading.Of(Staged(read, showing), body.Start, text);
            return new ContentInset(Builder(language, reading, showing).Lay(room));
        }
        catch (Exception error)
        {
            return new ContentInset(Unreadable(text, body.Start, $"This could not be read: {error.Message}",
                                               style, !(language.Writable && writing), room));
        }
    }
}

/// <summary>
/// What a builder asks to lay out content written in another language inside the one it is laying out — the engine, and
/// only as far as that goes.
/// </summary>
public sealed class Nesting
{
    private readonly ContentEngine _engine;

    internal Nesting(ContentEngine engine) => _engine = engine;

    /// <summary>
    /// What <paramref name="holder"/> holds in another language, laid out at <paramref name="room"/> in
    /// <paramref name="style"/> — or null where nothing read it.
    /// </summary>
    /// <param name="shown">The stretch the content holding it is showing as typed, which is the other language's to show where it falls inside it.</param>
    /// <param name="writing">Whether somebody is writing in it.</param>
    internal ContentInset? Lay(ContentPart holder, double room, StyleFormat style, RawZone? shown, bool writing) =>
        _engine.Nested(holder, room, style, shown, writing);
}
