using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// SmartyPants' quotes in one run of words, paired as Markdig pairs them: in the order they are written, whatever emphasis or
/// link stands between, each closing quote with the nearest opening quote of its kind before it — every quote opened after that
/// one left the character it is — and every quote nothing pairs the character it is.
///
/// <para>
/// A quote is read (<see cref="MarkdownTokenizer"/>) as a piece of its own that says whether it opens or closes. Whether it
/// pairs can hang on a quote written anywhere further along — past the end of a link, or of emphasis, made before that quote is
/// reached — so it is settled here, over the words once they are read. A quote that pairs stays
/// <see cref="MarkdownKinds.Typographic"/>; one that does not goes back into the words either side of it, which read as they
/// would had nothing looked at it.
/// </para>
/// </summary>
internal static class MarkdownQuotes
{
    /// <summary><paramref name="parts"/> — the pieces a run of words was read into — with every quote in them paired or put back.</summary>
    public static ContentNode[] Paired(ContentNode[] parts)
    {
        var quotes = new List<ContentNode>();
        Gathered(parts, quotes);

        var open = new List<ContentNode>();
        var paired = new HashSet<ContentNode>(ReferenceEqualityComparer.Instance);

        foreach (var quote in quotes)
        {
            if (quote.Role == MarkdownRoles.LeftQuote)
            {
                open.Add(quote);
                continue;
            }

            for (var at = open.Count - 1; at >= 0; at--)
            {
                if (!Partners(open[at].Text, quote.Text)) continue;

                paired.Add(open[at]);
                paired.Add(quote);
                open.RemoveRange(at, open.Count - at);
                break;
            }
        }

        if (paired.Count == quotes.Count) return parts;

        var now = new List<ContentNode>(parts.Length);
        foreach (var part in parts) Joined(now, Unpaired(part, paired));

        return [.. now];
    }

    /// <summary>Every quote <paramref name="parts"/> hold, in the order they are written.</summary>
    private static void Gathered(IReadOnlyList<ContentNode> parts, List<ContentNode> quotes)
    {
        foreach (var part in parts)
        {
            if (Quote(part)) quotes.Add(part);
            else if (!part.IsLeaf) Gathered(part.Children, quotes);
        }
    }

    private static bool Quote(ContentNode node) =>
        node is { IsLeaf: true, Kind: MarkdownKinds.Typographic, Role: MarkdownRoles.LeftQuote or MarkdownRoles.RightQuote };

    /// <summary>Whether a quote written <paramref name="opening"/> is closed by one written <paramref name="closing"/>.</summary>
    private static bool Partners(string opening, string closing) => opening == "<<" ? closing == ">>" : opening == closing;

    /// <summary><paramref name="node"/> with every quote in it that nothing paired the characters it is.</summary>
    private static ContentNode Unpaired(ContentNode node, HashSet<ContentNode> paired)
    {
        if (node.IsLeaf) return Quote(node) && !paired.Contains(node) ? ContentNode.Leaf(MarkdownKinds.Word, node.Text, Roles.Element, offset: node.Offset) : node;

        List<ContentNode>? children = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = Unpaired(child, paired);

            if (children is null)
            {
                if (ReferenceEquals(now, child)) continue;
                children = [.. node.Children.Take(at)];
            }

            Joined(children, now);
        }

        return children is null ? node : node.With(children);
    }

    /// <summary>
    /// <paramref name="part"/> added after the parts before it — as one word with the last of them where both are words written
    /// side by side, which is what a quote put back makes of the words either side of it.
    /// </summary>
    private static void Joined(List<ContentNode> parts, ContentNode part)
    {
        if (parts.Count > 0 && Worded(parts[^1]) && Worded(part) && parts[^1].Offset + parts[^1].Text.Length == part.Offset)
        {
            parts[^1] = ContentNode.Leaf(MarkdownKinds.Word, parts[^1].Text + part.Text, Roles.Element, offset: parts[^1].Offset);
            return;
        }

        parts.Add(part);
    }

    private static bool Worded(ContentNode node) => node is { IsLeaf: true, Kind: MarkdownKinds.Word, Role: Roles.Element, Offset: not null };
}
