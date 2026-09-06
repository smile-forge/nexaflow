namespace Nexaflow.Markdown.Ast;

/// <summary>
/// One part of a piece of content, with where it sits and what holds it.
///
/// <para>
/// <see cref="ContentNode"/> deliberately knows neither: it owns its text and nothing else, so a subtree
/// can be moved without a position going stale and shared without a parent pointer being wrong. Both
/// facts are wanted often enough to be worked out once per reading rather than per question, and this is
/// where they are kept — a positioned view over an unpositioned tree, thrown away and rebuilt when the
/// source changes, which is the only time either could be wrong.
/// </para>
/// </summary>
public sealed class ContentPart : ISourcePart
{
    private readonly List<ContentPart> _children = [];

    /// <summary>The span this part reports instead of its own, or nothing where it was written.</summary>
    private readonly int? _derived;

    private ContentPart(ContentNode node, int start, ContentPart? parent, int? derived)
    {
        // Anything derived, and everything under it, stands for no source: it begins where the piece it
        // was hung under begins and is no characters long, which is the only answer that keeps a part's
        // span and what it prints as the same thing. Selecting the whole of what it explains still works
        // — the piece drawn from it carries the *written* part, which does have a span.
        var inherited = derived ?? (node.Role == Roles.Derived && parent is not null
            ? parent.Start
            : (int?)null);

        this.Node = node;
        this.Parent = parent;
        this._derived = inherited;
        this.Start = inherited ?? start;

        var at = this.Start;
        foreach (var child in node.Children)
        {
            this._children.Add(new ContentPart(child, at, this, inherited));
            if (inherited is null) at += child.Width;
        }
    }

    /// <summary>Reads a tree into a positioned view of itself.</summary>
    public static ContentPart Of(ContentNode root) => new(root, 0, null, null);

    /// <summary>The piece this part is a positioned view of.</summary>
    public ContentNode Node { get; }

    /// <summary>What is wrong with it, where anything is.</summary>
    public string? Trouble => this.Node.Trouble;

    /// <summary>Where this part begins in the source the tree prints as.</summary>
    public int Start { get; }

    /// <summary>
    /// How much source this part stands for. Nothing at all for anything derived — see the constructor —
    /// so that this and <see cref="Print"/> never disagree.
    /// </summary>
    public int Length => this._derived is null ? this.Node.Width : 0;

    /// <summary>What holds it, or null for the whole content.</summary>
    public ContentPart? Parent { get; }

    public IReadOnlyList<ContentPart> Children => this._children;

    public string Role => this.Node.Role;

    public string Kind => this.Node.Kind;

    /// <summary>
    /// Whether this part stands for something worked out rather than something somebody wrote.
    ///
    /// <para>
    /// Such a part is in the tree because it says what the content <em>means</em>, and it stands for no
    /// source at all. Anything checking a part against the text it came from has to ask this first,
    /// because there is no text it came from.
    /// </para>
    /// </summary>
    public bool Derived => this._derived is not null || this.Node.IsDerived;

    /// <summary>One past the last character this part stands for.</summary>
    public int End => this.Start + this.Length;

    /// <summary>The span it names.</summary>
    public (int Start, int Length) Span => (this.Start, this.Length);

    /// <summary>
    /// Whether this stands for what is written <em>in</em> it rather than for itself: a braced group, a
    /// cell of a table, a bar of music.
    ///
    /// <para>
    /// Asked of the parts rather than of the kind, so no language has to be enumerated here: punctuation
    /// that belongs to whatever holds a piece is exactly what <see cref="Roles.Open"/>,
    /// <see cref="Roles.Close"/> and <see cref="Roles.Separator"/> mark. Pointing at what is inside one
    /// of these is pointing at it.
    /// </para>
    /// </summary>
    public bool IsWrapper =>
        this._children.Any(child => child.Role is Roles.Open or Roles.Close or Roles.Separator);

    /// <summary>
    /// What is written inside it, without its punctuation — the same span for anything that is not a
    /// wrapper.
    /// <para>
    /// The seam between the two readings of a piece of content. Brackets are the writer's way of saying
    /// "all of this is one thing", and a builder drops them the moment it has understood them: its box
    /// for the numerator of <c>\frac{a+b}{c}</c> covers <c>a+b</c>, where the part that <em>is</em> the
    /// numerator here is <c>{a+b}</c>. Both name the same argument, and this is what lets one be found
    /// from the other.
    /// </para>
    /// </summary>
    public (int Start, int Length) Contents
    {
        get
        {
            if (!this.IsWrapper || this._children.Count == 0) return this.Span;

            var from = this.Start;
            var to = this.End;

            if (this._children[0].Role == Roles.Open) from = this._children[0].End;
            if (this._children[^1].Role is Roles.Close or Roles.Separator) to = this._children[^1].Start;

            return from <= to ? (from, to - from) : this.Span;
        }
    }

    /// <summary>
    /// <see cref="Contents"/> with the space around it trimmed off — where the ink starts and stops.
    /// <para>
    /// Both are needed, because a builder is inconsistent about which it names, and reasonably so: a
    /// braced argument's box covers everything between the braces, spaces and all, while a table cell's
    /// covers what was written in it and not the room left either side of the separator.
    /// </para>
    /// </summary>
    public (int Start, int Length) Written
    {
        get
        {
            var inside = this._children
                .Where(child => child.Role is not (Roles.Open or Roles.Close or Roles.Separator))
                .ToList();

            var first = 0;
            while (first < inside.Count && inside[first].Kind == Kinds.Space) first++;

            var last = inside.Count - 1;
            while (last >= first && inside[last].Kind == Kinds.Space) last--;

            return first > last ? this.Contents : (inside[first].Start, inside[last].End - inside[first].Start);
        }
    }

    /// <summary>The first part with this role, or null.</summary>
    public ContentPart? Part(string role)
    {
        foreach (var child in this._children)
            if (child.Role == role) return child;

        return null;
    }

    /// <summary>This part and everything under it, outermost first.</summary>
    public IEnumerable<ContentPart> SelfAndDescendants()
    {
        yield return this;

        foreach (var child in this._children)
            foreach (var part in child.SelfAndDescendants())
                yield return part;
    }

    /// <summary>What holds it, then what holds that, up to the whole content.</summary>
    public IEnumerable<ContentPart> Ancestors()
    {
        for (var part = this.Parent; part is not null; part = part.Parent) yield return part;
    }

    /// <summary>
    /// The parts that mean something to this one — everything but the punctuation that makes it what it
    /// is, and the space between them. A command's name, a group's braces and a bar line are machinery:
    /// they are in the tree so it can be written back out, not because anything is written <em>in</em>
    /// them.
    /// <para>
    /// A derived part is left out for the opposite reason — nothing was written in it because nothing was
    /// written at all. Whoever wants it asks for it by role; every caller here is asking what this piece
    /// was given.
    /// </para>
    /// </summary>
    public IEnumerable<ContentPart> Parts =>
        this._children.Where(child => child.Kind is not (Kinds.Space or Kinds.Comment)
                                      && child.Role is not (Roles.Name or Roles.Open or Roles.Close
                                                            or Roles.Separator or Roles.Trivia
                                                            or Roles.Derived));

    /// <summary>Its own text — the node's, never a stretch of the source.</summary>
    public string Text => this.Node.Text;

    /// <summary>
    /// This part written back out, built up from the tree rather than cut out of the source.
    /// <para>
    /// Nothing at all for anything derived. The node itself would print its text — it is a leaf holding a
    /// pitch or a syllable, and a leaf prints what it holds — but what makes it derived is where it hangs,
    /// which is a fact the part knows and the node does not. Printing it would put a worked-out answer
    /// into the source.
    /// </para>
    /// </summary>
    public string Print() => this.Derived ? string.Empty : this.Node.Print();

    public override string ToString() => $"{this.Kind}[{this.Role}] @{this.Start}+{this.Length}";
}
