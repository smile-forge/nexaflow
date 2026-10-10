namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Content in one language. Every parser's tree is one at its root — a document, a diagram, a formula — and so is every stretch
/// written in another language inside it: its delimiters as trivia, and a <see cref="Kinds.Nested"/> leaf holding its
/// characters as written, unread (<see cref="ContentNested"/>).
///
/// <para>
/// Which language is on the block, so whatever stands in a tree knows the language it is written in by walking up to the
/// root. A stage that swaps the root for a node of its language's own type makes it one of these, in the same language — and
/// the pipeline holds every stage to that (<see cref="Pipeline.AstPipeline"/>).
/// </para>
/// </summary>
public class BlockNode : ContentNode
{
    /// <param name="language">The word naming the language — as the content was called, or as its own header names it.</param>
    /// <param name="kind">What the language calls its whole; <see cref="Kinds.Block"/> for a block holding another language.</param>
    /// <param name="offset">Where its first character was read from, where the parser says — see <see cref="ContentNode.Offset"/>.</param>
    public BlockNode(string language, IReadOnlyList<ContentNode> children, string kind = Kinds.Block, string role = Roles.Element,
                     int? offset = null)
        : this(ContentNode.Branch(kind, children, role, offset), language) { }

    /// <summary>A block standing for exactly what <paramref name="shape"/> stands for, in <paramref name="language"/>.</summary>
    private BlockNode(ContentNode shape, string language) : base(shape) => this.Language = language;

    /// <summary>A language's own kind of block, made from the block it takes the place of: the same content, in the same language.</summary>
    protected BlockNode(BlockNode written) : base(written) => this.Language = written.Language;

    /// <summary>The word naming the language this block is written in.</summary>
    public string Language { get; }

    /// <inheritdoc/>
    protected sealed override ContentNode Reshaped(ContentNode shape) => this.Retyped(new BlockNode(shape, this.Language));

    /// <summary>
    /// What a rewrite of this block makes of <paramref name="shape"/> — already a block in this language; a language's own kind of
    /// block makes another of itself from it, carrying what it knows.
    /// </summary>
    protected virtual BlockNode Retyped(BlockNode shape) => shape;
}
