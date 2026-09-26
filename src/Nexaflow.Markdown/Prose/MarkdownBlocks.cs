using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The pass of the parse that reads each block's body with the reader its kind names, and finishes every piece it reads.
///
/// <para>
/// The document's split names each block and stops, which leaves every block holding one stretch of verbatim source: its
/// own. This is where that source is read — a paragraph's and a heading's into the constructs they were spelled with, a
/// table's into rows and cells, a list's into items, a quote's into the blocks written inside it. Each reader goes one level
/// and hands back blocks whose bodies are verbatim again, and this keeps walking, so nesting costs nobody anything to write.
/// Every reader checks that what it hands back prints as what it was given, so a kind with no reader yet — a fence, indented
/// code, front matter — is still the one verbatim leaf it was, which is the fallback every builder already knows how to draw.
/// </para>
/// <para>
/// <strong>Each piece is finished as it is read:</strong> what its parts make together (<see cref="MarkdownGroups"/>) and the
/// line break closing a block held as written (<see cref="MarkdownClosingLines"/>) are settled on the same walk, from the
/// bottom up. So a block is whole when this hands it back, and what is kept of it for the next reading is the block as the
/// parse hands it out.
/// </para>
/// <para>
/// <strong>One per document, read again and again as it is written.</strong> A keystroke changes one block, and reading is a
/// function of what a block says and what the document defines — nothing else goes in. So a block written the same beside
/// the same definitions is handed back as it was read last time, and only the one typed in is read again. Kept for the blocks
/// of one reading at a time: what the reading before that held is let go.
/// </para>
/// </summary>
internal sealed class MarkdownBlocks
{
    /// <summary>What each block of the last reading came to, by the characters its body was written as.</summary>
    private Dictionary<string, List<(ContentNode Written, ContentNode Read)>> _before = [];

    /// <summary>What the definitions of the document said the last time, which every block's words were read beside.</summary>
    private string? _besides;

    /// <summary>The document's blocks read, each one that is written exactly as it was last time handed back as it was read then.</summary>
    public ContentNode Read(ContentNode document)
    {
        var besides = MarkdownDefinitions.Of(document)?.Text;

        if (besides != _besides) _before = [];
        _besides = besides;

        var now = new Dictionary<string, List<(ContentNode Written, ContentNode Read)>>(_before.Count);
        var seen = new ContentNode[document.Children.Count];
        var moved = false;

        for (var at = 0; at < seen.Length; at++)
        {
            var block = document.Children[at];

            if (block.Part(Roles.Body) is not { IsLeaf: true, Kind: Kinds.Verbatim } body)
            {
                seen[at] = Read(block, besides);
            }
            else
            {
                seen[at] = Remembered(body.Text, block) ?? Read(block, besides);

                if (!now.TryGetValue(body.Text, out var alike)) now[body.Text] = alike = [];
                alike.Add((block, seen[at]));
            }

            moved |= !ReferenceEquals(seen[at], block);
        }

        _before = now;
        return moved ? document.With(seen) : document;
    }

    /// <summary>What a block written as this one is was read as last time — each handed out once.</summary>
    private ContentNode? Remembered(string written, ContentNode block)
    {
        if (!_before.TryGetValue(written, out var alike)) return null;

        for (var at = 0; at < alike.Count; at++)
        {
            if (!Alike(alike[at].Written, block)) continue;

            var read = alike[at].Read;
            alike.RemoveAt(at);
            return read;
        }

        return null;
    }

    /// <summary>
    /// Whether two blocks say the same: the same characters in the same shape, and the same things the parser worked out
    /// about them — a heading's name, which moves when another heading is written above it, is not in its characters.
    /// </summary>
    private static bool Alike(ContentNode was, ContentNode now)
    {
        if (ReferenceEquals(was, now)) return true;

        if (was.Kind != now.Kind
            || was.Role != now.Role
            || was.Text != now.Text
            || was.Trouble != now.Trouble
            || !Equals(was.Held, now.Held)
            || was.Children.Count != now.Children.Count) return false;

        for (var at = 0; at < was.Children.Count; at++)
            if (!Alike(was.Children[at], now.Children[at])) return false;

        return true;
    }

    /// <summary>
    /// Which parser reads this block, where one reads it. A kind that is not here is a kind nothing can read
    /// yet, and what it holds stays exactly as it was typed.
    ///
    /// <para>
    /// Asked of the node rather than of its kind alone, because one kind is read two ways: a cell of a pipe
    /// table holds a run of words and a cell of a grid table can hold a whole document, and which it is was
    /// settled by the reader that took the table in.
    /// </para>
    /// </summary>
    /// <param name="besides">What the document defines, which the words of a block are read beside.</param>
    private static Func<string, ContentNode>? Reader(ContentNode node, string? besides) => node.Kind switch
    {
        MarkdownKinds.Cell when Holds(node) => source => MarkdownParser.Inside(source),

        MarkdownKinds.Paragraph or MarkdownKinds.Heading or MarkdownKinds.Cell
            or MarkdownKinds.Term or MarkdownKinds.Caption =>
            source => MarkdownInline.Read(source, besides: besides).As(Roles.Body),

        MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.Definition or MarkdownKinds.Described
            or MarkdownKinds.Figure or MarkdownKinds.Footer => source => MarkdownParser.Inside(source),

        MarkdownKinds.List => source => MarkdownList.Read(source),
        MarkdownKinds.Table => source => MarkdownTable.Read(source),

        _ => null,
    };

    /// <summary>Whether the reader that took this piece in said it holds blocks rather than words.</summary>
    private static bool Holds(ContentNode node)
    {
        foreach (var child in node.Children)
            foreach (var held in child.Children)
                if (held.Kind == MarkdownKinds.Blocks) return true;

        return false;
    }

    /// <summary>This piece with its body read, everything under it the same, and then finished.</summary>
    private static ContentNode Read(ContentNode node, string? besides)
    {
        if (node.IsLeaf) return node;

        if (Reader(node, besides) is { } reader && node.Part(Roles.Body) is { IsLeaf: true, Kind: Kinds.Verbatim } body
            && reader(body.Text) is { } read && !Circles(node, body, read))
            node = node.With([.. node.Children.Select(child => ReferenceEquals(child, body) ? read : child)]);

        ContentNode[]? seen = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = Read(child, besides);

            if (seen is null && !ReferenceEquals(now, child))
            {
                seen = new ContentNode[node.Children.Count];
                for (var before = 0; before < at; before++) seen[before] = node.Children[before];
            }

            if (seen is not null) seen[at] = now;
        }

        return MarkdownClosingLines.Closed(MarkdownGroups.Grouped(seen is null ? node : node.With(seen)));
    }

    /// <summary>
    /// Whether a reading came back as the very block it was asked to look inside.
    ///
    /// <para>
    /// Some blocks read as themselves when read alone: the <c>:</c> line of a definition is a definition item
    /// on its own, and a footer's line is a footer. Nothing was learned, and reading it once more would never
    /// stop — so the body stays the one stretch of source it was, which is what a kind with no reader gets
    /// anyway.
    /// </para>
    /// <para>
    /// Both halves are needed. The same kind alone is not enough, because a quotation inside a quotation is a
    /// quotation holding a quotation and that is exactly right — what says it went nowhere is that the block
    /// it found is the whole of what it was handed, character for character.
    /// </para>
    /// </summary>
    private static bool Circles(ContentNode node, ContentNode body, ContentNode read)
    {
        ContentNode? only = null;

        foreach (var child in read.Children)
        {
            if (child.Role == Roles.Trivia || child.IsDerived) continue;
            if (only is not null) return false;

            only = child;
        }

        return only is not null && only.Kind == node.Kind && only.Prints(body.Text);
    }
}
