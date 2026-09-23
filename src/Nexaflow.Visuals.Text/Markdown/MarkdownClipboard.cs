using System;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Markdig.Syntax;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Latex;

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
    /// What a stretch of markdown amounts to in each of the ways a reader might want it pasted: as the
    /// markdown itself, as the words with the marks taken off, and as marked-up text.
    ///
    /// <para>
    /// <strong>Saying it is the renderer's; putting it anywhere is not.</strong> Which characters were chosen
    /// and what they mean is what this knows. A clipboard is the application's — shared with every other thing
    /// in the window, and subject to whatever the host has to say about what may leave it — so what comes back
    /// is handed over rather than set.
    /// </para>
    /// </summary>
    /// <param name="Markdown">The source, as written.</param>
    /// <param name="Text">The words a reader sees, with the marks taken off.</param>
    /// <param name="Html">The same, marked up — empty where it could not be.</param>
    public sealed record ContentCopy(string Markdown, string Text, string Html);

    /// <summary>
    /// What copying <paramref name="chosen"/> out of <paramref name="source"/> would put on a clipboard —
    /// the whole document where nothing is chosen.
    /// </summary>
    public static ContentCopy Copied(string source, (int Start, int Length)? chosen)
    {
        var markdown = chosen is { Length: > 0 } picked && picked.Start >= 0 && picked.Start + picked.Length <= source.Length
            ? source.Substring(picked.Start, picked.Length)
            : source;

        var plain = ToPlainText(markdown);
        string html;

        try { html = WrapCfHtml(Markdig.Markdown.ToHtml(markdown, MarkdownParser.Pipeline)); }
        catch { html = string.Empty; }

        return new ContentCopy(markdown, plain, html);
    }

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
        try   { html = Markdig.Markdown.ToHtml(markdown, MarkdownParser.Pipeline); }
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
        try   { return Markdig.Markdown.ToPlainText(source, MarkdownParser.Pipeline); }
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

    /// <summary>Pasted text as the formula it is meant to be: whatever said "this is maths" taken off,
    /// however many lines folded into one expression, and the ends trimmed. Its own method because every
    /// paste route needs it. The newline is trimmed because a copy almost always carries one and inside an
    /// expression a newline means a space — left on, it's invisible and the first backspace seems to do nothing.
    /// </summary>
    public static string AsFormula(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Undelimited(text).ReplaceLineEndings(" ").Trim();

    /// <summary>Environments that only say "what follows is maths" — the wrapper, never the formula.
    /// Deliberately a fixed list, not any <c>\begin{…}</c>: <c>matrix</c>, <c>cases</c> and <c>array</c> are
    /// also environments but ARE the formula, and stripping those would take a matrix apart.</summary>
    private static readonly string[] MathEnvironments =
        ["equation", "displaymath", "math", "align", "alignat", "gather", "multline", "eqnarray"];

    /// <summary>Takes the typesetting instructions off a pasted formula, leaving the formula. LaTeX copied
    /// from anywhere arrives wrapped in whatever said "this is maths" (<c>$$…$$</c>, <c>\[…\]</c>,
    /// <c>\begin{equation}…\end{equation}</c>); pasting into a formula, that has already been said, so
    /// keeping it hands the parser unknown commands and a red wave under the reader's own formula. Stripped
    /// repeatedly since wrappers nest; only a pair around the whole text counts, not one in the middle.
    /// </summary>
    private static string Undelimited(string text)
    {
        var trimmed = text.Trim();

        // Bounded rather than while(true) — a grammar this loose isn't worth trusting with an unbounded loop over user input.
        for (var pass = 0; pass < 8; pass++)
        {
            var stripped = StripOnce(trimmed);
            if (stripped == trimmed) return trimmed.Length == 0 ? text : trimmed;
            trimmed = stripped.Trim();
        }

        return trimmed;
    }

    /// <summary>A markdown code fence around the whole of it, taken off with any info string — the same
    /// kind of wrapper as <c>$$</c>. This is how LaTeX arrives from a browser: copying a formula shown as
    /// code carries an HTML <c>&lt;pre&gt;</c>, converted to a fenced block; left on, the backticks paste in
    /// as opening quotes in TeX. Only a fence opening the first line and closing the last counts — backticks
    /// elsewhere were typed.</summary>
    private static string? Unfenced(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 2) return null;

        var open = lines[0].TrimEnd();
        var fence = new string('`', open.Length - open.TrimStart('`').Length);
        if (fence.Length < 3) return null;

        // The info string is a language name, never code: ```latex is still a fence.
        if (open[fence.Length..].Trim().Contains('`')) return null;

        var last = lines.Length - 1;
        while (last > 0 && lines[last].Trim().Length == 0) last--;
        if (last == 0 || lines[last].Trim() != fence) return null;

        return string.Join("\n", lines[1..last]);
    }

    private static string StripOnce(string text)
    {
        if (Unfenced(text) is { } unfenced) return unfenced;

        (string Open, string Close)[] pairs = [("$$", "$$"), (@"\[", @"\]"), (@"\(", @"\)"), ("$", "$")];

        foreach (var (open, close) in pairs)
        {
            if (text.Length < open.Length + close.Length) continue;
            if (!text.StartsWith(open, StringComparison.Ordinal)) continue;
            if (!text.EndsWith(close, StringComparison.Ordinal)) continue;

            return text[open.Length..^close.Length];
        }

        foreach (var environment in MathEnvironments)
        foreach (var name in new[] { environment, environment + "*" })
        {
            var open = @"\begin{" + name + "}";
            var close = @"\end{" + name + "}";

            if (!text.StartsWith(open, StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.EndsWith(close, StringComparison.OrdinalIgnoreCase)) continue;

            return text[open.Length..^close.Length];
        }

        return text;
    }

    /// <summary>
    /// What a copy looks like to a clipboard: the markdown in a format of its own, the plain words, and the marked-up text.
    /// Built, not set — putting it on the clipboard is whoever holds the clipboard's to do.
    /// </summary>
    public static IDataObject Data(ContentCopy copy)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, copy.Text);
        data.SetData(DataFormats.Text, copy.Text);
        data.SetData(MarkdownFormat, copy.Markdown);
        if (copy.Html.Length > 0) data.SetData(DataFormats.Html, copy.Html);

        return data;
    }
}
