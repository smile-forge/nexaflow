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

    public LayoutNode(Rect bounds, int sourceStart, int sourceLength, string kind, bool isInk)
    {
        Bounds = bounds;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        Kind = kind;
        IsInk = isInk;
    }

    public Rect Bounds { get; internal set; }
    public ILayoutNode? Parent { get; private set; }
    public IReadOnlyList<ILayoutNode> Children => _children;
    public int SourceStart { get; internal set; }
    public int SourceLength { get; internal set; }
    public bool IsInk { get; internal set; }
    public string Kind { get; }

    /// <summary>Adds a child and adopts it. The only way parentage is set, so it cannot disagree.</summary>
    public LayoutNode Add(LayoutNode child)
    {
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    public override string ToString() =>
        $"{Kind}[{SourceStart},{SourceLength}]{(IsInk ? "*" : "")} {Bounds}";
}
