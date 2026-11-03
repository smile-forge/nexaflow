
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
/// <param name="Slides">How far along each box it set slid sideways was slid as it was laid, by what the host knows each by.</param>
internal sealed record KeptLaying(ContentPart Part, LayoutTree Tree, double Height, double Reach,
                              IReadOnlyList<LayoutDiagnostic> Trouble, double Room, bool IsReadOnly,
                              IReadOnlyList<(object Reader, double At)>? Slides = null)
{
    /// <summary>
    /// What every piece stands for, and what the languages inside said, as the block reads now — <paramref name="now"/> being the same
    /// block in the new reading — and every tree put down inside it, put down again with what each of its pieces stands for now. Null
    /// where anything cannot be found again, so the block is laid afresh.
    ///
    /// <para>
    /// The block reads exactly as it did, so it is the same parts; only where they stand has moved, and by the one amount — everything
    /// before an edit stays where it was, and everything after moves by what was typed (<see cref="MovedParts"/>). A tree put down
    /// inside it — code's lines in a fence — names its parts through its mount, and is parted again the same way, so nothing kept names a
    /// reading older than the one it is set down from.
    /// </para>
    /// </summary>
    public (ISourcePart?[] Parts, List<LayoutDiagnostic> Trouble, Dictionary<int, LayoutMount>? Mounts)? At(ContentPart now)
    {
        var moved = new MovedParts(Part, now);
        (bool Found, ISourcePart? Now) Found(ISourcePart? part) => moved.Try(part, out var to) ? (true, to) : (false, null);

        var parts = new ISourcePart?[Tree.Count];
        Dictionary<int, LayoutMount>? mounts = null;

        for (var piece = 0; piece < Tree.Count; piece++)
        {
            if (Tree.MountOf(piece) is { } mount)
            {
                if (Restated(mount, Found) is not { } again) return null;
                (mounts ??= [])[piece] = again;
                continue;
            }

            if (!moved.Try(Tree.PartOf(piece), out parts[piece])) return null;
        }

        var trouble = new List<LayoutDiagnostic>(Trouble.Count);
        foreach (var said in Trouble)
        {
            if (!moved.Try(said.Part, out var part)) return null;
            trouble.Add(said with { Start = said.Start + moved.By, Part = part });
        }

        return (parts, trouble, mounts);
    }

    /// <summary>Whether every box it set slid sideways is slid where it was as it was laid, as <paramref name="slid"/> says each is now.</summary>
    public bool SlidAsLaid(Func<object, double> slid)
    {
        foreach (var (reader, at) in Slides ?? [])
            if (slid(reader) != at) return false;

        return true;
    }

    /// <summary>
    /// <paramref name="mount"/>'s tree put down again, each part it names — through the mount, then <paramref name="outward"/> — standing
    /// for what it stands for now, and every tree it puts down in turn the same; null where a part cannot be found again.
    /// </summary>
    private static LayoutMount? Restated(LayoutMount mount, Func<ISourcePart?, (bool Found, ISourcePart? Now)> outward)
    {
        (bool Found, ISourcePart? Now) Through(ISourcePart? part) => outward(mount.Restated(part));

        var tree = mount.Tree;
        var parts = new ISourcePart?[tree.Count];
        Dictionary<int, LayoutMount>? mounts = null;

        for (var piece = 0; piece < tree.Count; piece++)
        {
            if (tree.MountOf(piece) is { } inner)
            {
                if (Restated(inner, Through) is not { } again) return null;
                (mounts ??= [])[piece] = again;
                continue;
            }

            var (found, now) = Through(tree.PartOf(piece));
            if (!found) return null;

            parts[piece] = now;
        }

        return new LayoutMount(tree.Parted(parts, mounts));
    }
}
