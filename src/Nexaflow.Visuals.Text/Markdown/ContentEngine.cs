using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
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
/// <param name="options">What the host says about the content it shows — pictures, links, what diagrams are bound against.</param>
public sealed class ContentEngine(DiagramRenderOptions? options = null)
{
    /// <summary>What a piece of content read to: its language's tree, and what each piece in it written in another language read to.</summary>
    /// <param name="Inside">What each piece in another language read to, by where its body starts in <paramref name="Tree"/>.</param>
    private sealed record Parsed(ContentLanguage Language, string Named, string Text, ContentNode Tree, IReadOnlyDictionary<int, Parsed> Inside);

    private static readonly IReadOnlyDictionary<int, Parsed> Nothing = new Dictionary<int, Parsed>();

    private readonly Stages.WithUnchanged _unchanged = new();

    private Nesting? _nesting;

    /// <summary>The parse each language keeps for the content asked for, and the one for what is written inside it.</summary>
    private readonly Dictionary<ContentLanguage, Func<string, ContentNode>> _parses = [];
    private readonly Dictionary<ContentLanguage, Func<string, ContentNode>> _nestedParses = [];

    /// <summary>What every piece in another language read to last time and this time, by what it was written as.</summary>
    private Dictionary<(ContentLanguage, string, string), Parsed> _before = [];
    private Dictionary<(ContentLanguage, string, string), Parsed> _now = [];

    /// <summary>What each tree put in a body was read from — what a builder meeting it has laid out.</summary>
    private readonly Dictionary<ContentNode, Parsed> _reads = new(ReferenceEqualityComparer.Instance);

    /// <summary>What the reader has opened in each diagram, by where its body starts.</summary>
    private readonly Dictionary<int, DiagramViewState> _views = [];

    /// <summary>
    /// <paramref name="state"/>'s source laid out at <paramref name="room"/>, written in the language called
    /// <paramref name="named"/> — or in markdown, where nothing names one.
    /// </summary>
    public Laid Lay(string? named, EditState state, StyleFormat style, double room, bool readOnly)
    {
        var language = Language(named);
        var top = Parse(language, named, state.Source);

        var showing = Showing(named ?? string.Empty, style, !readOnly, state.Raw, 0) with { Unchanged = _unchanged };
        var staged = Staged(top, showing);

        Views(top.Tree);

        return language.Builder(ContentReading.Of(staged, 0, state.Source), showing).Lay(room);
    }

    /// <summary>
    /// <paramref name="source"/> read and worked over as the language called <paramref name="named"/> works it over — with
    /// nothing laid out, for whatever wants to know what content says rather than to draw it.
    /// </summary>
    public ContentReading Read(string? named, string source, bool writing = false)
    {
        var top = Parse(Language(named), named, source);
        return ContentReading.Of(Staged(top, Showing(named ?? string.Empty, StyleFormat.Dark, writing, null, 0)), 0, source);
    }

    /// <summary>
    /// Says that something the source does not say has changed — which nodes of a diagram are open, what a binding is bound
    /// against — so nothing is set down as it was.
    /// </summary>
    public void Forget() => _unchanged.Forget();

    /// <summary>What language content called <paramref name="named"/> is written in: markdown where nothing names one, and plain code where nothing reads what does.</summary>
    private static ContentLanguage Language(string? named) =>
        named is null ? ContentLanguages.Markdown : ContentLanguages.For(named) ?? ContentLanguages.Code;

    /// <summary>The content asked for, parsed — and every piece in it written in another language, parsed by that language.</summary>
    private Parsed Parse(ContentLanguage language, string? named, string source)
    {
        _now = [];
        _reads.Clear();

        if (!_parses.TryGetValue(language, out var parse)) _parses[language] = parse = language.Parser();

        var tree = parse(source);
        var read = new Parsed(language, named ?? string.Empty, source, tree, Inside(tree));

        _before = _now;
        return read;
    }

    /// <summary>What every piece in <paramref name="tree"/> written in another language read to, by where its body starts.</summary>
    private IReadOnlyDictionary<int, Parsed> Inside(ContentNode tree)
    {
        Dictionary<int, Parsed>? inside = null;
        Walk(tree, 0);
        return inside ?? Nothing;

        void Walk(ContentNode node, int at)
        {
            if (node.IsDerived || node.IsLeaf) return;

            if (ContentNested.Language(node) is { } name)
            {
                if (ContentLanguages.For(name) is not { } language) return;

                for (var index = 0; index < node.Children.Count; index++)
                {
                    var child = node.Children[index];
                    if (child.Role == Roles.Body && !child.IsDerived)
                    {
                        (inside ??= [])[at] = Nested(language, name, ContentNested.Own(child));
                        return;
                    }

                    at += child.Width;
                }

                return;
            }

            for (var index = 0; index < node.Children.Count; index++)
            {
                Walk(node.Children[index], at);
                at += node.Children[index].Width;
            }
        }
    }

    /// <summary>A piece written in another language, read by that language — or, written as it was last time, what it read to then.</summary>
    private Parsed Nested(ContentLanguage language, string named, string text)
    {
        var key = (language, named, text);

        if (_before.TryGetValue(key, out var kept) || _now.TryGetValue(key, out kept))
        {
            _now[key] = kept;
            Known(kept);
            return kept;
        }

        if (!_nestedParses.TryGetValue(language, out var parse)) _nestedParses[language] = parse = language.Parser();

        var tree = parse(text);
        var read = new Parsed(language, named, text, tree, Inside(tree));

        _now[key] = read;
        _reads[tree] = read;
        return read;
    }

    /// <summary>Says what a read kept from last time was read from, and so everything it holds.</summary>
    private void Known(Parsed read)
    {
        _reads[read.Tree] = read;
        foreach (var inner in read.Inside.Values) Known(inner);
    }

    private ContentShowing Showing(string named, StyleFormat style, bool writing, RawZone? shown, int at) =>
        new(named, style, writing, shown, at, options) { Nesting = _nesting ??= new Nesting(this), Reads = ContentLanguages.Reads };

    /// <summary>
    /// A read worked over by its language's stages, with what each piece in another language read to put back in its body
    /// once they are done — so no stage ever sees a node of a language other than its own.
    /// </summary>
    private ContentNode Staged(Parsed read, ContentShowing showing)
    {
        var stages = read.Language.Stages(read.Tree, showing).OfType<IAstStage>().ToList();
        var staged = stages.Count == 0 ? read.Tree : new AstPipeline(stages).Run(read.Tree);

        return read.Inside.Count == 0 ? staged : Spliced(staged, read.Inside, 0);
    }

    /// <summary><paramref name="node"/>, starting at <paramref name="at"/>, with every piece in another language holding what it read to.</summary>
    private static ContentNode Spliced(ContentNode node, IReadOnlyDictionary<int, Parsed> inside, int at)
    {
        if (node.IsDerived || node.IsLeaf) return node;

        if (ContentNested.Language(node) is not null)
        {
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                if (child.Role == Roles.Body && !child.IsDerived)
                    return inside.TryGetValue(at, out var read) ? ContentNested.Reading(node, read.Tree) : node;

                at += child.Width;
            }

            return node;
        }

        ContentNode[]? spliced = null;

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var now = Spliced(child, inside, at);
            at += child.Width;

            if (ReferenceEquals(now, child)) continue;

            spliced ??= [.. node.Children];
            spliced[index] = now;
        }

        return spliced is null ? node : node.With(spliced);
    }

    /// <summary>
    /// What the reader has opened in each diagram, handed out afresh in the order the diagrams are written: the one thing a
    /// reading of the same document again keeps, where the source is exactly what may have changed.
    /// </summary>
    private void Views(ContentNode tree)
    {
        _views.Clear();
        if (options?.Views is not { } views) return;

        views.Rewind();

        foreach (var (node, start) in tree.Placed())
            if (node.Kind is MarkdownKinds.Fence or Kinds.Nested && ContentLanguages.Reads(ContentNested.Language(node))
                && Body(node, start) is { } body)
                _views[body] = views.Next();
    }

    /// <summary>Where the body of a node holding another language starts, or null where it has none.</summary>
    private static int? Body(ContentNode holder, int at)
    {
        for (var index = 0; index < holder.Children.Count; index++)
        {
            if (holder.Children[index] is { Role: Roles.Body, IsDerived: false }) return at;
            at += holder.Children[index].Width;
        }

        return null;
    }

    /// <summary>What the reader has opened in the diagram <paramref name="holder"/> holds, or null where nothing says.</summary>
    public DiagramViewState? Opened(ContentPart holder) =>
        holder.Part(Roles.Body) is { } body && _views.TryGetValue(body.Start, out var view) ? view : null;

    /// <summary>
    /// What <paramref name="holder"/> holds in another language, worked over and laid out at <paramref name="room"/> — or null
    /// where nothing read it.
    /// </summary>
    internal ContentInset? Nested(ContentPart holder, double room, StyleFormat style, RawZone? shown, bool writing)
    {
        if (ContentNested.Read(holder) is not { } inner || !_reads.TryGetValue(inner.Node, out var read)) return null;

        var body = holder.Part(Roles.Body)!;
        if (Opened(holder) is { } view) style = style with { Expansion = view };

        var showing = Showing(read.Named, style, writing,
                              shown is { } zone && ContentNested.Holds(body, zone.Start, zone.End) ? zone : null,
                              inner.Start);

        var reading = ContentReading.Of(Staged(read, showing), inner.Start, read.Text);
        return new ContentInset(read.Language.Builder(reading, showing).Lay(room));
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
