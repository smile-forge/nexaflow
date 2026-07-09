using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footers;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMath.Controls;
using MdBlock        = Markdig.Syntax.Block;
using MdFigure       = Markdig.Extensions.Figures.Figure;
using MdFigureCaption= Markdig.Extensions.Figures.FigureCaption;
using MdInline       = Markdig.Syntax.Inlines.Inline;
using MdTable        = Markdig.Extensions.Tables.Table;
using MdTableCell    = Markdig.Extensions.Tables.TableCell;
using MdTableRow     = Markdig.Extensions.Tables.TableRow;
using TableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using WpfInline      = System.Windows.Documents.Inline;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Converts a single Markdig <see cref="MdBlock"/> into a WPF
/// <see cref="FrameworkElement"/>.  Pure rendering — no editor coupling.
///
/// Colours and the link-navigation hook come from a
/// <see cref="MarkdownRenderContext"/> (a <see cref="MarkdownPalette"/> converts
/// implicitly; defaults to <see cref="MarkdownPalette.Dark"/>). Fonts and sizes
/// are theme-independent.
/// </summary>
public static class BlockRenderer
{
    // ── Theme-independent typography ──────────────────────────────────────

    internal static readonly FontFamily BodyFont = new("Segoe UI");
    internal static readonly FontFamily MonoFont = new("Consolas, Courier New");
    internal const double BaseFontSize = 13.5;

    // ── Public entry point ────────────────────────────────────────────────

    /// <param name="rawMarkdown">
    /// The raw markdown source for this block.  Required for accurate math
    /// formula extraction; ignored by all other block types.
    /// </param>
    /// <param name="context">Colours + link hook; defaults to <see cref="MarkdownPalette.Dark"/>.</param>
    public static FrameworkElement Render(MdBlock block, string rawMarkdown = "", MarkdownRenderContext? context = null)
    {
        var ctx = context ?? (MarkdownRenderContext)MarkdownPalette.FromTheme();
        var p   = ctx.Palette;
        try
        {
            return block switch
            {
                HeadingBlock       hb  => RenderHeading(hb, ctx),
                ParagraphBlock     pb  => RenderParagraph(pb, ctx),
                ThematicBreakBlock     => RenderHr(ctx),
                // YAML front matter (--- … ---) — document metadata, not content; not rendered
                // (matches how Markdig's HTML renderer ignores it). Extends CodeBlock, so match first.
                Markdig.Extensions.Yaml.YamlFrontMatterBlock => RenderNothing(),
                // AlertBlock extends QuoteBlock — must match first
                AlertBlock         ab  => RenderAlert(ab, ctx),
                QuoteBlock         qb  => RenderBlockQuote(qb, ctx),
                ListBlock          lb  => RenderList(lb, ctx),
                // MathBlock extends FencedCodeBlock — must match first
                MathBlock          mb  => RenderMathBlock(mb, rawMarkdown, ctx),
                // Diagram blocks: check Info before falling through to generic code
                FencedCodeBlock    fc when DiagramRenderer.IsDiagramLanguage(fc.Info)
                                       => DiagramRenderer.Render(fc.Info!, ExtractFencedContent(fc, rawMarkdown), p, ctx.OnNavigate),
                FencedCodeBlock    fc  => RenderCode(fc.Lines.ToString(), ctx),
                CodeBlock          cb  => RenderCode(cb.Lines.ToString(), ctx),
                MdTable            t   => RenderTable(t, ctx),
                DefinitionList     dl  => RenderDefinitionList(dl, ctx),
                DefinitionTerm     dt  => RenderDefinitionTerm(dt, ctx),
                DefinitionItem     di  => RenderDefinitionItem(di, ctx),
                MdFigure           fig => RenderFigure(fig, ctx),
                MdFigureCaption    fc2 => RenderFigureCaption(fc2, ctx),
                FooterBlock        fb  => RenderFooter(fb, ctx),
                _                      => RenderFallback(block, ctx)
            };
        }
        catch
        {
            return new TextBlock
            {
                Text         = block.ToString() ?? string.Empty,
                Foreground   = p.TextMuted,
                FontSize     = BaseFontSize,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    // ── Headings ──────────────────────────────────────────────────────────

    private static FrameworkElement RenderHeading(HeadingBlock hb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        double[] sizes = [28, 22, 18, 16, 14.5, BaseFontSize];
        var tb = MakeTextBlock(p);
        tb.FontSize   = sizes[Math.Clamp(hb.Level - 1, 0, 5)];
        tb.FontWeight = FontWeights.Bold;
        tb.Foreground = p.Heading;
        tb.Margin     = new Thickness(0, hb.Level == 1 ? 14 : 10, 0, 4);

        if (hb.Inline is not null)
            foreach (var inline in hb.Inline)
                AddInlines(tb.Inlines, inline, ctx);

        if (hb.Level > 2) return tb;

        var stack = new StackPanel();
        stack.Children.Add(tb);
        stack.Children.Add(new Border
        {
            Height     = 1,
            Background = p.Hr,
            Margin     = new Thickness(0, 3, 0, 8)
        });
        return stack;
    }

    // ── Paragraph ─────────────────────────────────────────────────────────

    private static FrameworkElement RenderParagraph(ParagraphBlock pb, MarkdownRenderContext ctx)
    {
        var tb = MakeTextBlock(ctx.Palette);
        tb.Margin = new Thickness(0, 4, 0, 8);

        if (pb.Inline is not null)
            foreach (var inline in pb.Inline)
                AddInlines(tb.Inlines, inline, ctx);

        return tb;
    }

    // ── Thematic break ────────────────────────────────────────────────────

    private static FrameworkElement RenderHr(MarkdownRenderContext ctx) =>
        new Border { Height = 1, Background = ctx.Palette.Hr, Margin = new Thickness(0, 10, 0, 10) };

    // ── Nothing (suppressed blocks: YAML front matter) ────────────────────

    /// <summary>A zero-size, collapsed placeholder for blocks that are parsed but deliberately not
    /// drawn (e.g. YAML front matter). Keeps the single-element render contract without taking layout space.</summary>
    private static FrameworkElement RenderNothing() =>
        new Grid { Visibility = Visibility.Collapsed, Height = 0, Margin = new Thickness(0) };

    // ── Block-quote ───────────────────────────────────────────────────────

    private static FrameworkElement RenderBlockQuote(QuoteBlock qb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var inner = new StackPanel { Margin = new Thickness(12, 6, 12, 6) };
        foreach (var child in qb)
            inner.Children.Add(Render(child, "", ctx));

        return new Border
        {
            Background      = p.QuoteBg,
            BorderBrush     = p.Accent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Margin          = new Thickness(0, 4, 0, 8),
            Child           = inner
        };
    }

    // ── Alert blocks (> [!NOTE] / [!TIP] / [!IMPORTANT] / [!WARNING] / [!CAUTION]) ──

    private static FrameworkElement RenderAlert(AlertBlock ab, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var (accent, label) = AlertStyle(ab.Kind.ToString(), p);

        var inner = new StackPanel { Margin = new Thickness(12, 6, 12, 8) };
        inner.Children.Add(new TextBlock
        {
            Text       = label,
            FontFamily = BodyFont,
            FontSize   = BaseFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            Margin     = new Thickness(0, 0, 0, 4),
        });

        foreach (var child in ab)
            inner.Children.Add(Render(child, "", ctx));

        return new Border
        {
            Background      = p.QuoteBg,
            BorderBrush     = accent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Margin          = new Thickness(0, 4, 0, 8),
            Child           = inner,
        };
    }

    /// <summary>Maps an alert kind to its accent brush + title-cased label. The five GitHub kinds
    /// get distinct semantic colours; any other kind falls back to the accent colour.</summary>
    private static (Brush accent, string label) AlertStyle(string kind, MarkdownPalette p) =>
        kind.Trim().ToUpperInvariant() switch
        {
            "NOTE"      => (p.Accent,   "Note"),
            "TIP"       => (p.Success,  "Tip"),
            "IMPORTANT" => (p.Important, "Important"),
            "WARNING"   => (p.Warning,  "Warning"),
            "CAUTION"   => (p.Danger,   "Caution"),
            _           => (p.Accent,   TitleCase(kind.Trim())),
        };

    private static string TitleCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    // ── Lists (including list-extras: alpha, roman) ───────────────────────

    private static FrameworkElement RenderList(ListBlock lb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
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
                Foreground        = p.TextMuted,
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
                content.Children.Add(Render(child, "", ctx));

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

    private static FrameworkElement RenderCode(string text, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        return new Border
        {
            Background      = p.CodeBg,
            BorderBrush     = p.CodeBorder,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12, 8, 12, 8),
            Margin          = new Thickness(0, 4, 0, 10),
            Child           = new TextBlock
            {
                Text         = text.TrimEnd('\n'),
                FontFamily   = MonoFont,
                FontSize     = 12,
                Foreground   = p.Text,
                TextWrapping = TextWrapping.NoWrap
            }
        };
    }

    // ── Table (pipe tables + grid tables) ────────────────────────────────

    private static FrameworkElement RenderTable(MdTable table, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
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

                // A single-paragraph cell (every pipe-table cell, most grid cells) keeps the styled,
                // aligned text fast-path. Cells with block content (grid tables: lists, multiple
                // paragraphs) render every child block so nothing is silently dropped.
                FrameworkElement content;
                if (cell.Count == 1 && cell[0] is ParagraphBlock pb)
                {
                    var tb = MakeTextBlock(p);
                    tb.FontWeight    = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
                    tb.Foreground    = row.IsHeader ? p.Heading : p.Text;
                    tb.FontSize      = row.IsHeader ? 13 : BaseFontSize;
                    tb.Padding       = new Thickness(8, 5, 8, 5);
                    tb.TextAlignment = align switch
                    {
                        TableColumnAlign.Right  => TextAlignment.Right,
                        TableColumnAlign.Center => TextAlignment.Center,
                        _                       => TextAlignment.Left
                    };
                    if (pb.Inline is not null)
                        foreach (var inl in pb.Inline)
                            AddInlines(tb.Inlines, inl, ctx);
                    content = tb;
                }
                else
                {
                    var sp = new StackPanel { Margin = new Thickness(8, 5, 8, 5) };
                    foreach (var child in cell)
                        sp.Children.Add(Render(child, "", ctx));
                    content = sp;
                }

                var bg = row.IsHeader
                    ? p.TableHeaderBg
                    : (rowIdx % 2 == 1 ? p.TableAltRowBg : Brushes.Transparent);

                var cellBorder = new Border
                {
                    Background      = bg,
                    BorderBrush     = p.TableBorder,
                    BorderThickness = new Thickness(1),
                    Child           = content
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

    private static FrameworkElement RenderDefinitionList(DefinitionList dl, MarkdownRenderContext ctx)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        foreach (var child in dl)
            stack.Children.Add(Render(child, "", ctx));
        return stack;
    }

    private static FrameworkElement RenderDefinitionTerm(DefinitionTerm dt, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var tb = MakeTextBlock(p);
        tb.FontWeight = FontWeights.SemiBold;
        tb.Foreground = p.DefTerm;
        tb.Margin     = new Thickness(0, 6, 0, 2);

        if (dt.Inline is not null)
            foreach (var inl in dt.Inline)
                AddInlines(tb.Inlines, inl, ctx);

        return tb;
    }

    // ── Figure ────────────────────────────────────────────────────────────

    private static FrameworkElement RenderFigure(MdFigure fig, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var inner = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        foreach (var child in fig)
            inner.Children.Add(Render(child, "", ctx));

        return new Border
        {
            Background      = p.FigureBg,
            BorderBrush     = p.FigureBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 8, 0, 8),
            Child           = inner
        };
    }

    private static FrameworkElement RenderFigureCaption(MdFigureCaption fc, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var tb = MakeTextBlock(p);
        tb.FontStyle  = FontStyles.Italic;
        tb.FontSize   = 12;
        tb.Foreground = p.TextMuted;
        tb.TextAlignment = TextAlignment.Center;
        tb.Margin     = new Thickness(0, 2, 0, 6);

        if (fc.Inline is not null)
            foreach (var inl in fc.Inline)
                AddInlines(tb.Inlines, inl, ctx);

        return tb;
    }

    // ── Footer ────────────────────────────────────────────────────────────

    private static FrameworkElement RenderFooter(FooterBlock fb, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        var inner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var child in fb)
        {
            var rendered = Render(child, "", ctx);
            if (rendered is TextBlock ftb)
            {
                ftb.FontSize  = 12;
                ftb.Foreground = p.TextMuted;
            }
            inner.Children.Add(rendered);
        }

        var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };
        stack.Children.Add(new Border { Height = 1, Background = p.Hr });
        stack.Children.Add(new Border
        {
            Background = p.FooterBg,
            Padding    = new Thickness(0, 4, 0, 4),
            Child      = inner
        });
        return stack;
    }

    // ── Math block ($$ ... $$) ────────────────────────────────────────────

    private static FrameworkElement RenderMathBlock(MathBlock mb, string rawMarkdown, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
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
                    Foreground          = p.Text,
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
            Background          = p.CodeBg,
            BorderBrush         = p.Accent,
            BorderThickness     = new Thickness(2),
            Padding             = new Thickness(12, 8, 12, 8),
            Margin              = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text         = string.IsNullOrWhiteSpace(latex) ? "(empty math block)" : $"$$\n{latex}\n$$",
                FontFamily   = MonoFont,
                FontSize     = 12,
                Foreground   = p.Accent,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    // ── Definition item (<dd>) ────────────────────────────────────────────

    private static FrameworkElement RenderDefinitionItem(DefinitionItem di, MarkdownRenderContext ctx)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 0, 0, 4) };
        foreach (var child in di)
            stack.Children.Add(Render(child, "", ctx));
        return stack;
    }

    // ── Fallback ──────────────────────────────────────────────────────────

    private static FrameworkElement RenderFallback(MdBlock block, MarkdownRenderContext ctx) =>
        new TextBlock
        {
            Text         = block.ToString() ?? string.Empty,
            Foreground   = ctx.Palette.TextMuted,
            FontSize     = BaseFontSize,
            TextWrapping = TextWrapping.Wrap
        };

    // ── TextBlock factory ─────────────────────────────────────────────────

    private static TextBlock MakeTextBlock(MarkdownPalette p) =>
        new()
        {
            Foreground   = p.Text,
            FontSize     = BaseFontSize,
            FontFamily   = BodyFont,
            TextWrapping = TextWrapping.Wrap,
            LineHeight   = double.NaN
        };

    // ── Inline rendering ──────────────────────────────────────────────────

    internal static void AddInlines(InlineCollection target, MdInline inline, MarkdownRenderContext? context = null)
    {
        var ctx = context ?? (MarkdownRenderContext)MarkdownPalette.FromTheme();
        var p   = ctx.Palette;
        switch (inline)
        {
            case TaskList tl:
                // Render task list checkbox
                target.Add(new Run(tl.Checked ? "☑ " : "☐ ")
                {
                    Foreground = tl.Checked ? p.Accent : p.TextMuted
                });
                break;

            case LiteralInline li:
                target.Add(new Run(li.Content.ToString()) { Tag = li.Span });
                break;

            case HtmlEntityInline he:
                // &amp; / &#9731; — Markdig keeps these as a distinct inline carrying the decoded text.
                target.Add(new Run(he.Transcoded.ToString()) { Tag = he.Span });
                break;

            case EmphasisInline ei when ei.DelimiterChar == '"':
                // Citation (""text"") — Markdig's UseCitations delimiter; rendered as smaller raised text in a distinct colour
                var citeSpan = new Span
                {
                    Foreground = p.Citation,
                    FontSize   = BaseFontSize * 0.85,
                    BaselineAlignment = BaselineAlignment.Superscript
                };
                foreach (var child in ei) AddInlines(citeSpan.Inlines, child, ctx);
                target.Add(citeSpan);
                break;

            case EmphasisInline ei when ei.DelimiterChar is '~' or '^' or '=' or '+':
                // Emphasis-extras (UseEmphasisExtras): strikethrough ~~x~~, subscript ~x~,
                // superscript ^x^, marked ==x==, inserted ++x++.
                Span extraSpan = ei.DelimiterChar switch
                {
                    '~' when ei.DelimiterCount >= 2 => new Span { TextDecorations = TextDecorations.Strikethrough },
                    '~'                             => new Span { BaselineAlignment = BaselineAlignment.Subscript,   FontSize = BaseFontSize * 0.75 },
                    '^'                             => new Span { BaselineAlignment = BaselineAlignment.Superscript, FontSize = BaseFontSize * 0.75 },
                    '='                             => new Span { Background = p.Marked },
                    _                               => new Span { TextDecorations = TextDecorations.Underline }, // ++inserted++
                };
                foreach (var child in ei) AddInlines(extraSpan.Inlines, child, ctx);
                target.Add(extraSpan);
                break;

            case EmphasisInline ei:
                // Bold / italic (and strong)
                Span emphSpan;
                if (ei.DelimiterCount >= 2)
                    emphSpan = new Span { FontWeight = FontWeights.Bold };
                else
                    emphSpan = new Span { FontStyle = FontStyles.Italic };
                foreach (var child in ei) AddInlines(emphSpan.Inlines, child, ctx);
                target.Add(emphSpan);
                break;

            case CodeInline ci:
                target.Add(new Run(ci.Content)
                {
                    FontFamily = MonoFont,
                    FontSize   = 12,
                    Background = p.CodeBg,
                    Foreground = p.Accent,
                    Tag        = ci.Span
                });
                break;

            case LinkInline link when link.IsImage:
                if (TryRenderImage(link.Url, ctx) is { } image)
                    target.Add(image);
                else
                    // Unresolved/remote image → show its alt text so nothing is silently lost.
                    foreach (var child in link) AddInlines(target, child, ctx);
                break;

            case LinkInline link when !link.IsImage:
                var hyper = NewHyperlink(link.Url, ctx);
                foreach (var child in link) AddInlines(hyper.Inlines, child, ctx);
                target.Add(hyper);
                break;

            case AutolinkInline auto:
                // CommonMark <https://…> / <user@host> — a distinct inline from LinkInline; the URL is also the label.
                var autoLink = NewHyperlink(auto.IsEmail ? $"mailto:{auto.Url}" : auto.Url, ctx);
                autoLink.Inlines.Add(new Run(auto.Url) { Tag = auto.Span });
                target.Add(autoLink);
                break;

            case AbbreviationInline abbr:
                // *[HTML]: HyperText… — an occurrence of a defined abbreviation; the label shows
                // with a dotted underline and the definition appears as a hover tooltip.
                target.Add(MakeAbbreviation(abbr, p));
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
                        Foreground = p.Text
                    };
                    if (!ctrl.HasError)
                    {
                        target.Add(new InlineUIContainer(ctrl)
                            { BaselineAlignment = BaselineAlignment.Center });
                        break;
                    }
                }
                catch { }
                target.Add(new Run($"${miLatex}$") { FontFamily = MonoFont, FontSize = 12, Foreground = p.Accent });
                break;

            case HtmlInline:
                break; // drop raw HTML

            case ContainerInline ci2:
                foreach (var child in ci2) AddInlines(target, child, ctx);
                break;

            default:
                target.Add(new Run(inline.ToString() ?? string.Empty));
                break;
        }
    }

    /// <summary>
    /// Builds a navigable <see cref="Hyperlink"/> (shared by inline links and autolinks): the in-app
    /// <see cref="MarkdownRenderContext.OnNavigate"/> hook wins, otherwise the OS browser opens the URL.
    /// </summary>
    private static Hyperlink NewHyperlink(string? url, MarkdownRenderContext ctx)
    {
        var hyper = new Hyperlink
        {
            Foreground      = ctx.Palette.Accent,
            TextDecorations = TextDecorations.Underline,
            Cursor          = System.Windows.Input.Cursors.Hand,   // signal the link is clickable
            NavigateUri     = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null,
            Tag             = url,   // raw source URL — NavigateUri normalizes (trailing slash, casing),
                                     // and MarkdownInlineSerializer needs the exact original to round-trip
        };
        var onNavigate = ctx.OnNavigate;
        hyper.RequestNavigate += (_, e) =>
        {
            var nav = e.Uri.ToString();
            e.Handled = true;
            // In-app handler wins; otherwise fall back to the OS browser.
            if (onNavigate is not null && onNavigate(nav)) return;
            try { Process.Start(new ProcessStartInfo(nav) { UseShellExecute = true }); }
            catch { }
        };
        return hyper;
    }

    /// <summary>
    /// Builds the inline for an <see cref="AbbreviationInline"/>: the abbreviation label drawn with a
    /// dotted underline + help cursor, carrying the definition as a hover tooltip. The tooltip is an
    /// explicit <see cref="TextBlock"/> (a bare string would inherit the host's text alignment).
    /// </summary>
    private static WpfInline MakeAbbreviation(AbbreviationInline abbr, MarkdownPalette p)
    {
        var title = abbr.Abbreviation.Text.ToString().Trim();

        var pen = new Pen(p.TextMuted, 1) { DashStyle = new DashStyle([1, 2], 0) };
        pen.Freeze();
        var decorations = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline, Pen = pen, PenOffset = 2 }
        };
        decorations.Freeze();

        return new Run(abbr.Abbreviation.Label)
        {
            TextDecorations = decorations,
            Cursor          = System.Windows.Input.Cursors.Help,
            Tag             = abbr.Span,
            ToolTip         = string.IsNullOrEmpty(title)
                ? null
                : new TextBlock { Text = title, TextAlignment = TextAlignment.Left }
        };
    }

    // ── Image rendering (local files only) ────────────────────────────────

    /// <summary>
    /// Builds an inline image for a markdown <c>![](src)</c> when <paramref name="src"/> resolves
    /// to an existing LOCAL file — an absolute path, a <c>file:</c> URI, or a name relative to
    /// <see cref="MarkdownRenderContext.BaseDirectory"/>. Remote <c>http(s)</c> sources are never
    /// fetched (returns null so the caller shows alt text). The bitmap is loaded with
    /// <see cref="BitmapCacheOption.OnLoad"/> so the renderer holds no file handle.
    /// </summary>
    private static WpfInline? TryRenderImage(string? src, MarkdownRenderContext ctx)
    {
        var path = ResolveLocalImagePath(src, ctx.BaseDirectory);
        if (path is null) return null;

        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource    = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var img = new Image
            {
                Source           = bmp,
                Stretch          = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth         = 600,
                MaxHeight        = 600,
                Margin           = new Thickness(0, 2, 0, 2),
            };
            return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Bottom };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a markdown image src to a local file path that exists, or null. Handles
    /// <c>file:</c> URIs, rooted paths, and names relative to <paramref name="baseDir"/>.
    /// Returns null for remote schemes and missing files.
    /// </summary>
    private static string? ResolveLocalImagePath(string? src, string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;

        if (Uri.TryCreate(src, UriKind.Absolute, out var abs))
        {
            if (abs.IsFile) return File.Exists(abs.LocalPath) ? abs.LocalPath : null;
            return null;   // http(s)/data/etc. — not a local file
        }

        var candidate = src;
        if (!Path.IsPathRooted(candidate) && !string.IsNullOrEmpty(baseDir))
            candidate = Path.Combine(baseDir, Uri.UnescapeDataString(src));

        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
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
