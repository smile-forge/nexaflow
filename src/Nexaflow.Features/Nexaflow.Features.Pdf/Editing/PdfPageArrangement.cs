using System.Collections.Immutable;
using Nexaflow.Features.Pdf.Document;
using UglyToad.PdfPig.Core;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// The pages of the document being edited, in order, each a page of one of its sources, and the pictures put in place of
/// its sources' images. Immutable: every edit makes a new arrangement, which is what undo goes back to and what a save
/// writes.
/// <para>
/// A place between pages is a gap, numbered as the page after it is: gap 0 is before the first page, gap
/// <see cref="Count"/> after the last.
/// </para>
/// </summary>
internal sealed class PdfPageArrangement : IEquatable<PdfPageArrangement>
{
    private readonly ImmutableArray<PdfArrangedPage> _pages;

    /// <summary>The pictures put in place of the sources' images, by the image each replaces.</summary>
    private readonly ImmutableDictionary<PdfImageKey, PdfImageReplacement> _images;

    private PdfPageArrangement(ImmutableArray<PdfArrangedPage> pages, ImmutableDictionary<PdfImageKey, PdfImageReplacement> images)
    {
        _pages = pages;
        _images = images;
    }

    public static PdfPageArrangement Empty { get; } = new(ImmutableArray<PdfArrangedPage>.Empty, ImmutableDictionary<PdfImageKey, PdfImageReplacement>.Empty);

    public static PdfPageArrangement Of(IEnumerable<PdfArrangedPage> pages) =>
        new(pages.ToImmutableArray(), ImmutableDictionary<PdfImageKey, PdfImageReplacement>.Empty);

    public IReadOnlyList<PdfArrangedPage> Pages => _pages;

    public int Count => _pages.Length;

    /// <summary>The pictures put in place of the sources' images, by the image each replaces.</summary>
    public IReadOnlyDictionary<PdfImageKey, PdfImageReplacement> Images => _images;

    /// <summary>The pictures put in place of <paramref name="source"/>'s images, by the image each replaces, as its reader takes them.</summary>
    public IReadOnlyDictionary<IndirectReference, PdfImageReplacement> ImagesOf(PdfSourceId source)
    {
        if (_images.IsEmpty) return PdfReadingCaches.NoReplacements;

        var of = new Dictionary<IndirectReference, PdfImageReplacement>();
        foreach (var (key, replacement) in _images)
            if (key.Source == source) of[key.Image] = replacement;
        return of.Count == 0 ? PdfReadingCaches.NoReplacements : of;
    }

    /// <summary>
    /// The arrangement with <paramref name="replacement"/> put in place of <paramref name="image"/> — and every page of the
    /// image's source given a new id from <paramref name="newId"/>, since a page whose id is new is one the reader and the
    /// thumbnails draw anew, and any of them may draw the image.
    /// </summary>
    public PdfPageArrangement WithImage(PdfImageKey image, PdfImageReplacement replacement, Func<PdfPageId> newId)
    {
        var pages = _pages
            .Select(page => page.Sheet.Pieces.Any(piece => piece.Source == image.Source) ? page with { Id = newId() } : page)
            .ToImmutableArray();
        return new PdfPageArrangement(pages, _images.SetItem(image, replacement));
    }

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
        return new PdfPageArrangement(staying.ToImmutableArray(), _images);
    }

    /// <summary>
    /// The arrangement without the pages at <paramref name="indices"/>. A document keeps at least one page.
    /// </summary>
    public PdfPageArrangement Remove(IReadOnlyCollection<int> indices)
    {
        var removing = Checked(indices);
        if (removing.Count >= Count)
            throw new InvalidOperationException("A document keeps at least one page.");

        return new PdfPageArrangement(_pages.Where((_, index) => !removing.Contains(index)).ToImmutableArray(), _images);
    }

    /// <summary>
    /// The arrangement with <paramref name="pages"/>, in order, put into <paramref name="gap"/>.
    /// </summary>
    public PdfPageArrangement Insert(int gap, IReadOnlyList<PdfArrangedPage> pages)
    {
        CheckGap(gap);
        return new PdfPageArrangement(_pages.InsertRange(gap, pages), _images);
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
        return new PdfPageArrangement(kept.ToImmutableArray(), _images);
    }

    public int IndexOf(PdfPageId id)
    {
        for (var i = 0; i < _pages.Length; i++)
            if (_pages[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// Where page <paramref name="number"/> of <paramref name="source"/> now is — the first page showing any of it — or -1
    /// when it is no longer in the document.
    /// </summary>
    public int IndexOf(PdfSourceId source, int number)
    {
        for (var i = 0; i < _pages.Length; i++)
            foreach (var piece in _pages[i].Sheet.Pieces)
                if (piece.Source == source && piece.Number == number) return i;
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
    /// one of them — the page a part was split from, the pages merged onto a sheet, the parts a page was joined back from:
    /// a piece of the same page of the same source, their parts overlapping. Empty for a page that is here, and for one made
    /// from nothing here.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> MadeFrom(PdfPageArrangement next)
    {
        var here = new HashSet<PdfPageId>(_pages.Length);
        var bySourcePage = new Dictionary<(PdfSourceId, int), List<(int Index, PdfPagePart Part)>>();
        for (var i = 0; i < _pages.Length; i++)
        {
            here.Add(_pages[i].Id);
            foreach (var piece in _pages[i].Sheet.Pieces)
            {
                var key = (piece.Source, piece.Number);
                if (!bySourcePage.TryGetValue(key, out var kin)) bySourcePage[key] = kin = [];
                kin.Add((i, piece.Part));
            }
        }

        return next._pages
            .Select(page => here.Contains(page.Id)
                ? []
                : (IReadOnlyList<int>)page.Sheet.Pieces
                    .SelectMany(piece => bySourcePage.TryGetValue((piece.Source, piece.Number), out var kin)
                        ? kin.Where(was => was.Part.Overlaps(piece.Part)).Select(was => was.Index)
                        : [])
                    .Distinct()
                    .Order()
                    .ToList())
            .ToList();
    }

    public bool Equals(PdfPageArrangement? other) =>
        other is not null && (ReferenceEquals(this, other)
                              || (_pages.SequenceEqual(other._pages) && SameImages(_images, other._images)));

    public override bool Equals(object? obj) => Equals(obj as PdfPageArrangement);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var page in _pages)
            hash.Add(page);
        return hash.ToHashCode();
    }

    /// <summary>Whether two sets of pictures put in place of images are the same pictures for the same images.</summary>
    private static bool SameImages(ImmutableDictionary<PdfImageKey, PdfImageReplacement> a, ImmutableDictionary<PdfImageKey, PdfImageReplacement> b) =>
        ReferenceEquals(a, b)
        || (a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && ReferenceEquals(entry.Value, other)));

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
