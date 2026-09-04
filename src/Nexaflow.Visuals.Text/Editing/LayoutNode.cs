using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A plain <see cref="ILayoutNode"/>, for content that has no layout model of its own to implement the
/// interface on.
/// <para>
/// A music score has <c>SystemLayout</c> and <c>MeasureLayout</c> already and should implement
/// <see cref="ILayoutNode"/> on those rather than copy itself into these. A formula has no such classes —
/// its layout lives inside the typesetter — so it builds these instead.
/// </para>
/// </summary>
public class LayoutNode : ILayoutNode
{
    private readonly List<ILayoutNode> _children = [];
    private readonly List<LayoutMark> _marks = [];

    public LayoutNode(Rect bounds, ISourcePart? part, string kind, bool isInk)
    {
        Bounds = bounds;
        Part = part;
        Kind = kind;
        IsInk = isInk;
    }

    public Rect Bounds { get; internal set; }
    public ILayoutNode? Parent { get; private set; }
    public IReadOnlyList<ILayoutNode> Children => _children;

    /// <summary>
    /// What this piece was drawn from. Handed in by the builder, which is the only thing that holds both
    /// trees — never worked out here, and never a copy of where that part sits.
    /// </summary>
    public ISourcePart? Part { get; internal set; }

    public bool IsInk { get; internal set; }
    public bool IsEnclosure { get; internal set; }
    public string Kind { get; }

    /// <summary>Adds a child and adopts it. The only way parentage is set, so it cannot disagree.</summary>
    public LayoutNode Add(LayoutNode child)
    {
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>What this piece drew, in the order it drew it.</summary>
    public IReadOnlyList<LayoutMark> Marks => _marks;

    /// <summary>
    /// Records a mark against this piece. Held here rather than in a picture of its own so that painting
    /// and asking are the same walk over the same tree — see <see cref="LayoutMark"/>.
    /// </summary>
    public void Drew(LayoutMark mark) => _marks.Add(mark);

    public override string ToString() =>
        $"{Kind}{(Part is { } p ? $"[{p.Start},{p.Length}]" : "")}{(IsInk ? "*" : "")} {Bounds}";
}
