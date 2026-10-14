using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Reads the links a markdown document writes by name — <c>[text][name]</c>, <c>[name][]</c>, <c>[name]</c>, and a picture
/// written the same way — and the abbreviations it uses, by what the document defines (<see cref="LinkTargets"/>, told by
/// <see cref="WithTargets"/> before this runs).
///
/// <para>
/// A block's words are read on their own, and a definition is written somewhere else — so the reading of a block cannot tell
/// a link by name from a few words in brackets. It reads them as CommonMark does where nothing is defined, and keeps on the
/// words the list it matched them from, with the names its brackets wait on (<see cref="MarkdownDelimiters"/>). Where the
/// document defines one of those names, the words are matched again here from that same list, with the names known — the
/// same matching, so a link by name is read exactly as CommonMark reads it, emphasis round it and brackets before it and all.
/// Words whose brackets wait on nothing the document defines are left as they were read.
/// </para>
/// <para>
/// An abbreviation is a whole word, or words, the document says it stands for something: starting where a word does, and
/// ending where the words do, a space does or punctuation does. Each is made a piece of its own holding what it stands for.
/// </para>
/// </summary>
/// <param name="targets">What the document defines.</param>
public sealed class WithReferences(LinkTargets targets) : IAstStage
{
    /// <inheritdoc/>
    public string Name => "markdown:references";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree)
    {
        var linked = targets.Every(MarkdownKinds.Reference).Any() ? this.Linked(tree) : tree;

        if (this.Abbreviations() is not { } abbreviations) return linked;

        return Abbreviated(linked, abbreviations) is [var only] ? only : linked;
    }

    // ── Links ────────────────────────────────────────────────────────────────

    /// <summary>The walk, made once for the whole tree rather than once for every piece it goes into.</summary>
    private Func<ContentNode, ContentNode>? _linked;

    /// <summary>A piece with every run of words in it matched again where a link in it waits on a name the document defines.</summary>
    private ContentNode Linked(ContentNode node)
    {
        if (node.IsLeaf || node.IsDerived || Elsewhere(node)) return node;

        node = Each(node, _linked ??= this.Linked);

        return node.Kind == Kinds.Words ? this.Matched(node) : node;
    }

    /// <summary>A piece with <paramref name="of"/> applied to each of its parts — itself, where none of them changed.</summary>
    private static ContentNode Each(ContentNode node, Func<ContentNode, ContentNode> of)
    {
        ContentNode[]? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = of(child);

            if (parts is null && !ReferenceEquals(now, child)) parts = [.. node.Children];
            if (parts is not null) parts[at] = now;
        }

        return parts is null ? node : node.With(parts);
    }

    /// <summary>
    /// Whether a piece is written in something whose characters hold no link by name: another language a fence or a formula
    /// holds, code, raw HTML, front matter.
    /// </summary>
    private static bool Elsewhere(ContentNode node) =>
        node is BlockNode { Language: not MarkdownParser.Language }
        || node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter;

    /// <summary>
    /// A run of words with every stretch whose links wait on a name the document defines matched again — each list it was read
    /// from, last first, so the stretches before it stay where they stand — and the list let go once it is answered.
    /// </summary>
    private ContentNode Matched(ContentNode words)
    {
        List<ContentNode>? parts = null;
        List<ContentNode>? answered = null;

        for (var at = words.Children.Count - 1; at >= 0; at--)
        {
            if (words.Children[at] is not { IsDerived: true, Held: MarkdownDelimiters list } held || !list.Answered(this.Defined)) continue;

            parts ??= [.. words.Children];
            parts.RemoveRange(list.First, list.Count);
            parts.InsertRange(list.First, list.Again(words.Offset ?? 0, this.Defined));

            (answered ??= []).Add(held);
        }

        if (parts is null) return words;

        parts.RemoveAll(part => answered!.Exists(held => ReferenceEquals(held, part)));
        return words.With(parts);
    }

    /// <summary>The definition a link by <paramref name="name"/> — folded as labels are compared — goes where it says, or null where nothing defines it.</summary>
    private LinkTarget? Defined(string name) => targets.Named(MarkdownKinds.Reference, name);

    // ── Abbreviations ────────────────────────────────────────────────────────

    /// <summary>Every abbreviation the document defines, by its first character, longest first — the first definition of each.</summary>
    private Dictionary<char, List<(string Name, string Meaning)>>? Abbreviations()
    {
        Dictionary<char, List<(string Name, string Meaning)>>? by = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in targets.Every(MarkdownKinds.Abbreviation))
        {
            if (target.Name.Length == 0 || !seen.Add(target.Name)) continue;

            by ??= [];
            if (!by.TryGetValue(target.Name[0], out var alike)) by[target.Name[0]] = alike = [];
            alike.Add((target.Name, target.Meaning));
        }

        if (by is null) return null;

        foreach (var alike in by.Values) alike.Sort((one, other) => other.Name.Length.CompareTo(one.Name.Length));

        return by;
    }

    /// <summary>A piece with every abbreviation in its words made a piece of its own: the piece itself where none is, or what stands in its place.</summary>
    private static IReadOnlyList<ContentNode> Abbreviated(ContentNode node, Dictionary<char, List<(string Name, string Meaning)>> abbreviations)
    {
        if (node.IsDerived) return [node];

        if (node.IsLeaf)
            return node is { Kind: MarkdownKinds.Word, Role: Roles.Element, Offset: not null } && Spelled(node, abbreviations) is { } said ? said : [node];

        // Nothing written in code, raw HTML or another language is an abbreviation.
        if (node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.Abbreviation || node is BlockNode { Language: not MarkdownParser.Language })
            return [node];

        List<ContentNode>? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = Abbreviated(child, abbreviations);

            if (parts is null && (now.Count != 1 || !ReferenceEquals(now[0], child))) parts = [.. node.Children.Take(at)];
            parts?.AddRange(now);
        }

        return parts is null ? [node] : [node.With(parts)];
    }

    /// <summary>
    /// A word with every abbreviation in it a piece of its own, or null where it holds none. One starts where the word does or
    /// after space in it, and ends where the word does or space does — or punctuation, so long as nothing but punctuation
    /// stands between it and the space.
    /// </summary>
    private static List<ContentNode>? Spelled(ContentNode word, Dictionary<char, List<(string Name, string Meaning)>> abbreviations)
    {
        var text = word.Text;
        var stands = word.Offset!.Value;
        List<ContentNode>? parts = null;
        var taken = 0;

        for (var at = 0; at < text.Length; at++)
        {
            var starts = at == 0 || char.IsWhiteSpace(text[at - 1]);
            if (!starts || !abbreviations.TryGetValue(text[at], out var alike)) continue;

            foreach (var (name, meaning) in alike)
            {
                var end = at + name.Length;
                if (end > text.Length || string.CompareOrdinal(text, at, name, 0, name.Length) != 0 || !Ends(text, end)) continue;

                parts ??= [];
                if (at > taken) parts.Add(Word(text[taken..at], stands + taken));

                parts.Add(ContentNode.Branch(MarkdownKinds.Abbreviation, [ContentNode.Leaf(MarkdownKinds.Word, name, Roles.Body, offset: stands + at)])
                                     .Holding(MarkdownKinds.Abbreviation, Roles.Tip, meaning));

                taken = end;
                at = end - 1;
                break;
            }
        }

        if (parts is null) return null;

        if (taken < text.Length) parts.Add(Word(text[taken..], stands + taken));

        return parts;
    }

    /// <summary>Whether an abbreviation ending at <paramref name="end"/> ends a word: nothing but punctuation up to the next space.</summary>
    private static bool Ends(string text, int end)
    {
        for (var at = end; at < text.Length && !char.IsWhiteSpace(text[at]); at++)
            if (!Punctuation(text[at])) return false;

        return true;
    }

    private static ContentNode Word(string text, int at) => ContentNode.Leaf(MarkdownKinds.Word, text, Roles.Element, offset: at);

    /// <summary>Whether a character is ASCII punctuation, which may end an abbreviation as a space may.</summary>
    private static bool Punctuation(char character) =>
        character is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');
}
