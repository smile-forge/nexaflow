using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>How a page's text is put in order.</summary>
internal enum PdfReadingOrder
{
    /// <summary>
    /// The order the words appear in the content stream. Cheap, and correct for most single-column pages,
    /// because a generator usually writes them in the order it laid them out.
    /// </summary>
    ContentStream,

    /// <summary>
    /// The order a person would read them, worked out from where they sit on the page: words are grouped
    /// into blocks by spatial clustering and the blocks are then ordered.
    /// <para>
    /// This is what a multi-column page needs. In content-stream order a two-column page interleaves — a
    /// line of column one, a line of column two — and the result reads as nonsense while looking like it
    /// ought to make sense. Costs a clustering pass over every word on the page, which is why it is not
    /// what a search sweep uses.
    /// </para>
    /// </summary>
    Layout,
}

/// <summary>What reading a PDF for text produced.</summary>
/// <param name="Text">The text, possibly empty. Empty is a real answer: an image-only scan has none.</param>
/// <param name="Truncated">True when the byte budget ran out before the last page was read.</param>
internal readonly record struct PdfText(string Text, bool Truncated);

/// <summary>One page's text, for callers that need to say <em>which</em> page something was on.</summary>
/// <param name="PageNumber">1-based page.</param>
/// <param name="Text">The page's words, possibly empty — an image-only scan page has none.</param>
/// <param name="Truncated">True when the byte budget ran out part-way through this page.</param>
internal readonly record struct PdfPageText(int PageNumber, string Text, bool Truncated);

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

            if (!AppendPage(document, pageNumber, sb, budget, PdfReadingOrder.ContentStream))
                return new PdfText(sb.ToString(), Truncated: true);

            sb.Append('\n');
        }

        return new PdfText(sb.ToString(), Truncated: false);
    }

    /// <summary>
    /// Reads pages <paramref name="firstPage"/>..<paramref name="lastPage"/> (1-based, inclusive) one at a
    /// time, keeping page identity. <see cref="Read"/> flattens the whole document because search only needs
    /// a bag of words; a reader — human or model — needs to say "page 12", so this yields per page instead.
    /// <para>
    /// <paramref name="maxBytes"/> is the budget for the whole run, not per page: it stops mid-document like
    /// <see cref="Read"/> does, and the page it stopped on is flagged <c>Truncated</c> so the caller can ask
    /// for the rest rather than silently believing it saw everything.
    /// </para>
    /// </summary>
    /// <param name="order">
    /// How to order the page's text. <see cref="PdfReadingOrder.Layout"/> costs a clustering pass per page
    /// and is what a multi-column document needs; the search path never asks for it.
    /// </param>
    public static IEnumerable<PdfPageText> ReadPages(
        PdfDocument document, int firstPage, int lastPage, long maxBytes,
        PdfReadingOrder order, CancellationToken ct)
    {
        var budget = maxBytes <= 0 ? 0 : maxBytes / BytesPerChar;
        var spent  = 0L;

        var from = Math.Max(1, firstPage);
        var to   = Math.Min(document.NumberOfPages, lastPage);

        for (var pageNumber = from; pageNumber <= to; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();

            var sb        = new StringBuilder();
            var remaining = budget - spent;
            var complete  = remaining > 0 && AppendPage(document, pageNumber, sb, remaining, order);

            spent += sb.Length;
            yield return new PdfPageText(pageNumber, sb.ToString(), Truncated: !complete);

            if (!complete) yield break;
        }
    }

    /// <summary>
    /// Appends one page's text to <paramref name="sb"/>, stopping at <paramref name="budget"/> characters.
    /// False means the budget ran out part-way. A page that can't be opened, or whose fonts defeat the
    /// extractor, contributes nothing and still counts as complete — one bad page must not cost the other 200.
    /// </summary>
    private static bool AppendPage(
        PdfDocument document, int pageNumber, StringBuilder sb, long budget, PdfReadingOrder order)
    {
        Page page;
        try { page = document.GetPage(pageNumber); }
        catch { return true; }

        if (order == PdfReadingOrder.Layout && TryAppendInReadingOrder(page, sb, budget, out var complete))
            return complete;

        try
        {
            foreach (var word in page.GetWords())
            {
                if (sb.Length >= budget) return false;
                Append(sb, word.Text);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* a page whose fonts defeat the extractor still leaves the rest searchable */ }

        return true;
    }

    /// <summary>
    /// Appends the page as a person would read it: words grouped into blocks by where they sit, and the
    /// blocks put in reading order. False means layout analysis didn't produce anything usable and the
    /// caller should fall back — a page it can't segment must still yield its words rather than nothing.
    /// </summary>
    /// <remarks>
    /// Blocks are separated by a blank line and their internal line breaks kept, rather than being flattened
    /// to single spaces the way the search path does. Search only needs the words to exist; something reading
    /// the page needs to see where a heading ends and a paragraph starts.
    /// </remarks>
    private static bool TryAppendInReadingOrder(Page page, StringBuilder sb, long budget, out bool complete)
    {
        complete = true;

        IReadOnlyList<TextBlock> blocks;
        try
        {
            var letters = page.Letters;
            if (letters is null || letters.Count == 0) return false;

            var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
            blocks    = DocstrumBoundingBoxes.Instance.GetBlocks(words);
            if (blocks.Count == 0) return false;

            blocks = [.. UnsupervisedReadingOrderDetector.Instance.Get(blocks)];
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }   // an unusual page defeats the clustering; the plain word walk still works

        foreach (var block in blocks)
        {
            if (sb.Length >= budget) { complete = false; return true; }

            var text = block.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(text.Trim());
        }

        return true;
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
            // allowContainerNode: without it PdfPig drops every "grouping" bookmark - a section heading with
            // children but no destination - so a document whose only mention of "Appendices" is that heading
            // would be unfindable by it.
            if (document.TryGetBookmarks(out var bookmarks, allowContainerNode: true) && bookmarks is not null)
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
