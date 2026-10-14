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
/// function of what a block says — nothing else goes in. So a block written the same is handed back as it was read last time,
/// and only the one typed in is read again. Kept for the blocks of one reading at a time: what the reading before that held is
/// let go.
/// </para>
/// </summary>
internal sealed class MarkdownBlocks
{
    /// <summary>
    /// What each block of the last reading came to, by the characters its body was written as and what stood in front of it on
    /// its first line — the same characters read differently at a different depth.
    /// </summary>
    private Dictionary<(string Lead, string Written), List<(ContentNode Written, ContentNode Read)>> _before = [];

    /// <summary>The document's blocks read, each one that is written exactly as it was last time handed back as it was read then.</summary>
    /// <param name="document">The document as the split hands it over: every block named, every body still its own source.</param>
    /// <param name="source">What the document was read from, which says what stands in front of each block on its line.</param>
    public ContentNode Read(ContentNode document, string source)
    {
        var now = new Dictionary<(string Lead, string Written), List<(ContentNode Written, ContentNode Read)>>(_before.Count);
        var seen = new ContentNode[document.Children.Count];
        var moved = false;

        for (var at = 0; at < seen.Length; at++)
        {
            var block = document.Children[at];

            if (block.Part(Roles.Body) is not { IsLeaf: true, Kind: Kinds.Verbatim or Kinds.Nested } body)
            {
                seen[at] = ReadIn(block, source);
            }
            else
            {
                var written = (Lead(source, body.Offset), body.Text);
                seen[at] = Remembered(written, block) ?? ReadIn(block, source);

                if (!now.TryGetValue(written, out var alike)) now[written] = alike = [];
                alike.Add((block, seen[at]));
            }

            moved |= !ReferenceEquals(seen[at], block);
        }

        _before = now;
        return moved ? document.With(seen) : document;
    }

    /// <summary>
    /// What a block written as this one is was read as last time, moved along to where this one now stands — each handed
    /// out once.
    ///
    /// <para>
    /// How far it has moved is the two written blocks' own offsets, not the reading's: a reading is a tree of pieces that
    /// each say where they were read from, and the piece at the top of one need not say anything at all.
    /// </para>
    /// </summary>
    private ContentNode? Remembered((string Lead, string Written) written, ContentNode block)
    {
        if (!_before.TryGetValue(written, out var alike)) return null;

        for (var at = 0; at < alike.Count; at++)
        {
            if (!Alike(alike[at].Written, block)) continue;

            var (was, read) = alike[at];
            alike.RemoveAt(at);

            return was.Offset is { } stood && block.Offset is { } now ? read.FurtherOn(now - stood) : read;
        }

        return null;
    }

    /// <summary>
    /// Whether two blocks say the same: the same characters in the same shape, and the same things the parser worked out
    /// about them.
    ///
    /// <para>
    /// Where a block stands is deliberately none of it. What a reading came to depends on the characters, so the same
    /// words further down the page were read the same way, and the reading kept from last time is moved along to where
    /// this block stands on the way out (<see cref="ContentNode.FurtherOn"/>) rather than read again. Counting the
    /// offsets here would make every block below a keystroke a block nothing remembers.
    /// </para>
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
    /// Which parser reads this block, where one reads it, and told where the block's source stands in the
    /// document so every piece it reads says where it was read from. A kind that is not here is a kind nothing
    /// can read yet, and what it holds stays exactly as it was typed.
    ///
    /// <para>
    /// Every reader is told what stands in front of the block on its first line too, because it reads lines, and a block's
    /// later lines carry its containers' marks where its first does not. A table's cells are no block of their own: the
    /// table's reader reads them where they stand (<see cref="MarkdownTable"/>), a grid table's cell being no one stretch of the
    /// document.
    /// </para>
    /// </summary>
    private static Func<string, int, string, ContentNode>? Reader(ContentNode node) => node.Kind switch
    {


        MarkdownKinds.Paragraph or MarkdownKinds.Term or MarkdownKinds.Caption =>
            static (source, at, lead) => MarkdownInline.Read(source, at: at, lead: lead).As(Roles.Body),

        MarkdownKinds.Heading => static (source, at, lead) => MarkdownInline.Heading(source, at: at, lead: lead).As(Roles.Body),

        MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.Definition or MarkdownKinds.Described
            or MarkdownKinds.Figure or MarkdownKinds.Footer => static (source, at, lead) => MarkdownParser.Inside(source, at: at, lead: lead),

        MarkdownKinds.List => static (source, at, lead) => MarkdownList.Read(source, at: at, lead: lead),
        MarkdownKinds.Table => static (source, at, lead) => MarkdownTable.Read(source, at: at, lead: lead),

        _ => null,
    };

    /// <summary>What stands in front of a block's first character on the line it starts on — nothing for one that starts its line.</summary>
    private static string Lead(string document, int? at)
    {
        if (at is not { } stands || stands <= 0 || stands > document.Length) return string.Empty;

        return document[(document.LastIndexOf('\n', stands - 1) + 1)..stands];
    }

    /// <summary>This piece with its body read, everything under it the same, and then finished.</summary>
    private static ContentNode ReadIn(ContentNode node, string source)
    {
        if (node.IsLeaf) return node;

        // The reader is told where the body's characters stand, which is what the body itself says — the split that
        // named this block cut them out of the document and said so. A body that says nothing is read from nought,
        // which is the only answer left and the one the oracle over every corpus is there to catch.
        if (Reader(node) is { } reader && node.Part(Roles.Body) is { IsLeaf: true, Kind: Kinds.Verbatim } body
            && reader(body.Text, body.Offset ?? 0, Lead(source, body.Offset)) is { } read && !Circles(node, body, read))
            node = node.With([.. node.Children.Select(child => ReferenceEquals(child, body) ? read : child)]);

        ContentNode[]? seen = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = ReadIn(child, source);

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
