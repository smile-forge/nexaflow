using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing.Paged;

/// <summary>
/// Content that comes as fixed pages, each laid out on its own as a <see cref="LayoutTree"/> — a PDF, a printed preview.
/// A <see cref="PagedLayoutElement"/> shows it, asking for the pages near what is on screen and letting go of the rest,
/// so a document of any length costs what a screen of it costs.
/// </summary>
public interface IPagedLayoutSource
{
    /// <summary>How many pages there are.</summary>
    int PageCount { get; }

    /// <summary>
    /// How big page <paramref name="index"/> (0-based) is, in device-independent pixels at full size, before it is laid —
    /// what the scroll range is built from. A page laid at another size says so in <see cref="LaidPage.Size"/>.
    /// </summary>
    Size PageSize(int index);

    /// <summary>
    /// Page <paramref name="index"/> laid out. Called from the element's thread and expected to do its work off it; the tree
    /// it returns is frozen, as every layout tree is.
    /// </summary>
    Task<LaidPage> LayAsync(int index, CancellationToken ct);

    /// <summary>
    /// The text page <paramref name="index"/> shows — the text its tree's parts stand for — without laying it, for a
    /// selection or a search that reaches pages that are not on screen.
    /// </summary>
    Task<string> TextAsync(int index, CancellationToken ct);
}

/// <summary>
/// A source whose pages can be rearranged — moved, removed, inserted — while it is shown. It stays the same source and
/// says where each page was, so the host keeps the reader on the page they were reading; laying the pages, and keeping
/// what it has laid so a page that only moved is not laid again, is the source's own business.
/// </summary>
public interface IRearrangingPagedSource : IPagedLayoutSource
{
    /// <summary>The pages were rearranged; <see cref="IPagedLayoutSource.PageCount"/> and every page's index are new.</summary>
    event EventHandler<PagesRearranged>? Rearranged;
}

/// <summary>
/// How a source's pages were rearranged: for each page now, by its index, the index it had before — null for a page
/// that is new — and, for a new page made from pages before it, which they were.
/// </summary>
public sealed class PagesRearranged(IReadOnlyList<int?> before, IReadOnlyList<IReadOnlyList<int>>? madeFrom = null) : EventArgs
{
    public IReadOnlyList<int?> Before { get; } = before;

    /// <summary>
    /// For each page now, by its index, the pages before whose content it shows some of without being one of them — the
    /// page a part was split from, or the parts a page was joined back from; empty for the rest. A page being read that
    /// is gone is read on in the first page made from it.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> MadeFrom { get; } = madeFrom ?? [];
}

/// <summary>A link on a page was pressed: the page it is on (0-based), and its target as the page's tree names it.</summary>
public sealed record PageLink(int Page, string Href);

/// <summary>One page, laid out.</summary>
/// <param name="Tree">The page's layout, in device-independent pixels at full size, its origin the page's top left.</param>
/// <param name="Size">The page's size as laid.</param>
/// <param name="Text">The text its pieces' parts stand for: a part's start and length are offsets into this.</param>
public sealed record LaidPage(LayoutTree Tree, Size Size, string Text);

/// <summary>A place in paged text: a page, and an offset into that page's text.</summary>
public readonly record struct PagePlace(int Page, int Offset) : IComparable<PagePlace>
{
    public int CompareTo(PagePlace other) =>
        Page != other.Page ? Page.CompareTo(other.Page) : Offset.CompareTo(other.Offset);

    public static bool operator <(PagePlace a, PagePlace b) => a.CompareTo(b) < 0;
    public static bool operator >(PagePlace a, PagePlace b) => a.CompareTo(b) > 0;
    public static bool operator <=(PagePlace a, PagePlace b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PagePlace a, PagePlace b) => a.CompareTo(b) >= 0;
}

/// <summary>A stretch of one page's text — a search hit, a selection's share of a page.</summary>
public readonly record struct PageRange(int Page, int Start, int Length);
