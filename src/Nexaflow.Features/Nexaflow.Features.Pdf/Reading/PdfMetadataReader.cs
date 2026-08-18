using System;
using System.Collections.Generic;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>One row of a document's table of contents.</summary>
/// <param name="Title">The bookmark's label.</param>
/// <param name="Level">Nesting depth, 0 for a root entry.</param>
/// <param name="PageNumber">
/// 1-based page the entry points at, or null when it points somewhere that isn't a page in this document
/// (an external file, a URL) or its destination is unresolvable. Null must stay distinguishable from a page:
/// a row that can't be jumped to has to render as a label rather than a dead link.
/// </param>
/// <param name="OffsetFromTop">
/// How far below the <em>top</em> of the page the destination sits, in points, when it names a position at
/// all. Null for a destination that describes a whole page (<c>/Fit</c>) or none.
/// <para>
/// Deliberately measured downwards, which is not how the PDF stores it. PDF user space has its origin at the
/// bottom-left, so a heading near the top of the page carries a <em>large</em> y — while the embedded
/// viewer's <c>view=FitH</c> parameter takes a distance from the top. Handing the raw y straight over sends
/// every jump to the mirror image of where it belongs. Converting here, once, means the coordinate a caller
/// holds always means what its name says.
/// </para>
/// </param>
internal sealed record PdfOutlineEntry(string Title, int Level, int? PageNumber, double? OffsetFromTop = null);

/// <summary>What a PDF says about itself, without reading a single content stream.</summary>
internal sealed record PdfDocumentInfo(
    int PageCount,
    string? Title, string? Author, string? Subject, string? Keywords,
    string? Creator, string? Producer, string? CreationDate, string? ModifiedDate,
    string PdfVersion,
    bool IsEncrypted,
    bool HasForm, int FormFieldCount,
    IReadOnlyList<PdfOutlineEntry> Outline);

/// <summary>
/// Reads a PDF's <em>description of itself</em> — the information dictionary, the outline, whether it's
/// encrypted or carries a form. Deliberately separate from <see cref="PdfTextReader"/>: everything here comes
/// from the document catalogue, so it costs a dictionary lookup rather than a content-stream parse, and a
/// panel can show it the moment the file opens instead of waiting on the text.
/// <para>
/// Shell-free and synchronous, like its siblings — the caller decides which thread it runs on.
/// </para>
/// </summary>
internal static class PdfMetadataReader
{
    /// <summary>
    /// Everything cheap. Each source is guarded on its own, so a malformed outline tree or form dictionary
    /// costs only itself: a document with a broken bookmark tree still shows its title and page count.
    /// </summary>
    public static PdfDocumentInfo Read(PdfDocument document, CancellationToken ct)
    {
        string? title = null, author = null, subject = null, keywords = null;
        string? creator = null, producer = null, created = null, modified = null;

        try
        {
            var info = document.Information;
            title    = Clean(info.Title);
            author   = Clean(info.Author);
            subject  = Clean(info.Subject);
            keywords = Clean(info.Keywords);
            creator  = Clean(info.Creator);
            producer = Clean(info.Producer);
            created  = Clean(info.CreationDate);
            modified = Clean(info.ModifiedDate);
        }
        catch { }

        ct.ThrowIfCancellationRequested();

        var outline = ReadOutline(document, ct);

        ct.ThrowIfCancellationRequested();

        var hasForm = false;
        var fields  = 0;
        try
        {
            if (document.TryGetForm(out var form) && form is not null)
            {
                hasForm = true;
                foreach (var _ in Flatten(form.Fields)) fields++;
            }
        }
        catch { }

        var pageCount  = TryGet(() => document.NumberOfPages, 0);
        var version    = TryGet(() => document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "?");
        var encrypted  = TryGet(() => document.IsEncrypted, false);

        return new PdfDocumentInfo(
            pageCount, title, author, subject, keywords, creator, producer, created, modified,
            version, encrypted, hasForm, fields, outline);
    }

    /// <summary>
    /// The table of contents, flattened depth-first into reading order with each entry's nesting level kept.
    /// A tree shape is rebuilt from <c>Level</c> by whoever displays it; a flat list is what both a panel and
    /// a text summary actually want, and it can't be malformed by a bookmark tree that lies about its depth.
    /// </summary>
    public static IReadOnlyList<PdfOutlineEntry> ReadOutline(PdfDocument document, CancellationToken ct)
    {
        var entries = new List<PdfOutlineEntry>();
        try
        {
            // allowContainerNode: PdfPig defaults this to FALSE, which silently DROPS every "grouping"
            // bookmark - a heading that has children but no destination of its own - and hoists its children
            // up in its place. That is the single most common shape in a real table of contents ("Part II"
            // over its chapters), so the default turns a structured outline into an unexplained flat list
            // with its section headings missing.
            if (!document.TryGetBookmarks(out var bookmarks, allowContainerNode: true) || bookmarks is null)
                return entries;

            var pageTop = PageTopLookup(document);

            foreach (var (node, level) in Flatten(bookmarks.Roots, 0))
            {
                ct.ThrowIfCancellationRequested();

                var title = Clean(node.Title);
                if (title is null) continue;

                // Only a DocumentBookmarkNode targets a page in *this* document. A Uri/External/Embedded node
                // points elsewhere, and PdfPig reports an unresolvable destination as page 0 — both are "no
                // page", which the panel renders as an un-clickable label.
                var target = node as DocumentBookmarkNode;
                int? page  = target?.PageNumber > 0 ? target.PageNumber : null;
                entries.Add(new PdfOutlineEntry(
                    title, level, page, page is null ? null : OffsetFromTopOf(target, pageTop)));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* a broken outline is "no contents", not a failure to open the document */ }

        return entries;
    }

    /// <summary>
    /// How far below the top of its page the destination sits, or null when it names no position.
    /// <para>
    /// The flip from PDF user space (origin bottom-left) is the whole point — see
    /// <see cref="PdfOutlineEntry.OffsetFromTop"/>. It needs the page's own top edge rather than a constant,
    /// because a MediaBox does not have to start at the origin.
    /// </para>
    /// </summary>
    private static double? OffsetFromTopOf(DocumentBookmarkNode? node, Func<int, double?> pageTop)
    {
        try
        {
            var destination = node?.Destination;
            double? y = destination?.Type switch
            {
                ExplicitDestinationType.XyzCoordinates             => destination.Coordinates.Top,
                ExplicitDestinationType.FitHorizontally            => destination.Coordinates.Top,
                ExplicitDestinationType.FitRectangle               => destination.Coordinates.Top,
                ExplicitDestinationType.FitBoundingBoxHorizontally => destination.Coordinates.Top,
                _ => null,
            };

            if (y is not double top) return null;
            if (pageTop(destination!.PageNumber) is not double edge) return null;

            // Clamp: a destination fractionally outside its own MediaBox is common enough, and a negative
            // offset would scroll the viewer somewhere arbitrary rather than to the top of the page.
            return Math.Max(0, edge - top);
        }
        catch { return null; }
    }

    /// <summary>
    /// The y coordinate of a page's top edge, memoised per page.
    /// <para>
    /// Reading it costs a page parse, so it is only ever done for pages a bookmark actually points at, and
    /// only once each. The cap is a backstop for a pathological outline with a destination on every page of
    /// a huge document: past it the last known edge is reused, which is right for any document with a
    /// uniform page size and no worse than the alternative of making the panel take seconds to appear.
    /// </para>
    /// </summary>
    private static Func<int, double?> PageTopLookup(PdfDocument document)
    {
        const int MaxDistinctPages = 250;

        var cache = new Dictionary<int, double?>();
        double? last = null;

        return pageNumber =>
        {
            if (cache.TryGetValue(pageNumber, out var known)) return known;
            if (cache.Count >= MaxDistinctPages) return last;

            double? edge = null;
            try
            {
                if (pageNumber >= 1 && pageNumber <= document.NumberOfPages)
                    edge = document.GetPage(pageNumber).MediaBox.Bounds.Top;
            }
            catch { }

            cache[pageNumber] = edge;
            if (edge is not null) last = edge;
            return edge;
        };
    }

    // BookmarkNode.Level exists, but it is the level PdfPig read from the file and a malformed outline can
    // report anything; the walk's own depth is the one that always matches the tree we just traversed.
    private static IEnumerable<(BookmarkNode Node, int Level)> Flatten(IEnumerable<BookmarkNode>? nodes, int level)
    {
        if (nodes is null) yield break;
        foreach (var node in nodes)
        {
            yield return (node, level);
            foreach (var child in Flatten(node.Children, level + 1)) yield return child;
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

    /// <summary>Null for anything blank, so callers can omit the row rather than print an empty label.</summary>
    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T TryGet<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
}
