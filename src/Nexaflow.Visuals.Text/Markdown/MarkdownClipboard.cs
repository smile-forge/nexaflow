using System.Text;
using System.Windows;
using System.Windows.Documents;
using Markdig.Syntax;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Places an AI-response selection on the clipboard in three formats so the
/// destination can pick whichever it understands:
/// <list type="bullet">
///   <item>plain text (CF_UNICODETEXT/CF_TEXT) — stripped, for plain destinations;</item>
///   <item>HTML (CF_HTML) — rendered, for rich destinations (Word, Outlook, mail);</item>
///   <item><c>Markdown</c> (custom) — markdown source, for markdown-aware tools.</item>
/// </list>
/// When the selection is empty or spans the whole document the full source is
/// used; otherwise the markdown is sliced from the source using the
/// <see cref="SourceSpan"/> tags that <see cref="MarkdownFlowDocument"/> attaches
/// to leaf runs.
/// </summary>
public static class MarkdownClipboard
{
    /// <summary>Custom clipboard format carrying the raw markdown source.</summary>
    public const string MarkdownFormat = "Markdown";

    /// <summary>
    /// Extracts the best markdown representation from a clipboard / drag payload:
    /// the custom <see cref="MarkdownFormat"/> if present, else HTML converted to
    /// markdown, else plain text. Returns null if the payload carries none of these.
    /// </summary>
    public static string? ReadBestMarkdown(IDataObject? data)
    {
        if (data is null) return null;
        try
        {
            if (data.GetDataPresent(MarkdownFormat) &&
                data.GetData(MarkdownFormat) is string md && !string.IsNullOrEmpty(md))
                return md;
        }
        catch { /* ignore malformed format */ }

        try
        {
            if (data.GetDataPresent(DataFormats.Html) &&
                data.GetData(DataFormats.Html) is string html && !string.IsNullOrWhiteSpace(html))
            {
                var converted = HtmlToMarkdown.ConvertClipboardHtml(html);
                if (!string.IsNullOrWhiteSpace(converted)) return converted;
            }
        }
        catch { /* fall through to plain text */ }

        return ReadPlainText(data);
    }

    /// <summary>
    /// The clipboard's plain text, ignoring its richer flavours.
    /// <para>
    /// For a surface whose content is source rather than prose — a formula — this is the only honest
    /// reading. Converting the HTML flavour to markdown is meaningful for text and destructive for
    /// code: it is where a copied <c>\[\sqrt{x^2+1}\]</c> arrives wearing typographic quotes the page
    /// never had, because something in the markup was styled as a quotation. What was copied is what
    /// should be typed.
    /// </para>
    /// </summary>
    public static string? ReadPlainText(IDataObject? data)
    {
        if (data is null) return null;

        if (data.GetDataPresent(DataFormats.UnicodeText))
            return data.GetData(DataFormats.UnicodeText) as string;
        if (data.GetDataPresent(DataFormats.Text))
            return data.GetData(DataFormats.Text) as string;
        return null;
    }

    public static void CopySelection(TextSelection selection, FlowDocument document, string source)
    {
        bool whole = selection.IsEmpty || SpansWholeDocument(selection, document);

        string markdown = whole
            ? source
            : (SliceFromSelection(selection, source) ?? selection.Text);

        string plain = whole ? ToPlainText(source) : selection.Text;

        string html;
        try   { html = Markdig.Markdown.ToHtml(markdown, MarkdownPipelineFactory.Default); }
        catch { html = System.Net.WebUtility.HtmlEncode(plain); }

        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, plain);
        data.SetData(DataFormats.Text,        plain);
        data.SetData(MarkdownFormat,          markdown);
        try { data.SetData(DataFormats.Html, WrapCfHtml(html)); } catch { /* HTML optional */ }

        Clipboard.SetDataObject(data, copy: true);
    }

    private static string ToPlainText(string source)
    {
        try   { return Markdig.Markdown.ToPlainText(source, MarkdownPipelineFactory.Default); }
        catch { return source; }
    }

    // ── Selection → source range ────────────────────────────────────────────

    private static bool SpansWholeDocument(TextSelection sel, FlowDocument doc)
        => sel.Start.CompareTo(doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0
        && sel.End.CompareTo(doc.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) >= 0;

    private static string? SliceFromSelection(TextSelection sel, string source)
    {
        int min = int.MaxValue, max = -1;
        foreach (var run in RunsIn(sel))
        {
            if (run.Tag is not SourceSpan span || span.IsEmpty) continue;
            if (span.Start < min) min = span.Start;
            if (span.End   > max) max = span.End;
        }
        if (max < 0 || min > max) return null;

        min = Math.Clamp(min, 0, source.Length);
        int end = Math.Clamp(max + 1, min, source.Length);   // SourceSpan.End is inclusive
        return source[min..end];
    }

    /// <summary>Leaf runs whose text range intersects the selection.</summary>
    private static IEnumerable<Run> RunsIn(TextSelection sel)
    {
        // The run the selection begins inside (its ElementStart is before sel.Start,
        // so the forward walk below would miss it).
        if (sel.Start.Parent is Run startRun)
            yield return startRun;

        TextPointer? p = sel.Start;
        var end = sel.End;
        while (p is not null && p.CompareTo(end) < 0)
        {
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
                && p.GetAdjacentElement(LogicalDirection.Forward) is Run r)
                yield return r;
            p = p.GetNextContextPosition(LogicalDirection.Forward);
        }
    }

    // ── CF_HTML packaging ────────────────────────────────────────────────────

    private static string WrapCfHtml(string fragment)
    {
        const string headerFmt =
            "Version:0.9\r\nStartHTML:{0:00000000}\r\nEndHTML:{1:00000000}\r\n" +
            "StartFragment:{2:00000000}\r\nEndFragment:{3:00000000}\r\n";
        const string pre  = "<html><body><!--StartFragment-->";
        const string post = "<!--EndFragment--></body></html>";

        var enc       = Encoding.UTF8;
        int headerLen = enc.GetByteCount(string.Format(headerFmt, 0, 0, 0, 0));
        int startFrag = headerLen + enc.GetByteCount(pre);
        int endFrag   = startFrag + enc.GetByteCount(fragment);
        int endHtml   = endFrag   + enc.GetByteCount(post);

        return string.Format(headerFmt, headerLen, endHtml, startFrag, endFrag) + pre + fragment + post;
    }
}
