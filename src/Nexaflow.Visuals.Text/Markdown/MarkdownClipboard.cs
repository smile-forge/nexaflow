using System;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Nexaflow.Interaction;
using Nexaflow.IO.Documents;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Common.Dropping;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Places an AI-response selection on the clipboard in three formats so the
/// destination can pick whichever it understands:
/// <list type="bullet">
///   <item>plain text (CF_UNICODETEXT/CF_TEXT) — the words as they are shown, for plain destinations;</item>
///   <item>HTML (CF_HTML) — the stretch as it is shown, for rich destinations (Word, Outlook, mail);</item>
///   <item><c>Markdown</c> (custom) — markdown source, for markdown-aware tools.</item>
/// </list>
/// Where nothing is chosen the whole document is copied; otherwise the characters chosen.
/// </summary>
public static class MarkdownClipboard
{
    /// <summary>Custom clipboard format carrying the raw markdown source.</summary>
    public const string MarkdownFormat = "Markdown";

    /// <summary>The words of a copy: what a reader sees, a blank line between blocks.</summary>
    private static readonly TextDocumentWriter Text = new();

    /// <summary>The marked-up text of a copy, each heading with the name a link reaches it by.</summary>
    private static readonly HtmlDocumentWriter Html = new(new HtmlWriting { Anchors = true });

    /// <summary>The words a reader sees in <paramref name="copy"/>, with the marks taken off — what a plain destination is pasted.</summary>
    public static string PlainWords(ContentCopy copy) => copy.Shown is { } shown ? Text.Written(shown) : copy.Markdown;

    /// <summary>
    /// <paramref name="copy"/> marked up, as a clipboard carries HTML — for a rich destination: Word, Outlook, mail. Empty where nothing
    /// in it is marked up.
    /// </summary>
    public static string MarkedUp(ContentCopy copy) => copy.Shown is { } shown ? WrapCfHtml(Html.Written(shown)) : string.Empty;

    /// <summary>
    /// Extracts the best markdown representation from a clipboard or drag payload: the custom <see cref="MarkdownFormat"/> if
    /// present, else HTML converted to markdown, else plain text. Returns null if the payload carries none of these.
    /// </summary>
    public static string? ReadBestMarkdown(IDragPayload data)
    {
        if (data.Read(MarkdownFormat) is string { Length: > 0 } md) return md;

        try
        {
            if (data.Html() is { } html && !string.IsNullOrWhiteSpace(html))
            {
                var converted = HtmlToMarkdown.ConvertClipboardHtml(html);
                if (!string.IsNullOrWhiteSpace(converted)) return converted;
            }
        }
        catch { /* fall through to plain text */ }

        return ReadPlainText(data);
    }

    /// <summary>
    /// The payload's plain text, ignoring its richer flavours.
    /// <para>
    /// For a surface whose content is source rather than prose — a formula — this is the only honest
    /// reading. Converting the HTML flavour to markdown is meaningful for text and destructive for
    /// code: it is where a copied <c>\[\sqrt{x^2+1}\]</c> arrives wearing typographic quotes the page
    /// never had, because something in the markup was styled as a quotation. What was copied is what
    /// should be typed.
    /// </para>
    /// </summary>
    public static string? ReadPlainText(IDragPayload data) => data.Text();

    // ── Selection → source range ────────────────────────────────────────────

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

    /// <summary>What a copy that is a block of columns is marked with, as editors that pick out columns mark theirs.</summary>
    public const string ColumnFormat = "MSDEVColumnSelect";

    /// <summary>
    /// <paramref name="copy"/> as what goes on a clipboard: its words, its markdown as the app's own kind, its marked-up text, and
    /// — for a block that draws a picture — the picture, as PNG. Whatever it is pasted into takes the one it can use: a document
    /// takes the picture, an editor takes the markdown, and neither has to be asked which was meant.
    /// </summary>
    public static ComposedPayload Payload(ContentCopy copy)
    {
        var payload = ComposedPayload.OfText(PlainWords(copy)).With(MarkdownFormat, copy.Markdown);
        if (MarkedUp(copy) is { Length: > 0 } html) payload = payload.With(DragKinds.Html, html);
        if (copy.Picture is { } picture && Png(picture) is { } png) payload = payload.With(DragKinds.Image, png);
        if (copy.Column) payload = payload.With(ColumnFormat, string.Empty);
        return payload;
    }

    /// <summary>A picture drawn in this toolkit's medium, as PNG — null for one it holds no pixels of.</summary>
    private static byte[]? Png(LayoutPicture picture) => picture is WpfPicture { Source: BitmapSource bitmap } ? WpfPainter.Png(bitmap) : null;
}
