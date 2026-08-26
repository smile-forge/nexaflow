using System.Text;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// One piece of a formula: what it is, what it is to whatever holds it, and either the characters it
/// stands for or the pieces it is made of.
///
/// <para>
/// The tree owns the text. There are no offsets stored anywhere — a node knows how wide it is, and an
/// offset is worked out by a walk when somebody asks (<see cref="Placed"/>). That is what makes the
/// source a serialization format rather than the document: a tree that has never been printed is still
/// the whole truth, and an edit that replaces a subtree cannot leave a stale position behind, because
/// there were none to go stale.
/// </para>
/// <para>
/// Immutable, so a subtree can be reused wherever it is moved to rather than copied — which is why a
/// matrix rewrite need not reformat the cells it did not touch.
/// </para>
/// </summary>
public sealed class TexNode
{
    private static readonly TexNode[] Childless = [];

    private TexNode(TexKind kind, string role, string text, IReadOnlyList<TexNode> children)
    {
        this.Kind = kind;
        this.Role = role;
        this.Text = text;
        this.Children = children;

        var width = text.Length;
        for (var i = 0; i < children.Count; i++) width += children[i].Width;
        this.Width = width;
    }

    /// <summary>What this piece is.</summary>
    public TexKind Kind { get; }

    /// <summary>What it is to the piece holding it. See <see cref="TexRole"/>.</summary>
    public string Role { get; }

    /// <summary>The characters, for a leaf. Empty for anything made of parts.</summary>
    public string Text { get; }

    /// <summary>The parts, in the order they were written.</summary>
    public IReadOnlyList<TexNode> Children { get; }

    /// <summary>How many characters this piece prints as, itself and everything under it.</summary>
    public int Width { get; }

    /// <summary>Whether this stands for characters rather than for parts.</summary>
    public bool IsLeaf => this.Children.Count == 0;

    // ── Making them ─────────────────────────────────────────────────────────

    /// <summary>A piece that stands for characters.</summary>
    public static TexNode Leaf(TexKind kind, string text, string role = TexRole.Element) =>
        new(kind, role, text, Childless);

    /// <summary>A piece made of parts.</summary>
    public static TexNode Branch(TexKind kind, IReadOnlyList<TexNode> children, string role = TexRole.Element) =>
        new(kind, role, string.Empty, children);

    /// <summary>The same piece, meaning something else to whatever holds it.</summary>
    public TexNode As(string role) =>
        role == this.Role ? this : new TexNode(this.Kind, role, this.Text, this.Children);

    /// <summary>The same piece, made of different parts.</summary>
    public TexNode With(IReadOnlyList<TexNode> children) =>
        new(this.Kind, this.Role, this.Text, children);

    // ── Reading them ────────────────────────────────────────────────────────

    /// <summary>The first part with this role, or null.</summary>
    public TexNode? Part(string role)
    {
        foreach (var child in this.Children)
            if (child.Role == role) return child;
        return null;
    }

    /// <summary>Every part with this role, in order.</summary>
    public IEnumerable<TexNode> Parts(string role)
    {
        foreach (var child in this.Children)
            if (child.Role == role) yield return child;
    }

    /// <summary>This piece and everything under it, outermost first.</summary>
    public IEnumerable<TexNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in this.Children)
            foreach (var node in child.SelfAndDescendants())
                yield return node;
    }

    /// <summary>Everything under this piece that stands for characters, left to right.</summary>
    public IEnumerable<TexNode> Leaves()
    {
        if (this.IsLeaf) { yield return this; yield break; }

        foreach (var child in this.Children)
            foreach (var leaf in child.Leaves())
                yield return leaf;
    }

    // ── Writing them down ───────────────────────────────────────────────────

    /// <summary>The source this tree stands for.</summary>
    public string Print()
    {
        var text = new StringBuilder(this.Width);
        this.PrintTo(text);
        return text.ToString();
    }

    /// <summary>The source this tree stands for, appended to <paramref name="text"/>.</summary>
    public void PrintTo(StringBuilder text)
    {
        if (this.IsLeaf) { text.Append(this.Text); return; }

        foreach (var child in this.Children) child.PrintTo(text);
    }

    // ── Finding out where they landed ───────────────────────────────────────

    /// <summary>
    /// This piece and everything under it, each with where it starts — outermost first, then left to
    /// right, which is the order they print in.
    /// </summary>
    public IEnumerable<TexPlace> Placed(int start = 0)
    {
        yield return new TexPlace(this, start);

        var at = start;
        foreach (var child in this.Children)
        {
            foreach (var place in child.Placed(at)) yield return place;
            at += child.Width;
        }
    }

    public override string ToString() =>
        $"{this.Kind}[{this.Role}] {this.Print()}";
}

/// <summary>A piece, and where it starts in the source printed from the tree that holds it.</summary>
public readonly record struct TexPlace(TexNode Node, int Start)
{
    /// <summary>One past the last character this piece stands for.</summary>
    public int End => this.Start + this.Node.Width;

    /// <summary>Whether this piece covers <paramref name="offset"/>, ends included.</summary>
    public bool Covers(int offset) => offset >= this.Start && offset <= this.End;
}
