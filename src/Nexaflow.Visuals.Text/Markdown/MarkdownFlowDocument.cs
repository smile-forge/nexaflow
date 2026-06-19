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
/// Colours and the link hook come from a <see cref="MarkdownRenderContext"/>
/// (a <see cref="MarkdownPalette"/> converts implicitly; defaults to
/// <see cref="MarkdownPalette.Dark"/>).
///
/// Leaf runs are tagged with their Markdig <see cref="SourceSpan"/> (via
/// <see cref="BlockRenderer.AddInlines"/> and the code/heading paths here) so a
/// partial selection can be mapped back to the original markdown source — see
/// <see cref="MarkdownClipboard"/>.
/// </summary>
public static class MarkdownFlowDocument
{
    public static FlowDocument Build(string? markdown, MarkdownRenderContext? context = null)
    {
        var ctx = context ?? (MarkdownRenderContext)MarkdownPalette.FromTheme();
        var p   = ctx.Palette;
        var doc = new FlowDocument
        {
            FontFamily  = BlockRenderer.BodyFont,
            FontSize    = BlockRenderer.BaseFontSize,
            Foreground  = p.Text,
            Background  = Brushes.Transparent,
            PagePadding = new Thickness(0),
        };

        var raw = markdown ?? string.Empty;
        if (raw.Length == 0) return doc;

        var parsed   = Markdig.Markdown.Parse(raw, MarkdownPipelineFactory.Default);
        var rawLines = raw.Split('\n');

        foreach (var block in parsed)
            foreach (var b in RenderBlock(block, BlockRaw(block, rawLines), ctx))
                doc.Blocks.Add(b);

        return doc;
    }

    // ── Block dispatch ────────────────────────────────────────────────────

    private static IEnumerable<WpfBlock> RenderBlock(MdBlock block, string raw, MarkdownRenderContext ctx)
    {
        try
        {
            return block switch
            {
                HeadingBlock    hb => Heading(hb, ctx),
                ParagraphBlock  pb => [Para(pb, ctx)],
                ThematicBreakBlock => [Hr(ctx)],
                // YAML front matter is metadata, not content — emit nothing (matches Markdig's HTML output).
                Markdig.Extensions.Yaml.YamlFrontMatterBlock => [],
                // AlertBlock extends QuoteBlock — match first; styled callout falls back to BlockRenderer.
                Markdig.Extensions.Alerts.AlertBlock => [UiFallback(block, raw, ctx)],
                QuoteBlock      qb => [Quote(qb, ctx)],
                ListBlock       lb => [ListOf(lb, ctx)],
                // MathBlock extends FencedCodeBlock — match first
                MathBlock          => [UiFallback(block, raw, ctx)],
                FencedCodeBlock fc when DiagramRenderer.IsDiagramLanguage(fc.Info)
                                   => [DiagramFallback(block, raw, ctx)],
                FencedCodeBlock fc => [Code(fc.Lines.ToString(), fc.Span, ctx)],
                CodeBlock       cb => [Code(cb.Lines.ToString(), cb.Span, ctx)],
                MdTable         t  => [TableOf(t, ctx)],
                _                  => [UiFallback(block, raw, ctx)],
            };
        }
        catch
        {
            return [new Paragraph(new Run(block.ToString() ?? string.Empty))
                { Foreground = ctx.Palette.TextMuted }];
        }
    }

    // ── Headings ──────────────────────────────────────────────────────────

    private static IEnumerable<WpfBlock> Heading(HeadingBlock hb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        double[] sizes = [28, 22, 18, 16, 14.5, BlockRenderer.BaseFontSize];
        var para = new Paragraph
        {
            FontSize   = sizes[Math.Clamp(hb.Level - 1, 0, 5)],
            FontWeight = FontWeights.Bold,
            Foreground = p.Heading,
            Margin     = new Thickness(0, hb.Level == 1 ? 14 : 10, 0, 4),
            Tag        = hb.Span,
        };
        if (hb.Inline is not null)
            foreach (var inl in hb.Inline)
                BlockRenderer.AddInlines(para.Inlines, inl, ctx);

        if (hb.Level <= 2)
        {
            para.BorderBrush     = p.Hr;
            para.BorderThickness = new Thickness(0, 0, 0, 1);
            para.Padding         = new Thickness(0, 0, 0, 4);
        }
        return [para];
    }

    // ── Paragraph ─────────────────────────────────────────────────────────

    private static Paragraph Para(ParagraphBlock pb, MarkdownRenderContext ctx)
    {
        var para = new Paragraph { Margin = new Thickness(0, 4, 0, 8), Tag = pb.Span };
        if (pb.Inline is not null)
            foreach (var inl in pb.Inline)
                BlockRenderer.AddInlines(para.Inlines, inl, ctx);
        return para;
    }

    // ── Thematic break ────────────────────────────────────────────────────

    private static WpfBlock Hr(MarkdownRenderContext ctx) =>
        new BlockUIContainer(new System.Windows.Controls.Border
        {
            Height     = 1,
            Background  = ctx.Palette.Hr,
            Margin     = new Thickness(0, 10, 0, 10),
        });

    // ── Block-quote ───────────────────────────────────────────────────────

    private static WpfBlock Quote(QuoteBlock qb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var section = new Section
        {
            Background      = p.QuoteBg,
            BorderBrush     = p.Accent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = new Thickness(0, 4, 0, 8),
            Tag             = qb.Span,
        };
        foreach (var child in qb)
            foreach (var b in RenderBlock(child, string.Empty, ctx))
                section.Blocks.Add(b);
        return section;
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    private static WpfBlock ListOf(ListBlock lb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var list = new List
        {
            Margin      = new Thickness(0, 4, 0, 8),
            MarkerStyle = MarkerStyleFor(lb),
            Foreground  = p.TextMuted,   // marker colour
        };
        if (lb.IsOrdered && int.TryParse(lb.OrderedStart, out var start) && start > 0)
            list.StartIndex = start;

        foreach (ListItemBlock li in lb)
        {
            var item = new ListItem();
            foreach (var child in li)
                foreach (var b in RenderBlock(child, string.Empty, ctx))
                {
                    // Tighten the airy default paragraph spacing inside list items
                    // and restore body colour (the List sets a muted marker colour).
                    if (b is Paragraph bp)
                    {
                        bp.Margin     = new Thickness(0, 1, 0, 1);
                        bp.Foreground = p.Text;
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

    private static WpfBlock Code(string text, SourceSpan span, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var run = new Run(text.TrimEnd('\n'))
        {
            FontFamily = BlockRenderer.MonoFont,
            FontSize   = 12,
            Foreground = p.Text,
            Tag        = span,
        };
        return new Paragraph(run)
        {
            Background      = p.CodeBg,
            BorderBrush     = p.CodeBorder,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12, 8, 12, 8),
            Margin          = new Thickness(0, 4, 0, 10),
        };
    }

    // ── Tables ────────────────────────────────────────────────────────────

    private static WpfBlock TableOf(MdTable table, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
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

                // Single-paragraph cells (every pipe-table cell, most grid cells) stay selectable text
                // with header/alignment styling. Block-content cells (grid tables: lists, multiple
                // paragraphs) render every child block so nothing is silently dropped.
                TableCell tc;
                if (cell.Count == 1 && cell[0] is ParagraphBlock cpb)
                {
                    var para = new Paragraph
                    {
                        Margin        = new Thickness(0),
                        FontWeight    = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground    = row.IsHeader ? p.Heading : p.Text,
                        FontSize      = row.IsHeader ? 13 : BlockRenderer.BaseFontSize,
                        TextAlignment = align switch
                        {
                            TableColumnAlign.Right  => TextAlignment.Right,
                            TableColumnAlign.Center => TextAlignment.Center,
                            _                       => TextAlignment.Left,
                        },
                    };
                    if (cpb.Inline is not null)
                        foreach (var inl in cpb.Inline)
                            BlockRenderer.AddInlines(para.Inlines, inl, ctx);
                    tc = new TableCell(para);
                }
                else
                {
                    tc = new TableCell();
                    foreach (var child in cell)
                        foreach (var b in RenderBlock(child, string.Empty, ctx))
                            tc.Blocks.Add(b);
                    if (tc.Blocks.Count == 0) tc.Blocks.Add(new Paragraph());
                }

                tc.BorderBrush     = p.TableBorder;
                tc.BorderThickness = new Thickness(1);
                tc.Padding         = new Thickness(8, 5, 8, 5);
                tc.Background      = row.IsHeader
                    ? p.TableHeaderBg
                    : (rowIdx % 2 == 1 ? p.TableAltRowBg : Brushes.Transparent);
                tc.ColumnSpan      = Math.Max(1, cell.ColumnSpan);
                tc.RowSpan         = Math.Max(1, cell.RowSpan);

                trow.Cells.Add(tc);
                colIdx += Math.Max(1, cell.ColumnSpan);
            }
            group.Rows.Add(trow);
            rowIdx++;
        }
        return t;
    }

    // ── Math / diagram / unknown fallback ───────────────────────────────────

    private static WpfBlock UiFallback(MdBlock block, string raw, MarkdownRenderContext ctx) =>
        new BlockUIContainer(BlockRenderer.Render(block, raw, ctx)) { Margin = new Thickness(0) };

    /// <summary>A diagram block. With <see cref="MarkdownRenderContext.FitContentToWidth"/> the diagram's
    /// own height cap + scrollbars are dropped (so a long flowchart renders full height and the page
    /// scrolls — scrollbars can't be grabbed inside an editable surface anyway) and it is wrapped in a
    /// down-only <see cref="System.Windows.Controls.Viewbox"/> so an over-wide diagram scales to the
    /// column instead of overflowing.</summary>
    private static WpfBlock DiagramFallback(MdBlock block, string raw, MarkdownRenderContext ctx)
    {
        var element = BlockRenderer.Render(block, raw, ctx);
        if (ctx.FitContentToWidth)
        {
            UnboundDiagramHeight(element);
            element = new System.Windows.Controls.Viewbox
            {
                Child               = element,
                Stretch             = Stretch.Uniform,
                StretchDirection    = System.Windows.Controls.StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }
        return new BlockUIContainer(element) { Margin = new Thickness(0) };
    }

    /// <summary>Removes the fixed height + scrollbars from a diagram's inner
    /// <see cref="System.Windows.Controls.ScrollViewer"/> (the renderers cap it at a few hundred px) so it
    /// lays out at full natural height. Walks the logical tree, stopping at the first ScrollViewer on each
    /// branch.</summary>
    private static void UnboundDiagramHeight(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer sv)
        {
            sv.MaxHeight = double.PositiveInfinity;
            sv.VerticalScrollBarVisibility   = System.Windows.Controls.ScrollBarVisibility.Hidden;
            sv.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden;
            return;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d) UnboundDiagramHeight(d);
    }

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
