using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// The precise character offsets of the parts of one declaration — the whole thing, its name, its parameter
/// list, its body, and the doc comments attached above it.
/// <para>
/// <see cref="CodeOutline"/> answers "what is declared and on which lines", which is the right shape for a
/// reader. An <i>editor</i> needs more than lines: replacing a signature without touching a body, or renaming
/// a declaration without touching a string that happens to contain the same word, is a question about where
/// the sub-parts of a declaration begin and end. Every offset here comes from the parse tree's own field
/// names (<c>name</c>, <c>parameters</c>, <c>body</c>), which tree-sitter defines consistently across the
/// grammars, so this is one implementation rather than one per language — and nothing here counts a brace.
/// </para>
/// </summary>
/// <param name="NodeType">The grammar's node type, for diagnostics (<c>method_declaration</c>, <c>function_definition</c>, …).</param>
/// <param name="Start">Character offset the declaration begins at (its attributes included, where the grammar nests them).</param>
/// <param name="End">Character offset one past its last character.</param>
/// <param name="TriviaStart">Where the doc comments/decorators attached above it begin; equals <paramref name="Start"/> when there are none.</param>
public sealed record DeclarationAnchor(
    string NodeType,
    int Start,
    int End,
    int TriviaStart,
    int? NameStart,
    int? NameEnd,
    int? ParametersStart,
    int? ParametersEnd,
    int? BodyStart,
    int? BodyEnd)
{
    /// <summary>Whether the grammar gave this declaration a body, and so whether the signature and the body
    /// can be edited apart from one another.</summary>
    public bool HasBody => BodyStart is not null && BodyEnd is not null;

    /// <summary>Everything before the body — what "the signature" means for a replace that keeps the body.</summary>
    public (int Start, int End)? Header => BodyStart is { } b ? (Start, b) : null;
}

/// <summary>Finds <see cref="DeclarationAnchor"/>s. See the type's remarks for why this sits beside the
/// outline extractor rather than inside it.</summary>
public sealed class DeclarationAnchors
{
    /// <summary>
    /// The anchor for the declaration named <paramref name="expectedName"/> that ends on line
    /// <paramref name="endLine1"/> (1-based), or null when the file holds no such declaration.
    /// <para>
    /// Both the name and the end line must agree before an anchor is returned. That is deliberate: the caller
    /// is about to overwrite a range of a file on the strength of a graph record that may predate the file in
    /// hand, and a match on position alone would happily point at whatever now occupies those lines.
    /// </para>
    /// </summary>
    /// <param name="line1">1-based line the declaration was recorded as starting on — used to break ties
    /// between same-named declarations, and as the fallback when the end line no longer agrees.</param>
    public DeclarationAnchor? Find(string grammarId, string text, string expectedName, int line1, int endLine1)
    {
        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(text)) return null;

        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        if (highlighter is null) return null;

        try
        {
            return highlighter.WithParseTree(text, root =>
            {
                if (root.Type == "ERROR") return null;   // a file we cannot parse is a file we must not edit

                var candidates = new List<Node>();
                Collect(root, expectedName, candidates);
                if (candidates.Count == 0) return null;

                // End line first, then start line: the end is what bounds a replacement, so a candidate that
                // still ends where the record says is the one to trust.
                var chosen =
                    Pick(candidates, n => n.EndPosition.Row == endLine1 - 1 && n.StartPosition.Row == line1 - 1)
                 ?? Pick(candidates, n => n.EndPosition.Row == endLine1 - 1)
                 ?? Pick(candidates, n => n.StartPosition.Row == line1 - 1);

                return chosen is null ? null : Anchor(chosen, WithDecorators(chosen), text);
            });
        }
        catch { return null; }   // a malformed parse yields no anchor, never an exception mid-edit
    }

    /// <summary>
    /// Whether the grammar parses this text without an error node anywhere in it.
    /// <para>
    /// <see cref="CodeOutline.ParseFailed"/> is a coarser question — it asks whether the <i>root</i> came
    /// back as an error, which is the case that silently empties a file out of the graph. An editor needs
    /// the finer one: tree-sitter recovers from a missing brace locally, leaving a perfectly good outline
    /// with an <c>ERROR</c> node buried in it, and an edit that produces that must not be written. Compare
    /// before against after rather than demanding cleanliness outright, since some files do not parse
    /// cleanly to begin with and an edit is not required to fix them.
    /// </para>
    /// </summary>
    public bool ParsesCleanly(string grammarId, string text)
    {
        if (string.IsNullOrEmpty(grammarId)) return false;
        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        if (highlighter is null) return false;
        try { return highlighter.WithParseTree(text, root => !root.HasError); }
        catch { return false; }
    }

    /// <summary>Every named node whose <c>name</c> field is exactly <paramref name="expectedName"/>.</summary>
    private static void Collect(Node node, string expectedName, List<Node> into)
    {
        foreach (var child in node.NamedChildren)
        {
            if (NameOf(child)?.Text == expectedName) into.Add(child);
            Collect(child, expectedName, into);
        }
    }

    /// <summary>The outermost candidate satisfying <paramref name="test"/> — the declaration, not a nested
    /// re-declaration of the same name inside it.</summary>
    private static Node? Pick(List<Node> candidates, Func<Node, bool> test)
    {
        Node? best = null;
        foreach (var c in candidates)
            if (test(c) && (best is null || c.StartIndex < best.StartIndex)) best = c;
        return best;
    }

    /// <summary>
    /// Climbs to the wrapper a grammar puts decorators in (Python's <c>decorated_definition</c>, and the
    /// equivalents elsewhere) so <c>@cache</c> above a function is part of the function rather than an orphan
    /// left behind by a delete. The wrapper only widens the <i>span</i>: <c>name</c>, <c>parameters</c> and
    /// <c>body</c> stay on the declaration itself, which is where the grammar declares those fields.
    /// </summary>
    private static Node WithDecorators(Node node)
    {
        var current = node;
        while (current.Parent is { } parent
               && parent.EndIndex == current.EndIndex
               && (parent.Type.Contains("decorat", StringComparison.Ordinal)
                || parent.Type.Contains("annotat", StringComparison.Ordinal)))
            current = parent;
        return current;
    }

    /// <param name="declaration">The node carrying the <c>name</c>/<c>parameters</c>/<c>body</c> fields.</param>
    /// <param name="span">The same declaration including any decorator wrapper — what an edit must cover.</param>
    private static DeclarationAnchor Anchor(Node declaration, Node span, string text)
    {
        var name    = NameOf(declaration);
        var body    = declaration.GetChildForField("body");
        var @params = declaration.GetChildForField("parameters");

        return new DeclarationAnchor(
            declaration.Type,
            span.StartIndex,
            span.EndIndex,
            TriviaStart(span, text),
            name?.StartIndex, name?.EndIndex,
            @params?.StartIndex, @params?.EndIndex,
            body?.StartIndex, body?.EndIndex);
    }

    private static Node? NameOf(Node node) => node.GetChildForField("name");

    /// <summary>
    /// Where the doc comments and attributes written directly above this declaration start. Contiguity is
    /// what makes them "attached": a blank line between them and the declaration means they belong to the
    /// file, or to whatever came before, and deleting a method should not take them.
    /// <para>
    /// Attributes are here as well as comments because grammars disagree about where they live — C# nests
    /// <c>[Obsolete]</c> inside the declaration, Rust leaves <c>#[inline]</c> as a preceding sibling. Both
    /// end up covered, from opposite directions.
    /// </para>
    /// </summary>
    private static int TriviaStart(Node node, string text)
    {
        var start   = node.StartIndex;
        var current = node;

        while (current.PreviousSibling is { } previous)
        {
            if (!previous.IsNamed || !IsTrivia(previous.Type)) break;
            if (BlankLineBetween(text, previous.EndIndex, current.StartIndex)) break;

            start   = previous.StartIndex;
            current = previous;
        }
        return start;
    }

    private static bool IsTrivia(string nodeType) =>
        nodeType.Contains("comment",   StringComparison.Ordinal)
     || nodeType.Contains("attribute", StringComparison.Ordinal)
     || nodeType.Contains("annotat",   StringComparison.Ordinal)
     || nodeType.Contains("decorat",   StringComparison.Ordinal);

    /// <summary>Whether the gap between two nodes contains an empty line — i.e. more than one line break.</summary>
    private static bool BlankLineBetween(string text, int from, int to)
    {
        var breaks = 0;
        for (var i = from; i < to && i < text.Length; i++)
        {
            if (text[i] == '\n' && ++breaks > 1) return true;
            if (text[i] is not ('\n' or '\r' or ' ' or '\t')) return false;   // real content, not a gap
        }
        return false;
    }
}
