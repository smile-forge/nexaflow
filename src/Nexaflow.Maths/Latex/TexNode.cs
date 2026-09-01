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

    private TexNode(TexKind kind, string role, string text, IReadOnlyList<TexNode> children, string? trouble)
    {
        this.Kind = kind;
        this.Role = role;
        this.Text = text;
        this.Children = children;
        this.Trouble = trouble;

        // An expansion stands for nothing anybody wrote, so it takes up none of the source. Saying so
        // once, here, is what keeps every piece above it honest: a width is the sum of the widths under
        // it, so a zero here is a zero all the way up.
        if (this.IsDerived) { this.Width = 0; return; }

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

    /// <summary>
    /// What is wrong with this piece, where anything is — the reason a reader would want a line drawn
    /// under it, and eventually the text of the tooltip that explains itself.
    ///
    /// <para>
    /// Only a <see cref="TexKind.Verbatim"/> piece ever carries one, and not every one of those does: a
    /// stretch under the caret is shown rather than read for a reason that is nobody's fault, and
    /// nagging about it while somebody is still typing would be the wrong thing to draw. So the line
    /// under it follows from having something to say rather than from what kind of piece it is, which
    /// leaves room for something to say that is not an error at all.
    /// </para>
    /// </summary>
    public string? Trouble { get; }

    /// <summary>
    /// Whether this piece stands for nothing anybody wrote.
    ///
    /// <para>
    /// Two things do. A macro's expansion is what a name means rather than what was typed, and a hole is
    /// somewhere something still has to go. Both are drawn, and neither is source: they take up none of
    /// it, print as nothing, and are nowhere to be found by an offset — which together are what let the
    /// tree carry them and still say exactly what was written.
    /// </para>
    /// </summary>
    public bool IsDerived => this.Role == TexRole.Expansion || this.Kind == TexKind.Hole;

    /// <summary>Whether this stands for characters rather than for parts.</summary>
    public bool IsLeaf => this.Children.Count == 0;

    // ── Making them ─────────────────────────────────────────────────────────

    /// <summary>A piece that stands for characters.</summary>
    /// <summary>A piece that stands for characters.</summary>
    public static TexNode Leaf(TexKind kind, string text, string role = TexRole.Element, string? trouble = null) =>
        new(kind, role, text, Childless, trouble);

    /// <summary>A piece made of parts.</summary>
    public static TexNode Branch(TexKind kind, IReadOnlyList<TexNode> children, string role = TexRole.Element) =>
        new(kind, role, string.Empty, children, null);

    /// <summary>
    /// A stretch shown as the characters it is written with rather than read as maths, and what is
    /// wrong with it if anything is.
    /// <para>
    /// It prints as exactly what it stands for, which is what lets it replace whatever was there
    /// without the source changing underneath — a piece nobody can read, a command nothing can draw, or
    /// a stretch somebody is in the middle of typing.
    /// </para>
    /// </summary>
    public static TexNode Shown(string text, string? trouble = null, string role = TexRole.Element) =>
        new(TexKind.Verbatim, role, text, Childless, trouble);

    /// <summary>The same piece, meaning something else to whatever holds it.</summary>
    public TexNode As(string role) =>
        role == this.Role ? this : new TexNode(this.Kind, role, this.Text, this.Children, this.Trouble);

    /// <summary>The same piece, made of different parts.</summary>
    public TexNode With(IReadOnlyList<TexNode> children) =>
        new(this.Kind, this.Role, this.Text, children, this.Trouble);

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
    /// <summary>Everything under this piece that stands for characters, left to right.</summary>
    public IEnumerable<TexNode> Leaves()
    {
        if (this.IsDerived) yield break;
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
    /// <summary>The source this tree stands for, appended to <paramref name="text"/>.</summary>
    public void PrintTo(StringBuilder text)
    {
        if (this.IsDerived) return;
        if (this.IsLeaf) { text.Append(this.Text); return; }

        foreach (var child in this.Children) child.PrintTo(text);
    }

    // ── Finding out where they landed ───────────────────────────────────────

    /// <summary>
    /// This piece and everything under it, each with where it starts — outermost first, then left to
    /// right, which is the order they print in.
    /// </summary>
    /// <summary>
    /// This piece and everything under it, each with where it starts — outermost first, then left to
    /// right, which is the order they print in. An expansion is skipped: it prints nothing, so there is
    /// nowhere for it to start.
    /// </summary>
    public IEnumerable<TexPlace> Placed(int start = 0)
    {
        if (this.IsDerived) yield break;

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
