namespace Nexaflow.Maths.Latex;

/// <summary>
/// One part of a formula, with where it sits and what holds it.
///
/// <para>
/// <see cref="TexNode"/> deliberately knows neither: it owns its text and nothing else, so a subtree can
/// be moved without a position going stale and shared without a parent pointer being wrong. Both facts
/// are wanted often enough to be worked out once per reading rather than per question, and this is where
/// they are kept — a positioned view over an unpositioned tree, thrown away and rebuilt when the source
/// changes, which is the only time either could be wrong.
/// </para>
/// </summary>
public sealed class TexPart
{
    private readonly List<TexPart> _children = [];

    private TexPart(TexNode node, int start, TexPart? parent)
    {
        this.Node = node;
        this.Start = start;
        this.Parent = parent;

        var at = start;
        foreach (var child in node.Children)
        {
            this._children.Add(new TexPart(child, at, this));
            at += child.Width;
        }
    }

    internal static TexPart Of(TexNode root) => new(root, 0, null);

    public TexNode Node { get; }

    /// <summary>Where this part begins in the source the tree prints as.</summary>
    public int Start { get; }

    /// <summary>What holds it, or null for the whole formula.</summary>
    public TexPart? Parent { get; }

    public IReadOnlyList<TexPart> Children => this._children;

    public string Role => this.Node.Role;

    public TexKind Kind => this.Node.Kind;

    public int Length => this.Node.Width;

    /// <summary>One past its last character.</summary>
    public int End => this.Start + this.Node.Width;

    /// <summary>The span it names.</summary>
    public (int Start, int Length) Span => (this.Start, this.Length);

    /// <summary>
    /// What is written inside it, without its braces — the same span for anything that is not a braced
    /// group.
    /// <para>
    /// The seam between the two readings of a formula. Braces are the writer's way of saying "all of
    /// this is one argument", and a typesetter drops them the moment it has understood them: its box for
    /// the numerator of <c>\frac{a+b}{c}</c> covers <c>a+b</c>, where the part that <em>is</em> the
    /// numerator here is <c>{a+b}</c>. Both name the same argument, and this is what lets one be found
    /// from the other.
    /// </para>
    /// </summary>
    public (int Start, int Length) Contents
    {
        get
        {
            if (this.Kind != TexKind.Group || this._children.Count == 0) return this.Span;

            var from = this.Start;
            var to = this.End;

            if (this._children[0].Role == TexRole.Open) from = this._children[0].End;
            if (this._children[^1].Role == TexRole.Close) to = this._children[^1].Start;

            return from <= to ? (from, to - from) : this.Span;
        }
    }

    /// <summary>This part and everything under it, outermost first.</summary>
    public IEnumerable<TexPart> SelfAndDescendants()
    {
        yield return this;

        foreach (var child in this._children)
            foreach (var part in child.SelfAndDescendants())
                yield return part;
    }

    /// <summary>What holds it, then what holds that, up to the whole formula.</summary>
    public IEnumerable<TexPart> Ancestors()
    {
        for (var part = this.Parent; part is not null; part = part.Parent) yield return part;
    }

    /// <summary>
    /// The parts that mean something to this one — everything but the punctuation that makes it what it
    /// is. A command's <c>\name</c>, a group's braces and a row's <c>\\</c> are machinery: they are in
    /// the tree so it can be written back out, not because anything is written <em>in</em> them.
    /// </summary>
    public IEnumerable<TexPart> Parts =>
        this._children.Where(child => child.Role is not (TexRole.Name or TexRole.Open or TexRole.Close
                                                         or TexRole.Separator or TexRole.Trivia));

    public override string ToString() => $"{this.Kind}[{this.Role}] @{this.Start}+{this.Length}";
}

/// <summary>
/// A formula, read: the parse tree with every part's position and parent worked out, and the lookups an
/// editor needs to go from a stretch of source to the part written there.
/// </summary>
public sealed class TexReading
{
    private TexReading(string latex, TexPart root)
    {
        this.Latex = latex;
        this.Root = root;
    }

    /// <summary>Reads a formula.</summary>
    public static TexReading Of(string latex) =>
        new(latex, TexPart.Of(TexParser.Parse(latex)));

    /// <summary>The source this was read from.</summary>
    public string Latex { get; }

    /// <summary>The whole formula.</summary>
    public TexPart Root { get; }

    /// <summary>
    /// Every part written exactly at <paramref name="start"/> for <paramref name="length"/> characters,
    /// outermost first.
    /// <para>
    /// Several, because a part and the thing holding it can stand for the very same characters — a
    /// formula that is one fraction, a group holding one symbol — and which of them a question is about
    /// depends on the question.
    /// </para>
    /// </summary>
    public IEnumerable<TexPart> Naming(int start, int length)
    {
        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Start > start) break;
            if (part.Start == start && part.Length == length) yield return part;
        }
    }

    /// <summary>
    /// The part written at that span, or the braced group whose contents are written there — the second
    /// because a typesetter's box for an argument covers what is inside the braces, and this has to be
    /// findable from that.
    /// </summary>
    public TexPart? Wrapping(int start, int length)
    {
        TexPart? innermost = null;

        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Start == start && part.Length == length) innermost = part;
            else if (part.Kind == TexKind.Group && part.Contents == (start, length)) return part;
        }

        return innermost;
    }
}
