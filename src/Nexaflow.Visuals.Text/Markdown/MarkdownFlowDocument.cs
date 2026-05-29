using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock          = Markdig.Syntax.Block;
using WpfBlock         = System.Windows.Documents.Block;
using MdTable          = Markdig.Extensions.Tables.Table;
using MdTableRow       = Markdig.Extensions.Tables.TableRow;
using MdTableCell      = Markdig.Extensions.Tables.TableCell;
using TableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Builds a selectable WPF <see cref="FlowDocument"/> from markdown. Text-bearing
/// blocks (headings, paragraphs, lists, code, quotes, tables) become real
/// FlowDocument text so they can be drag-selected and copied; math and diagrams
/// fall back to the UIElement output of <see cref="BlockRenderer"/> wrapped in a
/// <see cref="BlockUIContainer"/>.
///
/// Leaf runs are tagged with their Markdig <see cref="SourceSpan"/> (via
/// <see cref="BlockRenderer.AddInlines"/> and the code/heading paths here) so a
/// partial selection can be mapped back to the original markdown source — see
/// <see cref="MarkdownClipboard"/>.
/// </summary>
public static class MarkdownFlowDocument
{
    public static FlowDocument Build(string? markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily  = BlockRenderer.BodyFont,
            FontSize    = BlockRenderer.BaseFontSize,
            Foreground  = BlockRenderer.TextBrush,
            Background  = Brushes.Transparent,
            PagePadding = new Thickness(0),
        };

        var raw = markdown ?? string.Empty;
        if (raw.Length == 0) return doc;

        var parsed   = Markdig.Markdown.Parse(raw, MarkdownPipelineFactory.Default);
        var rawLines = raw.Split('\n');

        foreach (var block in parsed)
            foreach (var b in RenderBlock(block, BlockRaw(block, rawLines)))
                doc.Blocks.Add(b);

        return doc;
    }

    // ── Block dispatch ────────────────────────────────────────────────────

    private static IEnumerable<WpfBlock> RenderBlock(MdBlock block, string raw)
    {
        try
        {
            return block switch
            {
                HeadingBlock    hb => Heading(hb),
                ParagraphBlock  pb => [Para(pb)],
                ThematicBreakBlock => [Hr()],
                QuoteBlock      qb => [Quote(qb)],
                ListBlock       lb => [ListOf(lb)],
                // MathBlock extends FencedCodeBlock — match first
                MathBlock          => [UiFallback(block, raw)],
                FencedCodeBlock fc when DiagramRenderer.IsDiagramLanguage(fc.Info)
                                   => [UiFallback(block, raw)],
                FencedCodeBlock fc => [Code(fc.Lines.ToString(), fc.Span)],
                CodeBlock       cb => [Code(cb.Lines.ToString(), cb.Span)],
                MdTable         t  => [TableOf(t)],
                _                  => [UiFallback(block, raw)],
            };
        }
        catch
        {
            return [new Paragraph(new Run(block.ToString() ?? string.Empty))
                { Foreground = BlockRenderer.TextMutedBrush }];
        }
    }

    // ── Headings ──────────────────────────────────────────────────────────

    private static IEnumerable<WpfBlock> Heading(HeadingBlock hb)
    {
        double[] sizes = [28, 22, 18, 16, 14.5, BlockRenderer.BaseFontSize];
        var p = new Paragraph
        {
            FontSize   = sizes[Math.Clamp(hb.Level - 1, 0, 5)],
            FontWeight = FontWeights.Bold,
            Foreground = BlockRenderer.HeadingBrush,
            Margin     = new Thickness(0, hb.Level == 1 ? 14 : 10, 0, 4),
            Tag        = hb.Span,
        };
        if (hb.Inline is not null)
            foreach (var inl in hb.Inline)
                BlockRenderer.AddInlines(p.Inlines, inl);

        if (hb.Level <= 2)
        {
            p.BorderBrush     = BlockRenderer.HrBrush;
            p.BorderThickness = new Thickness(0, 0, 0, 1);
            p.Padding         = new Thickness(0, 0, 0, 4);
        }
        return [p];
    }

    // ── Paragraph ─────────────────────────────────────────────────────────

    private static Paragraph Para(ParagraphBlock pb)
    {
        var p = new Paragraph { Margin = new Thickness(0, 4, 0, 8), Tag = pb.Span };
        if (pb.Inline is not null)
            foreach (var inl in pb.Inline)
                BlockRenderer.AddInlines(p.Inlines, inl);
        return p;
    }

    // ── Thematic break ────────────────────────────────────────────────────

    private static WpfBlock Hr() =>
        new BlockUIContainer(new System.Windows.Controls.Border
        {
            Height     = 1,
            Background  = BlockRenderer.HrBrush,
            Margin     = new Thickness(0, 10, 0, 10),
        });

    // ── Block-quote ───────────────────────────────────────────────────────

    private static WpfBlock Quote(QuoteBlock qb)
    {
        var section = new Section
        {
            Background      = BlockRenderer.QuoteBgBrush,
            BorderBrush     = BlockRenderer.AccentBrush,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = new Thickness(0, 4, 0, 8),
            Tag             = qb.Span,
        };
        foreach (var child in qb)
            foreach (var b in RenderBlock(child, string.Empty))
                section.Blocks.Add(b);
        return section;
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    private static WpfBlock ListOf(ListBlock lb)
    {
        var list = new List
        {
            Margin      = new Thickness(0, 4, 0, 8),
            MarkerStyle = MarkerStyleFor(lb),
            Foreground  = BlockRenderer.TextMutedBrush,   // marker colour
        };
        if (lb.IsOrdered && int.TryParse(lb.OrderedStart, out var start) && start > 0)
            list.StartIndex = start;

        foreach (ListItemBlock li in lb)
        {
            var item = new ListItem();
            foreach (var child in li)
                foreach (var b in RenderBlock(child, string.Empty))
                {
                    // Tighten the airy default paragraph spacing inside list items
                    // and restore body colour (the List sets a muted marker colour).
                    if (b is Paragraph bp)
                    {
                        bp.Margin     = new Thickness(0, 1, 0, 1);
                        bp.Foreground = BlockRenderer.TextBrush;
                    }
                    item.Blocks.Add(b);
                }
            if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
            list.ListItems.Add(item);
        }
        return list;
    }

    private static TextMarkerStyle MarkerStyleFor(ListBlock lb)
    {
        if (!lb.IsOrdered) return TextMarkerStyle.Disc;
        return lb.BulletType switch
        {
            'a' => TextMarkerStyle.LowerLatin,
            'A' => TextMarkerStyle.UpperLatin,
            'i' => TextMarkerStyle.LowerRoman,
            'I' => TextMarkerStyle.UpperRoman,
            _   => TextMarkerStyle.Decimal,
        };
    }

    // ── Code ──────────────────────────────────────────────────────────────

    private static WpfBlock Code(string text, SourceSpan span)
    {
        var run = new Run(text.TrimEnd('\n'))
        {
            FontFamily = BlockRenderer.MonoFont,
            FontSize   = 12,
            Foreground = BlockRenderer.TextBrush,
            Tag        = span,
        };
        return new Paragraph(run)
        {
            Background      = BlockRenderer.CodeBgBrush,
            BorderBrush     = BlockRenderer.CodeBorderBrush,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12, 8, 12, 8),
            Margin          = new Thickness(0, 4, 0, 10),
        };
    }

    // ── Tables ────────────────────────────────────────────────────────────

    private static WpfBlock TableOf(MdTable table)
    {
        var t = new Table { Margin = new Thickness(0, 8, 0, 12), CellSpacing = 0 };

        int colCount = table.ColumnDefinitions.Count;
        if (colCount == 0 && table.Count > 0 && table[0] is MdTableRow first)
            colCount = first.Count;

        for (int c = 0; c < colCount; c++)
            t.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        t.RowGroups.Add(group);

        int rowIdx = 0;
        foreach (MdTableRow row in table)
        {
            var trow   = new TableRow();
            int colIdx = 0;
            foreach (MdTableCell cell in row)
            {
                if (colIdx >= colCount) break;

                var align = colIdx < table.ColumnDefinitions.Count
                    ? table.ColumnDefinitions[colIdx].Alignment ?? TableColumnAlign.Left
                    : TableColumnAlign.Left;

                var p = new Paragraph
                {
                    Margin        = new Thickness(0),
                    FontWeight    = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground    = row.IsHeader ? BlockRenderer.HeadingBrush : BlockRenderer.TextBrush,
                    FontSize      = row.IsHeader ? 13 : BlockRenderer.BaseFontSize,
                    TextAlignment = align switch
                    {
                        TableColumnAlign.Right  => TextAlignment.Right,
                        TableColumnAlign.Center => TextAlignment.Center,
                        _                       => TextAlignment.Left,
                    },
                };
                if (cell.Count > 0 && cell[0] is ParagraphBlock cpb && cpb.Inline is not null)
                    foreach (var inl in cpb.Inline)
                        BlockRenderer.AddInlines(p.Inlines, inl);

                var tc = new TableCell(p)
                {
                    BorderBrush     = BlockRenderer.TableBorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding         = new Thickness(8, 5, 8, 5),
                    Background      = row.IsHeader
                        ? BlockRenderer.TableHeaderBg
                        : (rowIdx % 2 == 1 ? BlockRenderer.TableAltRowBg : Brushes.Transparent),
                    ColumnSpan      = Math.Max(1, cell.ColumnSpan),
                    RowSpan         = Math.Max(1, cell.RowSpan),
                };

                trow.Cells.Add(tc);
                colIdx += Math.Max(1, cell.ColumnSpan);
            }
            group.Rows.Add(trow);
            rowIdx++;
        }
        return t;
    }

    // ── Math / diagram / unknown fallback ───────────────────────────────────

    private static WpfBlock UiFallback(MdBlock block, string raw) =>
        new BlockUIContainer(BlockRenderer.Render(block, raw)) { Margin = new Thickness(0) };

    // ── Raw-source extraction (mirrors MarkdownView) ────────────────────────

    private static string BlockRaw(MdBlock block, string[] rawLines)
    {
        if (rawLines.Length == 0) return string.Empty;
        int start = Math.Clamp(block.Line, 0, rawLines.Length - 1);
        int end   = Math.Clamp(BlockEndLine(block), start, rawLines.Length - 1);
        return string.Join("\n", rawLines[start..(end + 1)]);
    }

    private static int BlockEndLine(MdBlock block)
    {
        if (block is FencedCodeBlock fcb)
            return block.Line + fcb.Lines.Count + 1;
        if (block is LeafBlock lb && lb.Lines.Count > 0)
            return block.Line + lb.Lines.Count - 1;
        if (block is ContainerBlock cb && cb.Count > 0)
            return BlockEndLine(cb[cb.Count - 1]);
        return block.Line;
    }
}
