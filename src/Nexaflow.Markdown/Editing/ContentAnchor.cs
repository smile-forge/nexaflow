using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// A place in a document that stays with the characters round it as the document is written in — what a fold, a mark or a host's
/// bookmark holds instead of an offset that would go stale with the next keystroke (<see cref="EditedDocument.Anchor"/>).
///
/// <para>
/// <strong>Carried, never searched for.</strong> The document moves every anchor through each change before it says the change
/// happened (<see cref="EditedDocument.Changed"/>), by the one rule every place in the engine is carried by
/// (<see cref="ContentChange.Carried"/>): text written in front of it pushes it along, a stretch written over that holds it leaves it
/// at the start of what took the stretch's place — or at its end, for one that <see cref="LeansAfter"/>.
/// </para>
/// <para>
/// <strong>Let go of, not collected.</strong> A document carries an anchor until it is disposed — nothing waits on the collector to
/// stop the work — so whatever takes one disposes it when it is done with it.
/// </para>
/// </summary>
public sealed class ContentAnchor : IDisposable
{
    private ContentAnchors? _owner;

    internal ContentAnchor(ContentAnchors owner, bool leansAfter)
    {
        _owner = owner;
        this.LeansAfter = leansAfter;
    }

    /// <summary>The block of anchors it is kept in, whose shift its offset is stored against.</summary>
    internal ContentAnchors.Block? Home { get; set; }

    /// <summary>Its offset, less the shift of the block it is kept in.</summary>
    internal int Stored { get; set; }

    /// <summary>
    /// Whether it goes with what is written exactly where it stands, ending up after it — a fold's end, a selection's far edge — rather
    /// than staying in front of it.
    /// </summary>
    public bool LeansAfter { get; }

    /// <summary>Where it stands in the document as it now reads — where it last stood, once let go of.</summary>
    public int Offset => this.Stored + (this.Home?.Shift ?? 0);

    /// <summary>Whether it has been let go of, so the document no longer carries it.</summary>
    public bool IsReleased => _owner is null;

    /// <summary>Stops the document carrying it; it keeps saying where it last stood.</summary>
    public void Dispose()
    {
        if (_owner is not { } owner) return;

        var at = this.Offset;
        owner.Remove(this);
        _owner = null;
        this.Stored = at;
    }

    public override string ToString() => $"@{this.Offset}{(this.LeansAfter ? "+" : string.Empty)}";
}

/// <summary>
/// The anchors of one document, in order, kept in blocks of a few dozen — each with a shift its anchors' offsets are stored against — so
/// carrying them through a write moves the ones inside it one by one and every block wholly after it by one addition.
/// </summary>
internal sealed class ContentAnchors
{
    /// <summary>How many anchors a block holds before it is split in two.</summary>
    private const int Most = 64;

    private readonly List<Block> _blocks = [];

    /// <summary>A run of anchors in order, standing a shift further along than their stored offsets say.</summary>
    internal sealed class Block
    {
        public readonly List<ContentAnchor> Items = new(Most + 1);

        public int Shift;

        public int Last => this.Items[^1].Stored + this.Shift;

        public int First => this.Items[0].Stored + this.Shift;
    }

    /// <summary>How many anchors it carries.</summary>
    public int Count => _blocks.Sum(block => block.Items.Count);

    /// <summary>A new anchor at <paramref name="offset"/>, among the others in order.</summary>
    public ContentAnchor Add(int offset, bool leansAfter)
    {
        var anchor = new ContentAnchor(this, leansAfter);

        if (_blocks.Count == 0) _blocks.Add(new Block());

        var index = this.BlockFor(offset, leansAfter);
        var block = _blocks[index];

        var at = 0;
        while (at < block.Items.Count && Sorts(block.Items[at], offset, leansAfter)) at++;

        anchor.Home = block;
        anchor.Stored = offset - block.Shift;
        block.Items.Insert(at, anchor);

        if (block.Items.Count > Most) this.Split(index);

        return anchor;
    }

    /// <summary>Stops carrying <paramref name="anchor"/>.</summary>
    public void Remove(ContentAnchor anchor)
    {
        if (anchor.Home is not { } block) return;

        block.Items.Remove(anchor);
        if (block.Items.Count == 0) _blocks.Remove(block);

        anchor.Home = null;
    }

    /// <summary>Every anchor carried through <paramref name="change"/>: its writes from the last back, so each one's offsets still mean what they did.</summary>
    public void Carry(ContentChange change)
    {
        if (_blocks.Count == 0) return;

        foreach (var write in change.Writes.OrderByDescending(write => write.Start)) this.Written(write);
    }

    /// <summary>Every anchor kept inside a document now <paramref name="length"/> long: what was loaded in its place says nothing of where any of them went.</summary>
    public void Clamp(int length)
    {
        var all = _blocks.SelectMany(block => block.Items).Select(anchor => (Anchor: anchor, At: Math.Clamp(anchor.Offset, 0, length))).ToList();
        all.Sort((one, other) => Compare(one.At, one.Anchor.LeansAfter, other.At, other.Anchor.LeansAfter));

        var slot = 0;
        foreach (var block in _blocks)
            for (var index = 0; index < block.Items.Count; index++, slot++)
            {
                var (anchor, at) = all[slot];
                block.Items[index] = anchor;
                anchor.Home = block;
                anchor.Stored = at - block.Shift;
            }
    }

    /// <summary>
    /// Carries every anchor through one write. Those in front of it stay; every block wholly after it moves by what it grew by; those
    /// inside or at its edges are each carried by the rule, and put back in order where that changed it.
    /// </summary>
    private void Written(ContentWrite write)
    {
        var by = write.Text.Length - write.Length;
        var first = this.FirstReaching(write.Start);
        if (first < 0) return;

        List<(Block Block, int Index, int At)>? touched = null;

        for (var index = first; index < _blocks.Count; index++)
        {
            var block = _blocks[index];

            if (block.First > write.End)
            {
                block.Shift += by;
                continue;
            }

            for (var item = 0; item < block.Items.Count; item++)
            {
                var anchor = block.Items[item];
                var at = anchor.Offset;

                if (at < write.Start) continue;

                if (at > write.End)
                {
                    anchor.Stored += by;
                    continue;
                }

                var now = write.End == at && (write.Length > 0 || anchor.LeansAfter) ? at + by
                        : write.Start < at ? write.Start + (anchor.LeansAfter ? write.Text.Length : 0)
                        : at;

                anchor.Stored = now - block.Shift;
                (touched ??= []).Add((block, item, now));
            }
        }

        if (touched is not null) Reordered(touched);
    }

    /// <summary>
    /// The anchors a write reached, which stand next to one another in order, put back in order: two inside a stretch written over can
    /// come out of it either way round, by which way each leans.
    /// </summary>
    private static void Reordered(List<(Block Block, int Index, int At)> touched)
    {
        var anchors = touched.Select(slot => (Anchor: slot.Block.Items[slot.Index], slot.At)).ToList();

        var sorted = true;
        for (var index = 1; index < anchors.Count && sorted; index++)
            sorted = Compare(anchors[index - 1].At, anchors[index - 1].Anchor.LeansAfter, anchors[index].At, anchors[index].Anchor.LeansAfter) <= 0;

        if (sorted) return;

        var ordered = anchors.OrderBy(pair => pair.At).ThenBy(pair => pair.Anchor.LeansAfter).ToList();

        for (var index = 0; index < touched.Count; index++)
        {
            var (block, slot, _) = touched[index];
            var (anchor, at) = ordered[index];

            block.Items[slot] = anchor;
            anchor.Home = block;
            anchor.Stored = at - block.Shift;
        }
    }

    /// <summary>The first block holding an anchor at or after <paramref name="offset"/>, or -1 where none does.</summary>
    private int FirstReaching(int offset)
    {
        var (low, high) = (0, _blocks.Count);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_blocks[middle].Last < offset) low = middle + 1;
            else high = middle;
        }

        return low < _blocks.Count ? low : -1;
    }

    /// <summary>The block a new anchor goes in: the first whose last anchor does not sort before it, or else the last.</summary>
    private int BlockFor(int offset, bool leansAfter)
    {
        var (low, high) = (0, _blocks.Count - 1);

        while (low < high)
        {
            var middle = (low + high) / 2;
            var last = _blocks[middle].Items[^1];

            if (Sorts(last, offset, leansAfter)) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    private void Split(int index)
    {
        var block = _blocks[index];
        var half = block.Items.Count / 2;
        var after = new Block { Shift = block.Shift };

        after.Items.AddRange(block.Items.Skip(half));
        block.Items.RemoveRange(half, block.Items.Count - half);

        foreach (var anchor in after.Items) anchor.Home = after;

        _blocks.Insert(index + 1, after);
    }

    /// <summary>Whether <paramref name="anchor"/> stands in front of a new one at <paramref name="offset"/>: by offset, and at one offset those leaning before first.</summary>
    private static bool Sorts(ContentAnchor anchor, int offset, bool leansAfter) =>
        Compare(anchor.Offset, anchor.LeansAfter, offset, leansAfter) <= 0;

    private static int Compare(int one, bool oneAfter, int other, bool otherAfter) =>
        one != other ? one.CompareTo(other) : oneAfter.CompareTo(otherAfter);

    /// <summary>Whether it carries any anchor at all — what spares a change finding what it was for nobody.</summary>
    public bool Any => _blocks.Count > 0;
}
