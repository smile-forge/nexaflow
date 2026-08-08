using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>What reading a PDF for text produced.</summary>
/// <param name="Text">The text, possibly empty. Empty is a real answer: an image-only scan has none.</param>
/// <param name="Truncated">True when the byte budget ran out before the last page was read.</param>
internal readonly record struct PdfText(string Text, bool Truncated);

/// <summary>
/// Turns a PDF into plain text for searching. Shell-free and synchronous by design — the callers decide
/// where it runs and what they do with the result.
/// </summary>
internal static class PdfTextReader
{
    /// <summary>Approximate bytes per char, for charging UTF-16 text against a byte budget.</summary>
    private const int BytesPerChar = 2;

    /// <summary>
    /// Reads text from an already-opened document, stopping once <paramref name="maxBytes"/> is reached.
    /// <para>
    /// Words rather than <see cref="Page.Text"/>: the raw property concatenates glyphs in content-stream
    /// order with no spacing, so "the lost dog" arrives as "thelostdog" and a whole-word search — which is
    /// what Nexaflow's search does — matches nothing in it. <c>GetWords</c> runs PdfPig's word extractor and
    /// gives back separable words, which is the entire difference between a searchable document and a
    /// useless one.
    /// </para>
    /// </summary>
    public static PdfText Read(PdfDocument document, long maxBytes, bool includeMetadata, CancellationToken ct)
    {
        var budget = maxBytes <= 0 ? 0 : maxBytes / BytesPerChar;
        var sb = new StringBuilder();

        if (includeMetadata) AppendMetadata(document, sb, ct);

        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();
            if (sb.Length >= budget) return new PdfText(sb.ToString(), Truncated: true);

            Page page;
            try { page = document.GetPage(pageNumber); }
            catch { continue; }   // one unreadable page shouldn't cost the other 200

            try
            {
                foreach (var word in page.GetWords())
                {
                    if (sb.Length >= budget) return new PdfText(sb.ToString(), Truncated: true);
                    Append(sb, word.Text);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a page whose fonts defeat the extractor still leaves the rest searchable */ }

            sb.Append('\n');
        }

        return new PdfText(sb.ToString(), Truncated: false);
    }

    // Title, author, subject and keywords make a document findable when its body doesn't mention its own
    // subject; bookmark titles carry a table of contents that appears nowhere in the page text; form field
    // values are the only place a filled-in form's answers exist. Each source is guarded separately so a
    // malformed outline tree or form dictionary can't cost us the page text that follows.
    private static void AppendMetadata(PdfDocument document, StringBuilder sb, CancellationToken ct)
    {
        try
        {
            var info = document.Information;
            Append(sb, info.Title);
            Append(sb, info.Author);
            Append(sb, info.Subject);
            Append(sb, info.Keywords);
        }
        catch { }

        ct.ThrowIfCancellationRequested();

        try
        {
            if (document.TryGetBookmarks(out var bookmarks) && bookmarks is not null)
                foreach (var node in Flatten(bookmarks.Roots))
                    Append(sb, node.Title);
        }
        catch { }

        ct.ThrowIfCancellationRequested();

        try
        {
            if (document.TryGetForm(out var form) && form is not null)
                foreach (var field in Flatten(form.Fields))
                    AppendFieldValue(sb, field);
        }
        catch { }

        if (sb.Length > 0) sb.Append('\n');
    }

    private static IEnumerable<BookmarkNode> Flatten(IEnumerable<BookmarkNode>? nodes)
    {
        if (nodes is null) yield break;
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private static IEnumerable<AcroFieldBase> Flatten(IEnumerable<AcroFieldBase>? fields)
    {
        if (fields is null) yield break;
        foreach (var field in fields)
        {
            yield return field;
            if (field is AcroNonTerminalField parent)
                foreach (var child in Flatten(parent.Children)) yield return child;
        }
    }

    // Only what a person actually entered. Field *names* are deliberately skipped — they are machine-ish
    // ("topmostSubform[0].Page1[0].f1_01"), so indexing them buys nothing and produces false hits.
    private static void AppendFieldValue(StringBuilder sb, AcroFieldBase field)
    {
        switch (field)
        {
            case AcroTextField text:
                Append(sb, text.Value);
                break;
            case AcroComboBoxField combo:
                foreach (var selected in combo.SelectedOptions) Append(sb, selected);
                break;
            case AcroListBoxField list:
                foreach (var selected in list.SelectedOptions) Append(sb, selected);
                break;
        }
    }

    // One space between everything. Word boundaries are all the search needs, and inventing line structure
    // from glyph positions would be guesswork that changes what matches.
    private static void Append(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) sb.Append(' ');
        sb.Append(value);
    }
}
