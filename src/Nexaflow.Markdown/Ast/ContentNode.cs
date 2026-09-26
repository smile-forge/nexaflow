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
/// <para>
/// A language's stages may put a node of its own type in place of one its parser made — a slice that knows its share of
/// the whole, a chart that carries what its front matter asks for. It stands for exactly the characters the node it
/// replaces did, so the tree still prints as what was written and still says where everything is; what it adds is
/// between that language's stages and its builder, and nothing shared reads it. A rewrite of it keeps its type
/// (<see cref="Reshaped"/>), which is what lets a later stage, or the engine putting nested content back, rebuild a node
/// above or around it. Once the builder has laid a tree out, it is finished: nothing rewrites it.
/// </para>
/// </summary>
public class ContentNode
{
    private static readonly ContentNode[] Childless = [];

    private ContentNode(string kind, string role, string text, IReadOnlyList<ContentNode> children, string? trouble,
                        object? held = null)
    {
        this.Kind = kind;
        this.Role = role;
        this.Text = text;
        this.Children = children;
        this.Trouble = trouble;
        this.Held = held;

        // A derived node stands for nothing anybody wrote, so it takes up none of the source. Saying so
        // once, here, is what keeps every piece above it honest: a width is the sum of the widths under
        // it, so a zero here is a zero all the way up.
        if (this.IsDerived) { this.Width = 0; return; }

        var width = text.Length;
        var nests = 0;
        for (var i = 0; i < children.Count; i++)
        {
            width += children[i].Width;
            nests += children[i].Kind == Kinds.Language ? 1 : children[i].Nests;
        }

        this.Width = width;
        this.Nests = nests;
    }

    /// <summary>A language's own node, standing for exactly what <paramref name="shape"/> stands for.</summary>
    protected ContentNode(ContentNode shape)
        : this(shape.Kind, shape.Role, shape.Text, shape.Children, shape.Trouble, shape.Held) { }

    /// <summary>
    /// What a rewrite of this node makes of <paramref name="shape"/> — this node rebuilt with another role, other parts or
    /// something else to say. A node the parser made is simply that; a language's own type makes another of itself from it,
    /// carrying what it knows.
    /// </summary>
    protected virtual ContentNode Reshaped(ContentNode shape) => shape;

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
    /// How many pieces written in another language this node is or holds (<see cref="ContentNested"/>) — counted as the node is
    /// made, as its width is, so where they are is found by following the counts rather than by walking the tree.
    /// </summary>
    public int Nests { get; }

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

    /// <summary>
    /// Something a stage worked out that is not text — a picture a name was resolved to, a table a setting was
    /// read into. Null for everything the parser makes, which is everything that stands for characters.
    ///
    /// <para>
    /// Untyped on purpose. What a stage resolves a name <em>to</em> is often a thing this assembly could not
    /// name: an image is a WPF object and nothing here knows about WPF. Whoever hangs it knows what it is, and
    /// so does whoever reads it; this only has to carry it.
    /// </para>
    /// <para>
    /// Only ever on a <see cref="Roles.Derived"/> node, so it takes up no source and the tree still prints as
    /// what it came from.
    /// </para>
    /// </summary>
    public object? Held { get; }

    /// <summary>A piece holding something worked out that is not text — see <see cref="Held"/>.</summary>
    public static ContentNode Holding(string kind, string role, object held) =>
        new(kind, role, string.Empty, Childless, null, held);

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
        role == this.Role ? this : this.Reshaped(new ContentNode(this.Kind, role, this.Text, this.Children, this.Trouble, this.Held));

    /// <summary>The same piece, made of different parts.</summary>
    public ContentNode With(IReadOnlyList<ContentNode> children) =>
        this.Reshaped(new ContentNode(this.Kind, this.Role, this.Text, children, this.Trouble, this.Held));

    /// <summary>The same piece, with something to say about it.</summary>
    public ContentNode Saying(string? trouble) =>
        trouble == this.Trouble ? this : this.Reshaped(new ContentNode(this.Kind, this.Role, this.Text, this.Children, trouble, this.Held));

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
        // Walked with a stack of its own, in the same order, rather than an iterator for every level — which hands each node
        // up through one iterator for every node above it.
        var waiting = new Stack<ContentNode>();
        waiting.Push(this);

        while (waiting.Count > 0)
        {
            var node = waiting.Pop();
            yield return node;

            for (var at = node.Children.Count - 1; at >= 0; at--) waiting.Push(node.Children[at]);
        }
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

        for (var at = 0; at < this.Children.Count; at++) this.Children[at].PrintTo(text);
    }

    /// <summary>
    /// Whether this tree prints as <paramref name="text"/> — what comparing <see cref="Print"/> with it says, found without
    /// printing it, so asking of a whole document costs no copy of it.
    /// </summary>
    public bool Prints(string text)
    {
        var at = 0;

        return this.Matches(text, ref at) && at == text.Length;
    }

    /// <summary>Whether what this piece prints stands in <paramref name="text"/> at <paramref name="at"/>, which it moves past it.</summary>
    private bool Matches(string text, ref int at)
    {
        if (this.IsDerived) return true;

        if (this.IsLeaf)
        {
            if (!text.AsSpan(at).StartsWith(this.Text, StringComparison.Ordinal)) return false;

            at += this.Text.Length;
            return true;
        }

        for (var child = 0; child < this.Children.Count; child++)
            if (!this.Children[child].Matches(text, ref at)) return false;

        return true;
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
            || this.GetType() != other.GetType()
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
