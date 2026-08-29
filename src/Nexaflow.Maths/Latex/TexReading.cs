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
public sealed class TexPart : ITexPart
{
    private readonly List<TexPart> _children = [];


    /// <summary>The macro span this part reports instead of its own, or nothing where it was written.</summary>
    private readonly int? _derived;

    private TexPart(TexNode node, int start, TexPart? parent, int? derived)
    {
        // A macro's expansion, and everything under it, stands for no source: it begins where the macro
        // begins and is no characters long, which is the only answer that keeps a part's span and what
        // it prints as the same thing. Selecting the whole of `\neq` still works — the piece that is set
        // from it carries the *command*, which does have a span, and that is where it belongs. There is
        // no part of `\neq` that is the slash and no part that is the equals, so there is nothing in
        // here for a caret to land inside.
        var inherited = derived ?? (node.Role == TexRole.Expansion && parent is not null
            ? parent.Start
            : (int?)null);

        this.Node = node;
        this.Parent = parent;
        this._derived = inherited;
        this.Start = inherited ?? start;

        var at = this.Start;
        foreach (var child in node.Children)
        {
            this._children.Add(new TexPart(child, at, this, inherited));
            if (inherited is null) at += child.Width;
        }
    }

    internal static TexPart Of(TexNode root) => new(root, 0, null, null);

    public TexNode Node { get; }

    /// <inheritdoc/>
    public string? Trouble => this.Node.Trouble;

    /// <summary>Where this part begins in the source the tree prints as.</summary>
    public int Start { get; }

    /// <summary>What holds it, or null for the whole formula.</summary>
    public TexPart? Parent { get; }

    public IReadOnlyList<TexPart> Children => this._children;

    public string Role => this.Node.Role;

    public TexKind Kind => this.Node.Kind;

    /// <summary>
    /// How much source this part stands for. Zero-width for anything a macro stands for, where the
    /// span is the macro's own — see the constructor.
    /// </summary>
    /// <summary>
    /// How much source this part stands for. Nothing at all for anything a macro stands for — see the
    /// constructor — so that this and <see cref="Print"/> never disagree.
    /// </summary>
    public int Length => this._derived is null ? this.Node.Width : 0;   // a derived node is already zero-wide

    /// <summary>
    /// Whether this part is something a macro stands for rather than something somebody wrote.
    ///
    /// <para>
    /// Such a part is in the tree because it says what the formula <em>means</em>, and it stands for no
    /// source at all: it begins where its macro begins and is no characters long. Anything checking a
    /// part against the text it came from has to ask this first, because there is no text it came from.
    /// </para>
    /// </summary>
    public bool Derived => this._derived is not null || this.Node.IsDerived;

    /// <summary>One past its last character.</summary>
    /// <summary>One past the last character this part stands for.</summary>
    public int End => this.Start + this.Length;

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
            if (!this.IsWrapper || this._children.Count == 0) return this.Span;

            var from = this.Start;
            var to = this.End;

            if (this._children[0].Role == TexRole.Open) from = this._children[0].End;
            if (this._children[^1].Role is TexRole.Close or TexRole.Separator) to = this._children[^1].Start;

            return from <= to ? (from, to - from) : this.Span;
        }
    }

    /// <summary>
    /// Whether this stands for what is written in it rather than for itself: a braced group, a cell of a
    /// table. Its punctuation — the braces, the <c>&amp;</c> — belongs to whatever holds it, so pointing
    /// at what is inside is pointing at this.
    /// </summary>
    public bool IsWrapper => this.Kind is TexKind.Group or TexKind.Cell;

    /// <summary>
    /// <see cref="Contents"/> with the space around it trimmed off — where the ink starts and stops.
    /// <para>
    /// Both are needed, because a typesetter is inconsistent about which it names, and reasonably so: a
    /// braced argument's box covers everything between the braces, spaces and all, while a table cell's
    /// covers what was written in it and not the room left either side of the ampersand.
    /// </para>
    /// </summary>
    public (int Start, int Length) Written
    {
        get
        {
            var inside = this._children
                .Where(child => child.Role is not (TexRole.Open or TexRole.Close or TexRole.Separator))
                .ToList();

            var first = 0;
            while (first < inside.Count && inside[first].Kind == TexKind.Space) first++;

            var last = inside.Count - 1;
            while (last >= first && inside[last].Kind == TexKind.Space) last--;

            return first > last ? this.Contents : (inside[first].Start, inside[last].End - inside[first].Start);
        }
    }

    /// <summary>The first part with this role, or null.</summary>
    public TexPart? Part(string role)
    {
        foreach (var child in this._children)
            if (child.Role == role) return child;

        return null;
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
    /// is, and the space between them. A command's <c>\name</c>, a group's braces and a row's <c>\\</c>
    /// are machinery: they are in the tree so it can be written back out, not because anything is
    /// written <em>in</em> them.
    /// </summary>
    /// <summary>
    /// The parts that mean something to this one — everything but the punctuation that makes it what it
    /// is, and the space between them. A command's <c>\name</c>, a group's braces and a row's <c>\</c>
    /// are machinery: they are in the tree so it can be written back out, not because anything is
    /// written <em>in</em> them.
    /// <para>
    /// An expansion is left out for the opposite reason — nothing was written in it because nothing was
    /// written at all. It is what a macro stands for, and whoever wants it asks for it by name; every
    /// caller here is asking "what was this command given", and a macro was given nothing.
    /// </para>
    /// </summary>
    public IEnumerable<TexPart> Parts =>
        this._children.Where(child => child.Kind is not (TexKind.Space or TexKind.Comment)
                                      && child.Role is not (TexRole.Name or TexRole.Open or TexRole.Close
                                                            or TexRole.Separator or TexRole.Trivia
                                                            or TexRole.Expansion));

    /// <summary>Its own text — the node's, never a stretch of the formula.</summary>
    public string Text => this.Node.Text;

    /// <summary>This part written back out, built up from the tree rather than cut out of the source.</summary>
    public string Print() => this.Node.Print();

    public override string ToString() => $"{this.Kind}[{this.Role}] @{this.Start}+{this.Length}";

    // ── The same tree, with the positions taken away ─────────────────────────
    //
    // Explicit, so that holding a TexPart still gets you everything and holding an ITexPart gets you only
    // what a builder may have. The lists convert on their own: IReadOnlyList and IEnumerable are both
    // covariant, so a list of parts is already a list of the read-only view of them.

    ITexPart? ITexPart.Parent => this.Parent;

    IReadOnlyList<ITexPart> ITexPart.Children => this._children;

    IEnumerable<ITexPart> ITexPart.Parts => this.Parts;

    ITexPart? ITexPart.Part(string role) => this.Part(role);

    IEnumerable<ITexPart> ITexPart.SelfAndDescendants() => this.SelfAndDescendants();
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

    /// <summary>
    /// A reading of a tree that has already been read and worked over — what the stages in
    /// <see cref="TexPipeline"/> hand back. The source it reports is the source the tree prints as,
    /// which is the same source it came from, because that is the one rule every stage keeps.
    /// </summary>
    public static TexReading Of(TexNode tree) => new(tree.Print(), TexPart.Of(tree));

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
    /// What stands for exactly this stretch of source: the parts written there, outermost first — or,
    /// when nothing is written exactly there, the braced group whose contents these are.
    ///
    /// <para>
    /// The seam between the two readings of a formula, and the fallback is the whole of it. A typesetter
    /// drops braces as soon as it has understood them, so its box for the numerator of
    /// <c>\frac{a+b}{c}</c> covers <c>a+b</c> — three things here, and no single part. The group is what
    /// that box is a picture of.
    /// </para>
    /// <para>
    /// Only when nothing matches exactly, which is what keeps the two apart where both could answer: in
    /// <c>\frac{a}{b}</c> the same stretch is both the letter a and the contents of the numerator, and
    /// asking what is written there has to answer the letter.
    /// </para>
    /// </summary>
    public IReadOnlyList<TexPart> Standing(int start, int length)
    {
        var exact = new List<TexPart>();
        TexPart? wrapping = null;

        foreach (var part in this.Root.SelfAndDescendants())
        {
            if (part.Start == start && part.Length == length) { exact.Add(part); continue; }

            if (wrapping is null
                && part.IsWrapper
                && (part.Contents == (start, length) || part.Written == (start, length)))
                wrapping = part;
        }

        if (exact.Count > 0) return exact;

        return wrapping is null ? [] : [wrapping];
    }
}
