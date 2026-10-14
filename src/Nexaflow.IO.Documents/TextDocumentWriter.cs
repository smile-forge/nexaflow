using System.Text;

using Nexaflow.Markdown.Export;

namespace Nexaflow.IO.Documents;

/// <summary>
/// A document as plain text: the words a reader sees, a blank line between blocks, an item under the marker it is drawn with,
/// a table a row to a line with its cells a tab apart, code as its lines — and what another language drew as the source it
/// was drawn from, since a line of text cannot hold the picture. What only says how something looks is left behind with the
/// looks.
/// </summary>
public sealed class TextDocumentWriter : IDocumentWriter
{
    /// <inheritdoc/>
    public void Write(ExportDocument document, Stream into)
    {
        var bytes = Encoding.UTF8.GetBytes(this.Written(document));
        into.Write(bytes, 0, bytes.Length);
    }

    /// <summary><paramref name="document"/> as plain text, its lines ended with <c>\n</c>.</summary>
    public string Written(ExportDocument document)
    {
        var text = new StringBuilder();
        Blocks(document.Blocks, text, string.Empty);

        return text.ToString().TrimEnd('\n') is { Length: > 0 } said ? said + "\n" : string.Empty;
    }

    /// <summary>A run of words as plain text on its own.</summary>
    public string Written(IReadOnlyList<ExportInline> words)
    {
        var text = new StringBuilder();
        Inlines(words, text);

        return text.ToString();
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    /// <param name="indent">What every line is set in by: an item's blocks stand under its words, not under its marker.</param>
    private static void Blocks(IReadOnlyList<ExportBlock> blocks, StringBuilder text, string indent)
    {
        var first = true;

        foreach (var block in blocks)
        {
            if (!first) text.Append('\n');
            first = false;

            Block(block, text, indent);
        }
    }

    private static void Block(ExportBlock block, StringBuilder text, string indent)
    {
        switch (block)
        {
            case ExportParagraph paragraph: Lines(Said(paragraph.Words), text, indent); return;
            case ExportWords words: Lines(Said(words.Words), text, indent); return;
            case ExportHeading heading: Lines(Said(heading.Words), text, indent); return;
            case ExportTerm term: Lines(Said(term.Words), text, indent); return;
            case ExportCaption caption: Lines(Said(caption.Words), text, indent); return;
            case ExportCode code: Lines(code.Text, text, indent); return;
            case ExportMarkup markup: Lines(markup.Text, text, indent); return;
            case ExportDrawn drawn: Lines(drawn.Drawing.Source, text, indent); return;

            case ExportRule or ExportPageBreak:
                return;

            case ExportQuote quote:
                if (quote.Callout is { } callout)
                {
                    Lines(callout.Label, text, indent);
                    if (quote.Blocks.Count > 0) text.Append('\n');
                }

                Blocks(quote.Blocks, text, indent);
                return;

            case ExportDescription description: Blocks(description.Blocks, text, indent + "    "); return;
            case ExportDefinitions definitions: Blocks(definitions.Entries, text, indent); return;
            case ExportFigure figure: Blocks(figure.Blocks, text, indent); return;
            case ExportFooter footer: Blocks(footer.Blocks, text, indent); return;

            case ExportList list:
                Listed(list, text, indent);
                return;

            case ExportTable table:
                foreach (var row in table.Rows)
                    Lines(string.Join('\t', row.Cells.Select(cell => Celled(cell))), text, indent);

                return;
        }
    }

    /// <summary>A list: each item under the marker it is drawn with — its box, where it has one — and an item's blocks set in under its words.</summary>
    private static void Listed(ExportList list, StringBuilder text, string indent)
    {
        var first = true;

        foreach (var item in list.Items)
        {
            if (!first && list.Loose) text.Append('\n');
            first = false;

            var marker = item.Done switch
            {
                true => "[x]",
                false => "[ ]",
                null => item.Marker,
            } + " ";

            var start = text.Length;
            var under = indent + new string(' ', marker.Length);

            if (item.Blocks.Count == 0) text.Append(indent).Append(marker.TrimEnd()).Append('\n');
            else Blocks(item.Blocks, text, under);

            // The marker goes in front of the item's first line, where its words are set in.
            if (item.Blocks.Count > 0 && text.Length - start >= under.Length) text.Remove(start, under.Length).Insert(start, indent + marker);
        }
    }

    /// <summary>What a cell holds, on one line.</summary>
    private static string Celled(ExportCell cell)
    {
        var said = new StringBuilder();
        Blocks(cell.Blocks, said, string.Empty);

        return said.ToString().Trim('\n').Replace('\n', ' ');
    }

    /// <summary>Lines of text, each set in by <paramref name="indent"/> and ended.</summary>
    private static void Lines(string said, StringBuilder text, string indent)
    {
        foreach (var line in said.Split('\n')) text.Append(indent).Append(line).Append('\n');
    }

    // ── Runs of a line ──────────────────────────────────────────────────────

    private static string Said(IReadOnlyList<ExportInline> words)
    {
        var text = new StringBuilder();
        Inlines(words, text);

        return text.ToString();
    }

    private static void Inlines(IReadOnlyList<ExportInline> words, StringBuilder text)
    {
        foreach (var word in words)
        {
            switch (word)
            {
                case ExportText said: text.Append(said.Text); break;
                case ExportCodeSpan code: text.Append(code.Text); break;
                case ExportStyled styled: Inlines(styled.Words, text); break;
                case ExportLooked looked: Inlines(looked.Words, text); break;
                case ExportLink link: Inlines(link.Words, text); break;
                case ExportImage image: Inlines(image.Words, text); break;
                case ExportAbbreviation abbreviation: text.Append(abbreviation.Text); break;
                case ExportBreak { Hard: true }: text.Append('\n'); break;
                case ExportBreak: text.Append(' '); break;
                case ExportTab: text.Append('\t'); break;
                case ExportDrawnSpan drawn: text.Append(drawn.Drawing.Source); break;
            }
        }
    }
}
