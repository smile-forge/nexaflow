using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footers;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMath.Controls;
using Nexaflow.Features.Markdown.Markdown;
using MdBlock        = Markdig.Syntax.Block;
using MdFigure       = Markdig.Extensions.Figures.Figure;
using MdFigureCaption= Markdig.Extensions.Figures.FigureCaption;
using MdInline       = Markdig.Syntax.Inlines.Inline;
using MdTable        = Markdig.Extensions.Tables.Table;
using MdTableCell    = Markdig.Extensions.Tables.TableCell;
using MdTableRow     = Markdig.Extensions.Tables.TableRow;
using TableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using WpfInline      = System.Windows.Documents.Inline;

namespace Nexaflow.Features.Markdown.Markdown;

/// <summary>
/// Converts a single Markdig <see cref="MdBlock"/> into a WPF
/// <see cref="FrameworkElement"/> for use in the unified block-based markdown editor.
/// </summary>
public static class BlockRenderer
{
    // ── Dark-theme palette ────────────────────────────────────────────────

    private static readonly Brush TextBrush        = Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static readonly Brush TextMutedBrush   = Frozen(Color.FromRgb(0x78, 0x80, 0xA0));
    private static readonly Brush AccentBrush      = Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static readonly Brush CodeBgBrush      = Frozen(Color.FromRgb(0x08, 0x0C, 0x16));
    private static readonly Brush QuoteBgBrush     = Frozen(Color.FromRgb(0x2A, 0x30, 0x47));
    private static readonly Brush HeadingBrush     = Frozen(Color.FromRgb(0xA8, 0xD4, 0xFF));
    private static readonly Brush HrBrush          = Frozen(Color.FromRgb(0x2A, 0x30, 0x47));
    private static readonly Brush CodeBorderBrush  = Frozen(Color.FromRgb(0x2A, 0x30, 0x47));
    private static readonly Brush TableBorderBrush = Frozen(Color.FromRgb(0x3A, 0x42, 0x5C));
    private static readonly Brush TableHeaderBg    = Frozen(Color.FromRgb(0x1E, 0x24, 0x38));
    private static readonly Brush TableAltRowBg    = Frozen(Color.FromRgb(0x11, 0x15, 0x22));
    private static readonly Brush DefTermBrush     = Frozen(Color.FromRgb(0xD0, 0xE8, 0xFF));
    private static readonly Brush FigureBorderBrush= Frozen(Color.FromRgb(0x3A, 0x42, 0x5C));
    private static readonly Brush FigureBgBrush    = Frozen(Color.FromRgb(0x11, 0x15, 0x22));
    private static readonly Brush FooterBgBrush    = Frozen(Color.FromRgb(0x11, 0x15, 0x22));
    private static readonly Brush CitationBrush    = Frozen(Color.FromRgb(0xC8, 0xA8, 0xFF));

    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily MonoFont = new("Consolas, Courier New");
    private const double BaseFontSize = 13.5;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ── Public entry point ────────────────────────────────────────────────

    /// <param name="rawMarkdown">
    /// The raw markdown source for this block.  Required for accurate math
    /// formula extraction; ignored by all other block types.
    /// </param>
    public static FrameworkElement Render(MdBlock block, string rawMarkdown = "")
    {
        try
        {
            return block switch
            {
                HeadingBlock       hb  => RenderHeading(hb),
                ParagraphBlock     pb  => RenderParagraph(pb),
                ThematicBreakBlock     => RenderHr(),
                QuoteBlock         qb  => RenderBlockQuote(qb),
                ListBlock          lb  => RenderList(lb),
                // MathBlock extends FencedCodeBlock — must match first
                MathBlock          mb  => RenderMathBlock(mb, rawMarkdown),
                // Diagram blocks: check Info before falling through to generic code
                FencedCodeBlock    fc when DiagramRenderer.IsDiagramLanguage(fc.Info)
                                       => DiagramRenderer.Render(fc.Info!, ExtractFencedContent(fc, rawMarkdown)),
                FencedCodeBlock    fc  => RenderCode(fc.Lines.ToString()),
                CodeBlock          cb  => RenderCode(cb.Lines.ToString()),
                MdTable            t   => RenderTable(t),
                DefinitionList     dl  => RenderDefinitionList(dl),
                DefinitionTerm     dt  => RenderDefinitionTerm(dt),
                DefinitionItem     di  => RenderDefinitionItem(di),
                MdFigure           fig => RenderFigure(fig),
                MdFigureCaption    fc2 => RenderFigureCaption(fc2),
                FooterBlock        fb  => RenderFooter(fb),
                _                      => RenderFallback(block)
            };
        }
        catch
        {
            return new TextBlock
            {
                Text         = block.ToString() ?? string.Empty,
                Foreground   = TextMutedBrush,
                FontSize     = BaseFontSize,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    // ── Headings ──────────────────────────────────────────────────────────

    private static FrameworkElement RenderHeading(HeadingBlock hb)
    {
        double[] sizes = [28, 22, 18, 16, 14.5, BaseFontSize];
        var tb = MakeTextBlock();
        tb.FontSize   = sizes[Math.Clamp(hb.Level - 1, 0, 5)];
        tb.FontWeight = FontWeights.Bold;
        tb.Foreground = HeadingBrush;
        tb.Margin     = new Thickness(0, hb.Level == 1 ? 14 : 10, 0, 4);

        if (hb.Inline is not null)
            foreach (var inline in hb.Inline)
                AddInlines(tb.Inlines, inline);

        if (hb.Level > 2) return tb;

        var stack = new StackPanel();
        stack.Children.Add(tb);
        stack.Children.Add(new Border
        {
            Height     = 1,
            Background = HrBrush,
            Margin     = new Thickness(0, 3, 0, 8)
        });
        return stack;
    }

    // ── Paragraph ─────────────────────────────────────────────────────────

    private static FrameworkElement RenderParagraph(ParagraphBlock pb)
    {
        var tb = MakeTextBlock();
        tb.Margin = new Thickness(0, 4, 0, 8);

        if (pb.Inline is not null)
            foreach (var inline in pb.Inline)
                AddInlines(tb.Inlines, inline);

        return tb;
    }

    // ── Thematic break ────────────────────────────────────────────────────

    private static FrameworkElement RenderHr() =>
        new Border { Height = 1, Background = HrBrush, Margin = new Thickness(0, 10, 0, 10) };

    // ── Block-quote ───────────────────────────────────────────────────────

    private static FrameworkElement RenderBlockQuote(QuoteBlock qb)
    {
        var inner = new StackPanel { Margin = new Thickness(12, 6, 12, 6) };
        foreach (var child in qb)
            inner.Children.Add(Render(child));

        return new Border
        {
            Background      = QuoteBgBrush,
            BorderBrush     = AccentBrush,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Margin          = new Thickness(0, 4, 0, 8),
            Child           = inner
        };
    }

    // ── Lists (including list-extras: alpha, roman) ───────────────────────

    private static FrameworkElement RenderList(ListBlock lb)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        int ordinal = 1;
        if (lb.IsOrdered && lb.OrderedStart is string s)
            _ = int.TryParse(s, out ordinal);

        foreach (ListItemBlock li in lb)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text              = MakeListMarker(lb, ordinal),
                Foreground        = TextMutedBrush,
                FontSize          = BaseFontSize,
                FontFamily        = BodyFont,
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(0, 4, 4, 0),
                TextAlignment     = TextAlignment.Right
            };
            Grid.SetColumn(marker, 0);

            var content = new StackPanel();
            Grid.SetColumn(content, 1);
            foreach (var child in li)
                content.Children.Add(Render(child));

            row.Children.Add(marker);
            row.Children.Add(content);
            stack.Children.Add(row);

            if (lb.IsOrdered) ordinal++;
        }

        return stack;
    }

    private static string MakeListMarker(ListBlock lb, int ordinal)
    {
        if (!lb.IsOrdered) return "•";

        return lb.BulletType switch
        {
            'a' => $"{(char)('a' + ordinal - 1)}.",
            'A' => $"{(char)('A' + ordinal - 1)}.",
            'i' => $"{ToRoman(ordinal).ToLower()}.",
            'I' => $"{ToRoman(ordinal)}.",
            _   => $"{ordinal}."
        };
    }

    private static string ToRoman(int n)
    {
        if (n < 1) return n.ToString();
        (int v, string s)[] map =
        [
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),
            (100,"C"),(90,"XC"),(50,"L"),(40,"XL"),
            (10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")
        ];
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in map)
            while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }

    // ── Code blocks ───────────────────────────────────────────────────────

    private static FrameworkElement RenderCode(string text) =>
        new Border
        {
            Background      = CodeBgBrush,
            BorderBrush     = CodeBorderBrush,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12, 8, 12, 8),
            Margin          = new Thickness(0, 4, 0, 10),
            Child           = new TextBlock
            {
                Text         = text.TrimEnd('\n'),
                FontFamily   = MonoFont,
                FontSize     = 12,
                Foreground   = TextBrush,
                TextWrapping = TextWrapping.NoWrap
            }
        };

    // ── Table (pipe tables + grid tables) ────────────────────────────────

    private static FrameworkElement RenderTable(MdTable table)
    {
        var grid = new Grid
        {
            Margin            = new Thickness(0, 8, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        int colCount = table.ColumnDefinitions.Count;
        if (colCount == 0 && table.Count > 0 && table[0] is MdTableRow firstRow)
            colCount = firstRow.Count;

        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowIdx = 0;
        foreach (MdTableRow row in table)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int colIdx = 0;
            foreach (MdTableCell cell in row)
            {
                if (colIdx >= colCount) break;

                var align = colIdx < table.ColumnDefinitions.Count
                    ? table.ColumnDefinitions[colIdx].Alignment ?? TableColumnAlign.Left
                    : TableColumnAlign.Left;

                var tb = MakeTextBlock();
                tb.FontWeight     = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
                tb.Foreground     = row.IsHeader ? HeadingBrush : TextBrush;
                tb.FontSize       = row.IsHeader ? 13 : BaseFontSize;
                tb.Padding        = new Thickness(8, 5, 8, 5);
                tb.TextAlignment  = align switch
                {
                    TableColumnAlign.Right  => TextAlignment.Right,
                    TableColumnAlign.Center => TextAlignment.Center,
                    _                       => TextAlignment.Left
                };

                // Cells contain ParagraphBlock children; extract inlines from the first
                if (cell.Count > 0 && cell[0] is ParagraphBlock pb && pb.Inline is not null)
                    foreach (var inl in pb.Inline)
                        AddInlines(tb.Inlines, inl);

                var bg = row.IsHeader
                    ? TableHeaderBg
                    : (rowIdx % 2 == 1 ? TableAltRowBg : Brushes.Transparent);

                var cellBorder = new Border
                {
                    Background      = bg,
                    BorderBrush     = TableBorderBrush,
                    BorderThickness = new Thickness(1),
                    Child           = tb
                };

                int colspan = Math.Max(1, cell.ColumnSpan);
                int rowspan = Math.Max(1, cell.RowSpan);

                Grid.SetRow(cellBorder,    rowIdx);
                Grid.SetColumn(cellBorder, colIdx);
                if (colspan > 1) Grid.SetColumnSpan(cellBorder, colspan);
                if (rowspan > 1) Grid.SetRowSpan(cellBorder,    rowspan);

                grid.Children.Add(cellBorder);
                colIdx += colspan;
            }

            rowIdx++;
        }

        return grid;
    }

    // ── Definition list ───────────────────────────────────────────────────

    private static FrameworkElement RenderDefinitionList(DefinitionList dl)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        foreach (var child in dl)
            stack.Children.Add(Render(child));
        return stack;
    }

    private static FrameworkElement RenderDefinitionTerm(DefinitionTerm dt)
    {
        var tb = MakeTextBlock();
        tb.FontWeight = FontWeights.SemiBold;
        tb.Foreground = DefTermBrush;
        tb.Margin     = new Thickness(0, 6, 0, 2);

        if (dt.Inline is not null)
            foreach (var inl in dt.Inline)
                AddInlines(tb.Inlines, inl);

        return tb;
    }

    // ── Figure ────────────────────────────────────────────────────────────

    private static FrameworkElement RenderFigure(MdFigure fig)
    {
        var inner = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        foreach (var child in fig)
            inner.Children.Add(Render(child));

        return new Border
        {
            Background      = FigureBgBrush,
            BorderBrush     = FigureBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 8, 0, 8),
            Child           = inner
        };
    }

    private static FrameworkElement RenderFigureCaption(MdFigureCaption fc)
    {
        var tb = MakeTextBlock();
        tb.FontStyle  = FontStyles.Italic;
        tb.FontSize   = 12;
        tb.Foreground = TextMutedBrush;
        tb.TextAlignment = TextAlignment.Center;
        tb.Margin     = new Thickness(0, 2, 0, 6);

        if (fc.Inline is not null)
            foreach (var inl in fc.Inline)
                AddInlines(tb.Inlines, inl);

        return tb;
    }

    // ── Footer ────────────────────────────────────────────────────────────

    private static FrameworkElement RenderFooter(FooterBlock fb)
    {
        var inner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var child in fb)
        {
            var rendered = Render(child);
            if (rendered is TextBlock ftb)
            {
                ftb.FontSize  = 12;
                ftb.Foreground = TextMutedBrush;
            }
            inner.Children.Add(rendered);
        }

        var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };
        stack.Children.Add(new Border { Height = 1, Background = HrBrush });
        stack.Children.Add(new Border
        {
            Background = FooterBgBrush,
            Padding    = new Thickness(0, 4, 0, 4),
            Child      = inner
        });
        return stack;
    }

    // ── Math block ($$ ... $$) ────────────────────────────────────────────

    private static FrameworkElement RenderMathBlock(MathBlock mb, string rawMarkdown)
    {
        // Prefer raw-text extraction (strip the $$ fence lines) so that we are
        // not affected by any Markdig version quirks in StringLineGroup.ToString().
        string latex;
        if (!string.IsNullOrWhiteSpace(rawMarkdown))
        {
            var raw = rawMarkdown.Split('\n');
            // raw[0] = opening $$, raw[^1] = closing $$ (or trailing blank)
            int first = 1;
            int last  = raw.Length - 1;
            while (last > first && raw[last].Trim() is "" or "$$") last--;
            latex = (last >= first)
                ? string.Join('\n', raw[first..(last + 1)]).Trim()
                : string.Empty;
        }
        else
        {
            latex = mb.Lines.ToString().Trim();
        }

        if (!string.IsNullOrWhiteSpace(latex))
        {
            try
            {
                var formula = new FormulaControl
                {
                    Formula             = latex,
                    Scale               = BaseFontSize * 1.5,
                    Foreground          = TextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 8, 0, 8)
                };
                if (!formula.HasError)
                    return formula;
            }
            catch { }
        }

        // Fallback: show the LaTeX source in a clearly styled block so the user
        // can see what formula was written even when WpfMath cannot render it.
        return new Border
        {
            Background          = CodeBgBrush,
            BorderBrush         = AccentBrush,
            BorderThickness     = new Thickness(2),
            Padding             = new Thickness(12, 8, 12, 8),
            Margin              = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text         = string.IsNullOrWhiteSpace(latex) ? "(empty math block)" : $"$$\n{latex}\n$$",
                FontFamily   = MonoFont,
                FontSize     = 12,
                Foreground   = AccentBrush,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    // ── Definition item (<dd>) ────────────────────────────────────────────

    private static FrameworkElement RenderDefinitionItem(DefinitionItem di)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 0, 0, 4) };
        foreach (var child in di)
            stack.Children.Add(Render(child));
        return stack;
    }

    // ── Fallback ──────────────────────────────────────────────────────────

    private static FrameworkElement RenderFallback(MdBlock block) =>
        new TextBlock
        {
            Text         = block.ToString() ?? string.Empty,
            Foreground   = TextMutedBrush,
            FontSize     = BaseFontSize,
            TextWrapping = TextWrapping.Wrap
        };

    // ── TextBlock factory ─────────────────────────────────────────────────

    private static TextBlock MakeTextBlock() =>
        new()
        {
            Foreground   = TextBrush,
            FontSize     = BaseFontSize,
            FontFamily   = BodyFont,
            TextWrapping = TextWrapping.Wrap,
            LineHeight   = double.NaN
        };

    // ── Inline rendering ──────────────────────────────────────────────────

    internal static void AddInlines(InlineCollection target, MdInline inline)
    {
        switch (inline)
        {
            case TaskList tl:
                // Render task list checkbox
                target.Add(new Run(tl.Checked ? "☑ " : "☐ ")
                {
                    Foreground = tl.Checked ? AccentBrush : TextMutedBrush
                });
                break;

            case LiteralInline li:
                target.Add(new Run(li.Content.ToString()));
                break;

            case EmphasisInline ei when ei.DelimiterChar == '^':
                // Citation (^^text^^) — rendered as smaller raised text in a distinct colour
                var citeSpan = new Span
                {
                    Foreground = CitationBrush,
                    FontSize   = BaseFontSize * 0.85,
                    BaselineAlignment = BaselineAlignment.Superscript
                };
                foreach (var child in ei) AddInlines(citeSpan.Inlines, child);
                target.Add(citeSpan);
                break;

            case EmphasisInline ei:
                // Bold / italic (and strong)
                Span emphSpan;
                if (ei.DelimiterCount >= 2)
                    emphSpan = new Span { FontWeight = FontWeights.Bold };
                else
                    emphSpan = new Span { FontStyle = FontStyles.Italic };
                foreach (var child in ei) AddInlines(emphSpan.Inlines, child);
                target.Add(emphSpan);
                break;

            case CodeInline ci:
                target.Add(new Run(ci.Content)
                {
                    FontFamily = MonoFont,
                    FontSize   = 12,
                    Background = CodeBgBrush,
                    Foreground = AccentBrush
                });
                break;

            case LinkInline link when !link.IsImage:
                var hyper = new Hyperlink
                {
                    Foreground      = AccentBrush,
                    TextDecorations = TextDecorations.Underline,
                    NavigateUri     = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? uri : null
                };
                hyper.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
                    catch { }
                    e.Handled = true;
                };
                foreach (var child in link) AddInlines(hyper.Inlines, child);
                target.Add(hyper);
                break;

            case LineBreakInline lbr:
                target.Add(lbr.IsHard ? (WpfInline)new LineBreak() : new Run(" "));
                break;

            case MathInline mi:
                var miLatex = mi.Content.ToString();
                try
                {
                    var ctrl = new FormulaControl
                    {
                        Formula    = miLatex,
                        Scale      = BaseFontSize,
                        Foreground = TextBrush
                    };
                    if (!ctrl.HasError)
                    {
                        target.Add(new InlineUIContainer(ctrl)
                            { BaselineAlignment = BaselineAlignment.Center });
                        break;
                    }
                }
                catch { }
                target.Add(new Run($"${miLatex}$") { FontFamily = MonoFont, FontSize = 12, Foreground = AccentBrush });
                break;

            case HtmlInline:
                break; // drop raw HTML

            case ContainerInline ci2:
                foreach (var child in ci2) AddInlines(target, child);
                break;

            default:
                target.Add(new Run(inline.ToString() ?? string.Empty));
                break;
        }
    }

    // ── Fenced-content extraction ─────────────────────────────────────────

    /// <summary>
    /// Extracts the content lines of a fenced code block from the raw markdown
    /// (strips the opening and closing fence lines).  Falls back to
    /// <see cref="FencedCodeBlock.Lines"/> if raw text is unavailable.
    /// </summary>
    private static string ExtractFencedContent(FencedCodeBlock fc, string rawMarkdown)
    {
        if (!string.IsNullOrWhiteSpace(rawMarkdown))
        {
            var lines = rawMarkdown.Split('\n');
            // lines[0] = opening fence (```lang), lines[^1] = closing fence (```)
            if (lines.Length > 2)
                return string.Join('\n', lines[1..^1]).Trim();
            if (lines.Length == 2)
                return string.Empty;
        }
        return fc.Lines.ToString().Trim();
    }
}
