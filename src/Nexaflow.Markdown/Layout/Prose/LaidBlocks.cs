
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Maths;


namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>
/// Which blocks of a document read exactly as they did the last time it was read, and what each was laid as then — hung
/// on the document by <see cref="WithUnchanged"/>, so the builder has one question to ask of a block and nothing
/// to work out.
///
/// <para>
/// A block that reads as it did is laid as it was: the same words at the same room make the same lines, and every
/// language inside it the same picture. So the layout it was given is set down again rather than laid again, and a key
/// typed in one paragraph lays that paragraph.
/// </para>
/// </summary>
internal sealed class LaidBlocks
{
    /// <summary>
    /// What was laid for each block, by where the block comes among the document's parts — which is all that settles which block
    /// it is. Not by the node: what another language wrote inside a block is put in it after this is said
    /// (the engine), which makes the block a node of its own and leaves it the block it was.
    /// </summary>
    private readonly Dictionary<int, LaidBlock> _blocks = [];

    /// <summary>What was laid for the block that is the document's part <paramref name="at"/> — null for one never read.</summary>
    public LaidBlock? For(int at) => _blocks.GetValueOrDefault(at);

    /// <summary>Says the block that is the document's part <paramref name="at"/> carries <paramref name="laid"/>.</summary>
    public void Add(int at, LaidBlock laid) => _blocks[at] = laid;

    /// <summary>What a document's reading says about its blocks, or null where nothing kept anything.</summary>
    public static LaidBlocks? Of(ContentPart root) =>
        root.Children.Where(child => child.Kind == WithUnchanged.Kind)
            .SelectMany(child => child.Children)
            .Select(child => child.Node.Held)
            .OfType<LaidBlocks>()
            .FirstOrDefault();
}

/// <summary>
/// What one block was laid as, carried from one reading to the next for as long as the block reads the same. Filled by
/// the builder that lays it: the reading only says which block carries which.
/// </summary>
internal sealed class LaidBlock
{
    /// <summary>The layout this block was last given, or null where it has not been laid — or was laid as it was written.</summary>
    public KeptLaying? Last { get; set; }
}

/// <summary>
/// A block's layout as it was made: its tree, standing at the top of its own frame, how far down and across it reached,
/// what the languages inside it could not read, and what it was laid for.
/// </summary>
/// <param name="Part">The block as the reading it was laid from had it — what every piece's part is found again from.</param>
internal sealed record KeptLaying(ContentPart Part, LayoutTree Tree, double Height, double Reach,
                              IReadOnlyList<LayoutDiagnostic> Trouble, double Room, bool IsReadOnly)
{
    /// <summary>
    /// What every piece stands for, and what the languages inside said, as the block reads now — <paramref name="now"/>
    /// being the same block in the new reading. Null where anything cannot be found again, so the block is laid afresh.
    ///
    /// <para>
    /// The block reads exactly as it did, so it is the same parts; only where they stand has moved, and by the one
    /// amount — everything before an edit stays where it was, and everything after moves by what was typed
    /// (<see cref="MovedParts"/>).
    /// </para>
    /// </summary>
    public (ISourcePart?[] Parts, List<LayoutDiagnostic> Trouble)? At(ContentPart now)
    {
        var moved = new MovedParts(Part, now);

        var parts = new ISourcePart?[Tree.Count];
        for (var piece = 0; piece < Tree.Count; piece++)
            if (!moved.Try(Tree.PartOf(piece), out parts[piece])) return null;

        var trouble = new List<LayoutDiagnostic>(Trouble.Count);
        foreach (var said in Trouble)
        {
            if (!moved.Try(said.Part, out var part)) return null;
            trouble.Add(said with { Start = said.Start + moved.By, Part = part });
        }

        return (parts, trouble);
    }
}
