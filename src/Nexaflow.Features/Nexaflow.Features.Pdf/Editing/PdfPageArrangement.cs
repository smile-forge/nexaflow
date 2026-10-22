using System.Collections.Immutable;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// The pages of the document being edited, in order, each a page of one of its sources. Immutable: every edit makes a
/// new arrangement, which is what undo goes back to and what a save writes.
/// <para>
/// A place between pages is a gap, numbered as the page after it is: gap 0 is before the first page, gap
/// <see cref="Count"/> after the last.
/// </para>
/// </summary>
internal sealed class PdfPageArrangement : IEquatable<PdfPageArrangement>
{
    private readonly ImmutableArray<PdfArrangedPage> _pages;

    private PdfPageArrangement(ImmutableArray<PdfArrangedPage> pages)
    {
        _pages = pages;
    }

    public static PdfPageArrangement Empty { get; } = new(ImmutableArray<PdfArrangedPage>.Empty);

    public static PdfPageArrangement Of(IEnumerable<PdfArrangedPage> pages) => new(pages.ToImmutableArray());

    public IReadOnlyList<PdfArrangedPage> Pages => _pages;

    public int Count => _pages.Length;

    public PdfArrangedPage this[int index] => _pages[index];

    /// <summary>
    /// The pages at <paramref name="indices"/>, kept in the order they had, moved together into
    /// <paramref name="gap"/> — a gap of this arrangement, before the move.
    /// </summary>
    public PdfPageArrangement Move(IReadOnlyCollection<int> indices, int gap)
    {
        var moving = Checked(indices);
        CheckGap(gap);

        var moved = moving.OrderBy(index => index).Select(index => _pages[index]).ToList();
        var staying = _pages.Where((_, index) => !moving.Contains(index)).ToList();
        staying.InsertRange(gap - moving.Count(index => index < gap), moved);
        return new PdfPageArrangement(staying.ToImmutableArray());
    }

    /// <summary>
    /// The arrangement without the pages at <paramref name="indices"/>. A document keeps at least one page.
    /// </summary>
    public PdfPageArrangement Remove(IReadOnlyCollection<int> indices)
    {
        var removing = Checked(indices);
        if (removing.Count >= Count)
            throw new InvalidOperationException("A document keeps at least one page.");

        return new PdfPageArrangement(_pages.Where((_, index) => !removing.Contains(index)).ToImmutableArray());
    }

    /// <summary>
    /// The arrangement with <paramref name="pages"/>, in order, put into <paramref name="gap"/>.
    /// </summary>
    public PdfPageArrangement Insert(int gap, IReadOnlyList<PdfArrangedPage> pages)
    {
        CheckGap(gap);
        return new PdfPageArrangement(_pages.InsertRange(gap, pages));
    }

    /// <summary>
    /// The arrangement with the pages at <paramref name="indices"/> replaced by <paramref name="pages"/>, put where the
    /// first of them was — a split page by its parts.
    /// </summary>
    public PdfPageArrangement Replace(IReadOnlyCollection<int> indices, IReadOnlyList<PdfArrangedPage> pages)
    {
        var replacing = Checked(indices);
        var at = replacing.Min();
        var kept = _pages.Where((_, index) => !replacing.Contains(index)).ToList();
        kept.InsertRange(at - replacing.Count(index => index < at), pages);
        return new PdfPageArrangement(kept.ToImmutableArray());
    }

    public int IndexOf(PdfPageId id)
    {
        for (var i = 0; i < _pages.Length; i++)
            if (_pages[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// Where page <paramref name="number"/> of <paramref name="source"/> now is, or -1 when it is no longer in the document.
    /// </summary>
    public int IndexOf(PdfSourceId source, int number)
    {
        for (var i = 0; i < _pages.Length; i++)
            if (_pages[i].Source == source && _pages[i].Number == number) return i;
        return -1;
    }

    /// <summary>
    /// Where each page of <paramref name="next"/> was in this arrangement, for a reader that keeps its place: for each
    /// of its indices, the index the same page had here, or null for a page that is new.
    /// </summary>
    public IReadOnlyList<int?> Before(PdfPageArrangement next)
    {
        var was = new Dictionary<PdfPageId, int>(_pages.Length);
        for (var i = 0; i < _pages.Length; i++)
            was[_pages[i].Id] = i;

        return next._pages.Select(page => was.TryGetValue(page.Id, out var index) ? index : (int?)null).ToList();
    }

    /// <summary>
    /// For each page of <paramref name="next"/>, by its index, the pages here whose content it shows some of without being
    /// one of them — the page a part was split from, or the parts a page was joined back from by an undo: the same page
    /// of the same source, their parts overlapping. Empty for a page that is here, and for one made from nothing here.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> MadeFrom(PdfPageArrangement next)
    {
        var here = new HashSet<PdfPageId>(_pages.Length);
        var bySourcePage = new Dictionary<(PdfSourceId, int), List<int>>();
        for (var i = 0; i < _pages.Length; i++)
        {
            here.Add(_pages[i].Id);
            var key = (_pages[i].Source, _pages[i].Number);
            if (!bySourcePage.TryGetValue(key, out var indices)) bySourcePage[key] = indices = [];
            indices.Add(i);
        }

        return next._pages
            .Select(page => here.Contains(page.Id) || !bySourcePage.TryGetValue((page.Source, page.Number), out var kin)
                ? []
                : (IReadOnlyList<int>)kin.Where(i => (_pages[i].Part ?? PdfPagePart.Whole).Overlaps(page.Part ?? PdfPagePart.Whole)).ToList())
            .ToList();
    }

    public bool Equals(PdfPageArrangement? other) =>
        other is not null && (ReferenceEquals(this, other) || _pages.SequenceEqual(other._pages));

    public override bool Equals(object? obj) => Equals(obj as PdfPageArrangement);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var page in _pages)
            hash.Add(page);
        return hash.ToHashCode();
    }

    private HashSet<int> Checked(IReadOnlyCollection<int> indices)
    {
        var set = new HashSet<int>(indices);
        if (set.Count == 0)
            throw new ArgumentException("No pages were given.", nameof(indices));
        if (set.Any(index => index < 0 || index >= Count))
            throw new ArgumentOutOfRangeException(nameof(indices), "A page is not one of the document's.");
        return set;
    }

    private void CheckGap(int gap)
    {
        if (gap < 0 || gap > Count)
            throw new ArgumentOutOfRangeException(nameof(gap), gap, "The place is not between the document's pages.");
    }
}
