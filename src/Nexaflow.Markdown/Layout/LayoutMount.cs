using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// A laid tree put down inside another as one piece (<see cref="LayoutBuilder.Mount"/>) — a unit of a long document laid on its own and
/// put down where the unit now starts — and walked, drawn and asked as though its pieces were the holder's own.
///
/// <para>
/// <strong>Nothing of it is copied or moved.</strong> The mounted tree stays exactly as it was laid: its pieces' places on the page are
/// relative to the mount, and the parts they were drawn from name the characters of what it was laid from, where that stood then. A
/// piece seen through the mount (<see cref="LayoutPiece"/>) is placed on the page through it, and names its part as it stands now — found
/// again when asked (<see cref="MovedParts"/>), never ahead of time. So a unit kept through an edit before it is put down again for the
/// cost of one piece, however much it holds.
/// </para>
/// <para>
/// <strong>The mount is no piece of its own</strong>, and neither is the top of the mounted tree: what the mounted tree's top holds is
/// held by whatever holds the mount, so walking the holder walks the mounted pieces as though they had been laid in it.
/// </para>
/// </summary>
public sealed class LayoutMount
{
    /// <summary>What a mount piece is called in the tree holding it — no piece anybody walking the tree meets.</summary>
    public const string Kind = "layout-mount";

    private readonly MovedParts? _moved;

    /// <summary>Each part the mounted tree names, as it stands now — found the first time it is asked after.</summary>
    private Dictionary<ISourcePart, ISourcePart?>? _restated;

    /// <summary>A tree put down as it was laid, its parts naming exactly what they named.</summary>
    public LayoutMount(LayoutTree tree) => this.Tree = tree;

    /// <summary>
    /// A tree laid from <paramref name="drawnFrom"/> — a reading's top, standing where it stood when the tree was laid — put down where
    /// that reading now stands <paramref name="shift"/> characters further along.
    /// </summary>
    public LayoutMount(LayoutTree tree, ContentPart drawnFrom, int shift) : this(tree)
    {
        this.Shift = shift;
        _moved = shift == 0 ? null : new MovedParts(drawnFrom, shift);
    }

    /// <summary>The tree put down.</summary>
    public LayoutTree Tree { get; }

    /// <summary>How far along what the mounted tree was laid from now stands, from where it stood when it was laid.</summary>
    public int Shift { get; }

    /// <summary>
    /// What <paramref name="part"/> — a part one of the mounted tree's pieces was drawn from — is in the tree holding it: the same part,
    /// standing where it now stands. Null for one that can no longer be found, which a tree laid from what it is put down for never has.
    /// </summary>
    internal ISourcePart? Restated(ISourcePart? part)
    {
        if (_moved is null || part is null) return part;

        _restated ??= new(ReferenceEqualityComparer.Instance);
        if (!_restated.TryGetValue(part, out var now)) _restated[part] = now = _moved.Moved(part);

        return now;
    }
}

/// <summary>
/// Where a mounted tree is put down: the mount piece at <paramref name="At"/> in <paramref name="Holder"/>, and where that tree is put down
/// in turn — what a piece seen through mounts climbs out through, and what places it on the page and in the source.
/// </summary>
internal sealed record LayoutMounted(LayoutTree Holder, int At, LayoutMounted? In)
{
    /// <summary>The mount.</summary>
    public LayoutMount Mount => this.Holder.MountOf(this.At)!;

    /// <summary>How far along the source the mounted tree names now stands, through every mount on the way out.</summary>
    public int Shift => this.Mount.Shift + (this.In?.Shift ?? 0);

    /// <summary>The frame the mounted tree's nought is measured in, on the page.</summary>
    public LayoutFrame Frame => Composed(this.In, this.Holder.FrameOf(this.At));

    /// <summary>How many pieces hold the mount, as a walk of the holders sees them.</summary>
    public int Depth => Seen(this.In, this.Holder.DepthOf(this.At));

    /// <summary>What <paramref name="part"/>, named in the mounted tree, is outside every mount on the way out.</summary>
    public ISourcePart? Restated(ISourcePart? part)
    {
        for (var mounted = this; mounted is not null && part is not null; mounted = mounted.In) part = mounted.Mount.Restated(part);

        return part;
    }

    /// <summary><paramref name="local"/>, a frame measured in a tree put down at <paramref name="within"/>, as it stands on the page.</summary>
    public static LayoutFrame Composed(LayoutMounted? within, LayoutFrame local)
    {
        if (within is null) return local;

        var outer = within.Frame;
        var anchor = (LayoutVector)outer.Out(new LayoutPoint(local.Anchor.X, local.Anchor.Y));

        return new LayoutFrame(anchor, local.Shaped is { } inner ? (outer.Shaped is { } shaped ? inner * shaped : inner) : outer.Shaped);
    }

    /// <summary>
    /// How deep a piece <paramref name="depth"/> deep in a tree put down at <paramref name="within"/> is seen: the mount stands where the
    /// mounted tree's top would, and neither is a piece of its own.
    /// </summary>
    public static int Seen(LayoutMounted? within, int depth) => within is null ? depth : within.Depth + depth - 1;
}
