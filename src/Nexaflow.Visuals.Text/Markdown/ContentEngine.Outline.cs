using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The outline of the content shown (<see cref="ContentOutline"/>): what its blocks come to across the units laid — its sections, and
/// the names its headings answer to — and what the reader folded away of it.
///
/// <para>
/// Kept in step with the units laid. A unit kept through a change keeps what it says to the outline, so only the units read again are
/// gathered again, from what their blocks were worked over into, and the outline numbers again only the names they reach.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>What the content laid comes to across its units.</summary>
    private readonly ContentOutline _outline = new();

    /// <summary>What each unit the outline is in step with said to it, one for each of its own.</summary>
    private readonly List<OutlineUnit> _outlined = [];

    /// <summary>The sections folded away as last laid, in the order written — none inside another, whose heading is not laid.</summary>
    private List<OutlineSection> _folded = [];

    /// <summary>Says that a heading of the content laid came, went, or now says or answers to something else.</summary>
    internal event EventHandler? OutlineChanged;

    /// <summary>Every heading of the content's own blocks read, in the order written — what an outline panel lists.</summary>
    internal IReadOnlyList<OutlineHeading> Headings => [.. _outline.Headings()];

    /// <summary>Where the heading a list of them handed on now stands, or null where no heading answers to its name any more.</summary>
    internal (int Start, int Length)? Placed(OutlineHeading heading) =>
        _outline.Named(heading.Name) is { } at && PartAt(_outline.StartOf(at.Unit, at.Entry)) is { } part ? Stretch(part) : null;

    /// <summary>
    /// Where the heading whose titles, outermost first, are <paramref name="titles"/> stands — a heading named by the sections holding it,
    /// so repeated titles under different headings stay apart — or null where none is.
    /// </summary>
    internal (int Start, int Length)? Titled(IReadOnlyList<string> titles) =>
        _outline.Titled(titles) is { } at && PartAt(_outline.StartOf(at.Unit, at.Entry)) is { } part ? Stretch(part) : null;

    /// <summary>
    /// Where a deep link leads in the content laid: a step naming a heading is the heading answering to that name in the outline, and
    /// the steps after it are looked for in its section; any other step is the part of its kind with that name, or at that place among
    /// those of its kind, inside the last. Where the link no longer leads all the way, the furthest it does; null where it leads nowhere.
    /// </summary>
    internal (int Start, int Length)? Located(ContentPath path)
    {
        var (from, to) = (0, int.MaxValue);
        ContentPart? at = null, found = null;

        foreach (var step in path.Steps)
        {
            if (at is null && step.Name is { Length: > 0 } name && _outline.Within(from, to, name) is { } entry
                && PartAt(_outline.StartOf(entry.Unit, entry.Entry)) is { } heading && heading.Kind == step.Kind)
            {
                found = heading;

                // A heading of the content's own is gone into as its section; one inside a block, as the part it is.
                if (_outline.Section(entry.Unit, entry.Entry) is { } section) (from, to) = (section.Start + 1, section.End);
                else at = heading;

                continue;
            }

            if (Took(at is null ? Tops(from, to) : Inside(at), step) is not { } next) break;

            found = at = next;
        }

        return found is null ? null : Stretch(found);
    }

    /// <summary>
    /// A deep link to the place <paramref name="offset"/> is in, in the content laid: a heading by the name it answers to, and anything
    /// else from the section it stands in, by the kinds holding it, each by its name or its place among those of its kind.
    /// </summary>
    internal ContentPath PathTo(int offset)
    {
        var holding = new List<ContentPart>();

        for (var at = UnitHolding(offset); at is not null && Inside(at).FirstOrDefault(part => part.Start <= offset && offset < part.End) is { } inner; at = inner)
            holding.Add(inner);
        if (holding.Count == 0) return ContentPath.Nowhere;

        var block = holding[0];
        if (OutlineMark.Of(block.Node) is not null && _outline.EntryAt(block.Start) is { } own)
            return new ContentPath([new ContentStep(block.Kind, _outline.Name(own.Unit, own.Entry))]);

        var steps = new List<ContentStep>();
        var (from, to) = (0, int.MaxValue);

        if (_outline.Holding(block.Start).FirstOrDefault() is { } section && PartAt(section.Start) is { } heading)
        {
            steps.Add(new ContentStep(heading.Kind, section.Name));
            (from, to) = (section.Start + 1, section.End);
        }

        var among = Tops(from, to);

        foreach (var part in holding)
        {
            steps.Add(new ContentStep(part.Kind, Named(part), among.Where(beside => beside.Kind == part.Kind).TakeWhile(beside => !ReferenceEquals(beside, part)).Count()));
            among = Inside(part);
        }

        return new ContentPath(steps);
    }

    /// <summary>The stretch a reader is shown for <paramref name="part"/>: what it was written as, or where it stands where that is nothing.</summary>
    private static (int Start, int Length) Stretch(ContentPart part) => part.Written is { Length: > 0 } written ? written : (part.Start, part.Length);

    /// <summary>The part of the content laid starting at <paramref name="start"/> that was promoted into the outline, or null.</summary>
    private ContentPart? PartAt(int start)
    {
        for (var at = UnitHolding(start); at?.Children.FirstOrDefault(part => !part.Derived && part.Start <= start && (start < part.End || part.Start == start)) is { } inner; at = inner)
            if (inner.Start == start && OutlineMark.Of(inner.Node) is not null) return inner;

        return null;
    }

    /// <summary>The part of <paramref name="among"/> a step names — of its kind, by its name or at its place among those of its kind — or null.</summary>
    private static ContentPart? Took(IEnumerable<ContentPart> among, ContentStep step)
    {
        var seen = 0;

        foreach (var part in among)
        {
            if (part.Kind != step.Kind) continue;

            if (step.Name is { Length: > 0 } wanted)
            {
                if (string.Equals(Named(part), wanted, StringComparison.OrdinalIgnoreCase)) return part;

                continue;
            }

            if (seen++ == step.Index) return part;
        }

        return null;
    }

    /// <summary>
    /// What a step may name inside a part: its own children, and the children of the wrappers a path goes through — the body of a block,
    /// the sequence its children sit in — so a path says what the content is made of and not how its reading happens to nest it.
    /// </summary>
    private static IEnumerable<ContentPart> Inside(ContentPart at)
    {
        foreach (var child in at.Children)
        {
            if (child.Derived || child.Role == Roles.Trivia) continue;

            if (child.Role == Roles.Body || child.Kind == Kinds.Sequence)
            {
                foreach (var inner in Inside(child)) yield return inner;

                continue;
            }

            yield return child;
        }
    }

    /// <summary>What a part is called: what it was written as, where it says so, and otherwise the name a stage worked out for it.</summary>
    private static string? Named(ContentPart part)
    {
        if (part.Part(Roles.Name) is { Text.Length: > 0 } written) return written.Text.Trim();

        foreach (var child in part.Children)
            foreach (var held in child.Children)
                if (held.Node.Held is string said && said.Length > 0)
                    return said;

        return null;
    }

    /// <summary>
    /// The heading a link to <c>#<paramref name="anchor"/></c> points at, as <paramref name="laid"/> lays it — whatever the name's case
    /// — or nothing where no heading laid answers to it.
    /// </summary>
    public LayoutPiece Sought(Laid laid, string anchor)
    {
        if (_outline.Named(anchor) is not { } named) return default;

        var start = _outline.StartOf(named.Unit, named.Entry);

        foreach (var piece in laid.Root.SelfAndDescendants())
            if (piece.Part is ContentPart part && part.Start == start && OutlineMark.Of(part.Node) is not null)
                return piece;

        return default;
    }

    /// <summary>The name the piece <paramref name="part"/> was promoted into the outline as answers to, or null where it was not.</summary>
    internal string? NameOf(ContentPart part) =>
        OutlineMark.Of(part.Node) is not null && _outline.EntryAt(part.Start) is { } entry ? _outline.Name(entry.Unit, entry.Entry) : null;

    /// <summary>The section a block of the content's own heads, or null where it heads none.</summary>
    private OutlineSection? Headed(ContentPart? block) =>
        block is { Parent: { Parent: null } } && _outline.EntryAt(block.Start) is { } entry ? _outline.Section(entry.Unit, entry.Entry) : null;

    /// <summary>
    /// Brings the outline into step with every unit read: what each unit says to it is what working it over found, or its width alone where
    /// nothing has worked it over yet, so the outline's offsets hold for the whole of what is read. The units either end that say what they
    /// said when it was last in step are kept, and those between replace what stood there. Says so (<see cref="OutlineChanged"/>) where a
    /// heading came, went, or now says or answers to something else.
    /// </summary>
    private void InStep()
    {
        var listed = _shown?.Listed ?? [];
        var units = new List<OutlineUnit>(listed.Count);

        foreach (var (unit, _) in listed)
            units.Add(_facts.TryGetValue(unit.Tree, out var facts) ? facts.Outline : _unworked.GetValue(unit.Tree, _ => OutlineUnit.Of([], unit.Width)));

        var first = 0;
        while (first < units.Count && first < _outlined.Count && ReferenceEquals(units[first], _outlined[first])) first++;

        var last = 0;
        while (last < units.Count - first && last < _outlined.Count - first && ReferenceEquals(units[^(last + 1)], _outlined[^(last + 1)])) last++;

        if (first == units.Count && first == _outlined.Count) return;

        var gathered = units.GetRange(first, units.Count - first - last);

        var was = Enumerable.Range(first, _outlined.Count - last - first).SelectMany(index => Listed(_outline[index].Unit)).ToList();
        var renamed = _outline.Replaced(first, _outlined.Count - last, gathered);

        _outlined.RemoveRange(first, _outlined.Count - last - first);
        _outlined.InsertRange(first, gathered);

        if (renamed.Count > 0 || !was.SequenceEqual(gathered.SelectMany(Listed))) OutlineChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What <paramref name="unit"/>'s headings say and how high they rank — what a list of them shows.</summary>
    private static IEnumerable<(string Title, int Rank, string Called)> Listed(OutlineUnit unit) =>
        unit.Entries.Where(entry => entry.Heads).Select(entry => (entry.Title, entry.Rank, entry.Called));

    /// <summary>Works out which sections the reader has folded away, as the outline now stands.</summary>
    private void Fold()
    {
        var folded = new List<OutlineSection>();

        foreach (var (name, open) in _sections.Expansion)
            if (!open && _outline.Named(name) is { } named && _outline.Section(named.Unit, named.Entry) is { Holds: true } section)
                folded.Add(section);

        folded.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        _folded = [];
        foreach (var section in folded)
            if (_folded.Count == 0 || section.Start >= _folded[^1].End) _folded.Add(section);
    }

    /// <summary>What the reader's folding says of the block <paramref name="block"/> of the content laid.</summary>
    private LayoutFold Folding(ContentPart block)
    {
        var (low, high) = (0, _folded.Count);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_folded[middle].Start <= block.Start) low = middle + 1;
            else high = middle;
        }

        if (low == 0) return default;

        var section = _folded[low - 1];

        return section.Start == block.Start ? new LayoutFold(false, section.Name, section.Behind)
             : block.Start < section.End ? new LayoutFold(true)
             : default;
    }
}
