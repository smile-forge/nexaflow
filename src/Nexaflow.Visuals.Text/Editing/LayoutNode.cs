using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;

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

    /// <summary>Says this drew nothing after all, so nothing treats it as something to point at.</summary>
    public void NotInk() => IsInk = false;

    public ILayoutNode? Across { get; private set; }

    public ILayoutNode? Down { get; private set; }

    public Vector Offset { get; private set; }

    /// <summary>
    /// Moves this piece and everything drawn inside it, without laying any of it out again.
    ///
    /// <para>
    /// Both halves happen here, and both are needed. <see cref="Offset"/> accumulates, which is what a
    /// painter pushes as a transform - the marks were recorded where the piece used to be and are not
    /// rewritten. <see cref="Bounds"/> is translated through the whole subtree, which is what keeps
    /// every query answering about where things are now. A node moved by its parent and again by itself
    /// composes correctly because the transforms nest exactly as the bounds accumulate.
    /// </para>
    /// <para>
    /// Containment only. An ordering parent (<see cref="Across"/>, <see cref="Down"/>) holds members it
    /// does not draw, and moving one would move a subtree that something else is responsible for.
    /// </para>
    /// </summary>
    public void Move(Vector by)
    {
        if (by.X == 0 && by.Y == 0) return;

        Offset += by;

        foreach (var node in this.SelfAndDescendants().OfType<LayoutNode>())
            if (!node.Bounds.IsEmpty)
                node.Bounds = Rect.Offset(node.Bounds, by);
    }

    /// <summary>
    /// Puts <paramref name="members"/> in order along one axis, so each of them can be stepped from to
    /// the next, and hands back the parent that holds that order.
    ///
    /// <para>
    /// The members are its children, and their <see cref="Parent"/> is left alone: it goes on pointing at
    /// whatever draws them. So this is reachable only from a member, upward, and a walk that paints or
    /// measures never meets one.
    /// </para>
    /// <para>
    /// It exists to answer "what is after this", nothing more. It is not a group anybody belongs to and
    /// nothing enumerates it from the top — see <see cref="ILayoutNode.Across"/> for why that distinction
    /// is the whole design.
    /// </para>
    /// </summary>
    public static LayoutNode Ordering(IReadOnlyList<ILayoutNode> members, bool across, string kind,
                                      ISourcePart? part = null)
    {
        var bounds = Rect.Empty;
        foreach (var member in members) bounds = Rect.Union(bounds, member.Bounds);

        var parent = new LayoutNode(bounds, part, kind, isInk: false);

        foreach (var member in members)
        {
            parent._children.Add(member);
            if (member is not LayoutNode node) continue;

            if (across) node.Across = parent;
                        else node.Down = parent;
                    }

                    return parent;
    }
    public bool IsEnclosure { get; internal set; }
    public string Kind { get; }

    /// <summary>Adds a child and adopts it. The only way parentage is set, so it cannot disagree.</summary>
    public LayoutNode Add(LayoutNode child)
    {
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    /// <summary>
    /// Grows this piece to cover something it drew, and everything holding it to cover this.
    ///
    /// <para>
    /// Upward, because a piece is made before it is filled — a note before its head, a bar before its
    /// notes — so growing only the piece itself would leave every container the size it was when it was
    /// made. Each drawing says once where it went and the whole chain above it learns; the alternative is a
    /// <c>foreach child</c> at the end of every method that builds one, and the one that gets forgotten is a
    /// container reporting a rectangle smaller than what it holds.
    /// </para>
    /// <para>
    /// <strong>Deliberately not called by <see cref="Add"/>.</strong> Whether a parent's rectangle is the
    /// union of its children is a fact about the content, not about layout trees: a typeset formula's
    /// container bounds are the height and depth it reserves on its line, so a subscript hangs below the
    /// very node that holds it and growing to fit would be wrong. Content whose containers <em>are</em>
    /// bounding boxes calls this as it draws.
    /// </para>
    /// </summary>
    public void Covering(Rect what)
    {
        if (what.IsEmpty || what.Width < 0 || what.Height < 0) return;

        var union = Bounds;
        if (union.IsEmpty || (union.Width == 0 && union.Height == 0)) Bounds = what;
        else
        {
            union.Union(what);
            Bounds = union;
        }

        (Parent as LayoutNode)?.Covering(what);
    }

    /// <summary>
    /// A piece drawn from a stretch of source, or from nothing anybody wrote.
    ///
    /// <para>
    /// Ink follows the part, which is the ordinary convention: something a reader typed is something they
    /// can point at, and a rule, a beam or a guard pattern the drawing invented is not. Content that draws
    /// something pointable which nobody wrote — a hole waiting to be typed into — says so with the other
    /// constructor.
    /// </para>
    /// </summary>
    public LayoutNode(Rect bounds, string kind, ISourcePart? part = null)
        : this(bounds, part, kind, isInk: part is { Length: > 0 })
    {
    }

    /// <summary>
    /// Adds a child and grows to hold it, giving back the child so a builder can go on filling it.
    ///
    /// <para>
    /// For content whose containers <em>are</em> bounding boxes — a bar is as tall as what is in it — which
    /// is most of them. <see cref="Add"/> stays the bare version for content where a container's rectangle
    /// means something else; see <see cref="Covering"/>.
    /// </para>
    /// </summary>
    public LayoutNode Holding(LayoutNode child)
    {
        Add(child);
        Covering(child.Bounds);
        return child;
    }

    /// <summary>
    /// Gathers children this node already has under <paramref name="group"/>, which takes their place
    /// where the first of them sat.
    ///
    /// <para>
    /// For a thing that is only known once its parts are drawn. A tie joins two notes and cannot be laid
    /// out until both have landed, so the notes are drawn first and what joins them is built around them
    /// afterwards — a re-parenting rather than a nesting.
    /// </para>
    /// <para>
    /// The group is handed in rather than made here so that a tree stays one node type all the way down;
    /// a score's pieces carry their own drawing and a plain node would carry none.
    /// </para>
    /// <para>
    /// Only members this node actually holds, and it says so rather than quietly ignoring one: a group
    /// standing over children that belong to somebody else would be two parents claiming one subtree, and
    /// every walk that paints or measures would meet it twice.
    /// </para>
    /// </summary>
    public TGroup Regroup<TGroup>(IReadOnlyList<LayoutNode> members, TGroup group)
        where TGroup : LayoutNode
    {
        var at = _children.Count;

        foreach (var member in members)
        {
            var was = _children.IndexOf(member);
            if (was < 0)
                throw new System.InvalidOperationException(
                    $"{group.Kind}: {member.Kind} is not a child of {Kind}");

            at = System.Math.Min(at, was);
        }

        group.Parent = this;

        foreach (var member in members)
        {
            _children.Remove(member);
            member.Parent = group;
            group._children.Add(member);
            group.Bounds = group.Bounds.IsEmpty ? member.Bounds : Rect.Union(group.Bounds, member.Bounds);
        }

        _children.Insert(System.Math.Min(at, _children.Count), group);
        return group;
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
