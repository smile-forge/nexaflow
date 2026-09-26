using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// What a piece's parts make together, gathered as the piece is read (<see cref="MarkdownBlocks"/>) — each piece asked by its
/// kind whether what it holds is one of them.
///
/// <para>
/// A reader cuts the text and says what each piece is, and stops there: what pieces mean together is in none of them.
/// A definition list is read back as a flat run — a term, its paragraphs, the next term — when what it is, is pairs. An
/// alert opens with a <c>[!</c>, a name and a <c>]</c>, which together are the marker saying which kind of alert it is. A
/// paragraph of nothing but a formula between double dollars is the display formula it is.
/// </para>
/// <para>
/// <strong>The tree says what the document is; the builder draws what the tree says.</strong> Without this a builder
/// would work out the pairs from where a block sits among its siblings, and the kind of an alert from its characters —
/// inferring structure, once per drawing, from the order things happen to be in. Only what is already side by side is
/// re-nested, so the characters coming out are the ones that went in.
/// </para>
/// </summary>
internal static class MarkdownGroups
{
    /// <summary>
    /// This piece with what its parts make together gathered, where they make anything. What was hung on the piece itself is
    /// not among the parts regrouped: it explains the piece, so sweeping it into a group made of the piece's contents would
    /// move an answer somewhere it is not true.
    /// </summary>
    public static ContentNode Grouped(ContentNode node)
    {
        if (Displayed(node) is { } maths) return maths;
        if (node.IsLeaf) return node;

        var facts = 0;
        for (var at = 0; at < node.Children.Count; at++)
            if (node.Children[at].Role == Roles.Derived) facts++;

        var contents = facts == 0 ? node.Children : [.. node.Children.Where(child => child.Role != Roles.Derived)];

        var grouped = node switch
        {
            { Kind: MarkdownKinds.Alert } => Marked(contents),
            { Role: Roles.Body } when Defines(contents) => Paired(contents),
            _ => null,
        };

        if (grouped is null) return node;

        return node.With(facts == 0 ? grouped : [.. grouped, .. node.Children.Where(child => child.Role == Roles.Derived)]);
    }

    // ── Display formulas ────────────────────────────────────────────────────

    /// <summary>
    /// A paragraph holding one formula written between double dollars and nothing else, as the display formula it is —
    /// which a paragraph written that way is, on one line or several, whatever the reader made of its dollars. The same
    /// characters in the same order: the dollars and the formula are the block's, and what stood round them is its trivia.
    /// </summary>
    private static ContentNode? Displayed(ContentNode paragraph)
    {
        if (paragraph.Kind != MarkdownKinds.Paragraph || paragraph.Part(Roles.Body) is not { IsLeaf: false } words) return null;

        ContentNode? formula = null;

        foreach (var child in words.Children)
        {
            if (child.Kind == MarkdownKinds.Formula && formula is null) formula = child;
            else if (child.Role != Roles.Trivia) return null;
        }

        if (formula?.Part(Roles.Open) is not { } opens || !opens.Text.StartsWith("$$", StringComparison.Ordinal)) return null;

        var parts = new List<ContentNode>();

        foreach (var child in paragraph.Children)
        {
            if (!ReferenceEquals(child, words)) { parts.Add(child); continue; }

            foreach (var written in words.Children)
                if (ReferenceEquals(written, formula)) parts.AddRange(formula.Children);
                else parts.Add(written);
        }

        return ContentNode.Branch(MarkdownKinds.Math, parts, paragraph.Role);
    }

    // ── Definitions ─────────────────────────────────────────────────────────

    /// <summary>Whether this run of blocks is a definition list's — which is to say, whether it holds a term.</summary>
    private static bool Defines(IReadOnlyList<ContentNode> children)
    {
        foreach (var child in children)
            if (child.Kind == MarkdownKinds.Term) return true;

        return false;
    }

    /// <summary>Each term with the blocks that explain it.</summary>
    private static List<ContentNode> Paired(IReadOnlyList<ContentNode> children)
    {
        var paired = new List<ContentNode>(children.Count);

        for (var at = 0; at < children.Count;)
        {
            paired.Add(children[at]);

            if (children[at++].Kind != MarkdownKinds.Term) continue;

            // Everything up to the next term is what this one means — trivia included, because the blank
            // line between two paragraphs of an explanation belongs to the explanation.
            var means = new List<ContentNode>();

            while (at < children.Count && children[at].Kind != MarkdownKinds.Term) means.Add(children[at++]);

            if (means.Count > 0) paired.Add(ContentNode.Branch(MarkdownKinds.Described, means));
        }

        return paired;
    }

    // ── Alerts ──────────────────────────────────────────────────────────────

    /// <summary>An alert's parts with its body's opening marks gathered into its marker, or null where they are not there.</summary>
    private static List<ContentNode>? Marked(IReadOnlyList<ContentNode> children)
    {
        var marked = new List<ContentNode>(children.Count);
        var moved = false;

        foreach (var child in children)
        {
            var seen = child.Role == Roles.Body && !child.IsLeaf ? Gathered(child) : child;
            moved |= !ReferenceEquals(seen, child);
            marked.Add(seen);
        }

        return moved ? marked : null;
    }

    /// <summary>A body whose first parts past its trivia are the marks and name an alert opens with, with those as one.</summary>
    private static ContentNode Gathered(ContentNode body)
    {
        var parts = body.Children;
        var at = 0;

        while (at < parts.Count && parts[at].Role == Roles.Trivia) at++;

        if (at + 2 >= parts.Count
            || parts[at].Role != Roles.Open
            || parts[at + 1].Role != Roles.Name
            || parts[at + 2].Role != Roles.Close) return body;

        var gathered = new List<ContentNode>(parts.Count - 2);

        for (var before = 0; before < at; before++) gathered.Add(parts[before]);
        gathered.Add(ContentNode.Branch(MarkdownKinds.Marker, [parts[at], parts[at + 1], parts[at + 2]]));
        for (var after = at + 3; after < parts.Count; after++) gathered.Add(parts[after]);

        return body.With(gathered);
    }
}
