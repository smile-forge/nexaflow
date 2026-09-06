using System.Text;

namespace Nexaflow.Markdown.Ast;

/// <summary>
/// One piece of a document's content: what it is, what it is to whatever holds it, and either the
/// characters it stands for or the pieces it is made of.
///
/// <para>
/// The tree owns the text. There are no offsets stored anywhere — a node knows how wide it is, and an
/// offset is worked out by a walk when somebody asks (<see cref="Placed"/>). That is what makes the
/// source a serialization format rather than the document: a tree that has never been printed is still
/// the whole truth, and an edit that replaces a subtree cannot leave a stale position behind, because
/// there were none to go stale.
/// </para>
/// <para>
/// Immutable, so a subtree can be reused wherever it is moved to rather than copied — which is why
/// rewriting one note in a tune need not reformat the bar it stands in.
/// </para>
/// <para>
/// <see cref="Kind"/> and <see cref="Role"/> are both open strings, deliberately. A shared tree cannot
/// hold an enum of every kind of thing every language has, and it does not need to: nothing in this
/// assembly switches on either, and each language declares its own set of constants. What is shared is
/// the shape — text, parts, width, printing — which is the same for a formula, a tune and a barcode.
/// </para>
/// </summary>
public sealed class ContentNode
{
    private static readonly ContentNode[] Childless = [];

    private ContentNode(string kind, string role, string text, IReadOnlyList<ContentNode> children, string? trouble)
    {
        this.Kind = kind;
        this.Role = role;
        this.Text = text;
        this.Children = children;
        this.Trouble = trouble;

        // A derived node stands for nothing anybody wrote, so it takes up none of the source. Saying so
        // once, here, is what keeps every piece above it honest: a width is the sum of the widths under
        // it, so a zero here is a zero all the way up.
        if (this.IsDerived) { this.Width = 0; return; }

        var width = text.Length;
        for (var i = 0; i < children.Count; i++) width += children[i].Width;
        this.Width = width;
    }

    /// <summary>What this piece is, in its own language's vocabulary.</summary>
    public string Kind { get; }

    /// <summary>What it is to the piece holding it. See <see cref="Roles"/> for the shared ones.</summary>
    public string Role { get; }

    /// <summary>The characters, for a leaf. Empty for anything made of parts.</summary>
    public string Text { get; }

    /// <summary>The parts, in the order they were written.</summary>
    public IReadOnlyList<ContentNode> Children { get; }

    /// <summary>How many characters this piece prints as, itself and everything under it.</summary>
    public int Width { get; }

    /// <summary>
    /// What is wrong with this piece, where anything is — the reason a reader would want a line drawn
    /// under it, and the text of the tooltip that explains itself.
    ///
    /// <para>
    /// Having something to say is what draws the line, rather than what kind of piece it is: a stretch
    /// under the caret is shown rather than read for a reason that is nobody's fault, and nagging about
    /// it while somebody is still typing would be the wrong thing to draw.
    /// </para>
    /// </summary>
    public string? Trouble { get; }

    /// <summary>
    /// Whether this piece stands for nothing anybody wrote.
    ///
    /// <para>
    /// This is how a pipeline stage says something the characters do not — what a macro means, which
    /// accidental a note actually prints, which syllable sits under it, that something still has to go
    /// here. All of it is drawn and none of it is source: it takes up none, prints as nothing, and is
    /// nowhere to be found by an offset, which together are what let the tree carry it and still say
    /// exactly what was written.
    /// </para>
    /// </summary>
    public bool IsDerived => this.Role == Roles.Derived || this.Kind == Kinds.Hole;

    /// <summary>Whether this stands for characters rather than for parts.</summary>
    public bool IsLeaf => this.Children.Count == 0;

    // ── Making them ─────────────────────────────────────────────────────────

    /// <summary>A piece that stands for characters.</summary>
    public static ContentNode Leaf(string kind, string text, string role = Roles.Element, string? trouble = null) =>
        new(kind, role, text, Childless, trouble);

    /// <summary>A piece made of parts.</summary>
    public static ContentNode Branch(string kind, IReadOnlyList<ContentNode> children, string role = Roles.Element) =>
        new(kind, role, string.Empty, children, null);

    /// <summary>
    /// A stretch shown as the characters it is written with rather than read, and what is wrong with it
    /// if anything is.
    /// <para>
    /// It prints as exactly what it stands for, which is what lets it replace whatever was there without
    /// the source changing underneath — a piece nobody can read, a construct nothing can draw, or a
    /// stretch somebody is in the middle of typing.
    /// </para>
    /// </summary>
    public static ContentNode Shown(string text, string? trouble = null, string role = Roles.Element) =>
        new(Kinds.Verbatim, role, text, Childless, trouble);

    /// <summary>The same piece, meaning something else to whatever holds it.</summary>
    public ContentNode As(string role) =>
        role == this.Role ? this : new ContentNode(this.Kind, role, this.Text, this.Children, this.Trouble);

    /// <summary>The same piece, made of different parts.</summary>
    public ContentNode With(IReadOnlyList<ContentNode> children) =>
        new(this.Kind, this.Role, this.Text, children, this.Trouble);

    /// <summary>The same piece, with something to say about it.</summary>
    public ContentNode Saying(string? trouble) =>
        trouble == this.Trouble ? this : new ContentNode(this.Kind, this.Role, this.Text, this.Children, trouble);

    // ── Reading them ────────────────────────────────────────────────────────

    /// <summary>The first part with this role, or null.</summary>
    public ContentNode? Part(string role)
    {
        foreach (var child in this.Children)
            if (child.Role == role) return child;
        return null;
    }

    /// <summary>Every part with this role, in order.</summary>
    public IEnumerable<ContentNode> Parts(string role)
    {
        foreach (var child in this.Children)
            if (child.Role == role) yield return child;
    }

    /// <summary>This piece and everything under it, outermost first.</summary>
    public IEnumerable<ContentNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in this.Children)
            foreach (var node in child.SelfAndDescendants())
                yield return node;
    }

    /// <summary>Everything under this piece that stands for characters, left to right.</summary>
    public IEnumerable<ContentNode> Leaves()
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
    public void PrintTo(StringBuilder text)
    {
        if (this.IsDerived) return;
        if (this.IsLeaf) { text.Append(this.Text); return; }

        foreach (var child in this.Children) child.PrintTo(text);
    }

    // ── Finding out where they landed ───────────────────────────────────────

    /// <summary>
    /// This piece and everything under it, each with where it starts — outermost first, then left to
    /// right, which is the order they print in. A derived node is skipped: it prints nothing, so there is
    /// nowhere for it to start.
    /// </summary>
    public IEnumerable<ContentPlace> Placed(int start = 0)
    {
        if (this.IsDerived) yield break;

        yield return new ContentPlace(this, start);

        var at = start;
        foreach (var child in this.Children)
        {
            foreach (var place in child.Placed(at)) yield return place;
            at += child.Width;
        }
    }

    public override string ToString() => $"{this.Kind}[{this.Role}] {this.Print()}";

    /// <summary>
    /// Whether this is the same tree as <paramref name="other"/>: the same shape, made of the same pieces,
    /// each saying the same thing.
    ///
    /// <para>
    /// Neither of the two comparisons that come for free will do. Reference equality asks whether they are
    /// the same object, which an edit that rebuilds a spine makes false for everything above the change.
    /// Equal printing asks something weaker than it looks: a group and the single thing inside it print
    /// alike and are not the same tree, and telling those two apart is the whole reason an edited tree is
    /// held against what re-reading its source produced.
    /// </para>
    /// <para>
    /// <see cref="Width"/> is deliberately not compared. It follows from the rest, so comparing it could
    /// only ever agree — and a comparison that includes what follows from the answer is a comparison that
    /// can hide a difference in the answer.
    /// </para>
    /// </summary>
    public bool Same(ContentNode? other)
    {
        if (ReferenceEquals(this, other)) return true;

        if (other is null
            || this.Kind != other.Kind
            || this.Role != other.Role
            || this.Text != other.Text
            || this.Trouble != other.Trouble
            || this.Children.Count != other.Children.Count) return false;

        for (var i = 0; i < this.Children.Count; i++)
            if (!this.Children[i].Same(other.Children[i])) return false;

        return true;
    }
}

/// <summary>A piece, and where it starts in the source printed from the tree that holds it.</summary>
public readonly record struct ContentPlace(ContentNode Node, int Start)
{
    /// <summary>One past the last character this piece stands for.</summary>
    public int End => this.Start + this.Node.Width;

    /// <summary>Whether this piece covers <paramref name="offset"/>, ends included.</summary>
    public bool Covers(int offset) => offset >= this.Start && offset <= this.End;
}
