using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// Which blocks of a document read exactly as they did the last time it was read, and what each was laid as then — hung
/// on the document by <see cref="Stages.WithUnchanged"/>, so the builder has one question to ask of a block and nothing
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
    /// (<see cref="ContentEngine"/>), which makes the block a node of its own and leaves it the block it was.
    /// </summary>
    private readonly Dictionary<int, LaidBlock> _blocks = [];

    /// <summary>What was laid for the block that is the document's part <paramref name="at"/> — null for one never read.</summary>
    public LaidBlock? For(int at) => _blocks.GetValueOrDefault(at);

    /// <summary>Says the block that is the document's part <paramref name="at"/> carries <paramref name="laid"/>.</summary>
    public void Add(int at, LaidBlock laid) => _blocks[at] = laid;

    /// <summary>What a document's reading says about its blocks, or null where nothing kept anything.</summary>
    public static LaidBlocks? Of(ContentPart root) =>
        root.Children.Where(child => child.Kind == Stages.WithUnchanged.Kind)
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
    public Laying? Last { get; set; }
}

/// <summary>
/// A block's layout as it was made: its tree, standing at the top of its own frame, how far down and across it reached,
/// what the languages inside it could not read, and what it was laid for.
/// </summary>
/// <param name="Part">The block as the reading it was laid from had it — what every piece's part is found again from.</param>
internal sealed record Laying(ContentPart Part, LayoutTree Tree, double Height, double Reach,
                              IReadOnlyList<Diagnostic> Trouble, double Room, bool IsReadOnly)
{
    /// <summary>
    /// What every piece stands for, and what the languages inside said, as the block reads now — <paramref name="now"/>
    /// being the same block in the new reading. Null where anything cannot be found again, so the block is laid afresh.
    ///
    /// <para>
    /// The block reads exactly as it did, so it is the same parts; only where they stand has moved, and by the one
    /// amount — everything before an edit stays where it was, and everything after moves by what was typed. A part of
    /// the document's reading is the new reading's part in the same place; a part of a language's own reading, which
    /// that language read when the block was laid, is that reading again, set that much further along.
    /// </para>
    /// </summary>
    public (ISourcePart?[] Parts, List<Diagnostic> Trouble)? At(ContentPart now)
    {
        var moved = new Moved(Part, now);

        var parts = new ISourcePart?[Tree.Count];
        for (var piece = 0; piece < Tree.Count; piece++)
            if (!moved.Try(Tree.PartOf(piece), out parts[piece])) return null;

        var trouble = new List<Diagnostic>(Trouble.Count);
        foreach (var said in Trouble)
        {
            if (!moved.Try(said.Part, out var part)) return null;
            trouble.Add(said with { Start = said.Start + moved.By, Part = part });
        }

        return (parts, trouble);
    }

    /// <summary>
    /// Where each part of the old reading stands in the new one. The block reads exactly as it did, so a part of it is found
    /// again by the way down to it — which of its parent's parts it was, all the way from the block — rather than by looking
    /// it up; only a language's own reading, read afresh when the block was laid, has to be made again, and then only once.
    /// </summary>
    private sealed class Moved(ContentPart was, ContentPart now)
    {
        private readonly ContentPart _document = Root(was);

        /// <summary>The way down from the block to the part being found, innermost first.</summary>
        private readonly List<int> _way = [];

        /// <summary>A language's reading, set further along, for each one met — and whatever else had to be made again.</summary>
        private Dictionary<object, ISourcePart>? _made;

        /// <summary>How far the block has moved.</summary>
        public int By { get; } = now.Start - was.Start;

        public bool Try(ISourcePart? part, out ISourcePart? to)
        {
            to = part switch
            {
                null => null,
                ContentPart written => Found(written),
                TexSourcePart named => Found(named),
                PartRun run => Found(run),
                PartSlice slice => Found(slice),
                SourceSpan span => span with { Start = span.Start + By },

                // Something this cannot follow stays put, which is right only where nothing moved.
                _ => By == 0 ? part : null,
            };

            return part is null || to is not null;
        }

        private ContentPart? Found(ContentPart part)
        {
            _way.Clear();

            var at = part;
            while (!ReferenceEquals(at, was))
            {
                if (at.Parent is not { } up) return Rooted(at, part);

                _way.Add(at.Order);
                at = up;
            }

            return Down(now);
        }

        /// <summary>
        /// A part of a reading other than the document's — a language's own, which that language read when the block was laid.
        /// Where nothing moved it is where it was; otherwise that reading is made again, set further along, and the same way
        /// is walked down it. The document's own reading is only ever reached through the block: a part outside the block is
        /// nothing this layout drew.
        /// </summary>
        private ContentPart? Rooted(ContentPart root, ContentPart part)
        {
            if (ReferenceEquals(root, _document)) return null;
            if (By == 0) return part;

            _made ??= [];

            if (!_made.TryGetValue(root, out var moved)) _made[root] = moved = ContentPart.Of(root.Node, root.Start + By);
            return Down((ContentPart)moved);
        }

        /// <summary>The way walked down from <paramref name="from"/>, or null where it no longer goes.</summary>
        private ContentPart? Down(ContentPart from)
        {
            for (var step = _way.Count - 1; step >= 0; step--)
            {
                var order = _way[step];
                if (order >= from.Children.Count) return null;

                from = from.Children[order];
            }

            return from;
        }

        private TexSourcePart? Found(TexSourcePart named)
        {
            _made ??= [];
            if (_made.TryGetValue(named, out var found)) return (TexSourcePart)found;
            if (Found(named.Of) is not { } of) return null;

            var moved = ReferenceEquals(of, named.Of) ? named : new TexSourcePart(of);
            _made[named] = moved;
            return moved;
        }



        /// <summary>A run of parts, each followed to where it now is — or null where any of them is gone.</summary>
        private PartRun? Found(PartRun run)
        {
            var moved = new List<ISourcePart>(run.Parts.Count);

            foreach (var part in run.Parts)
            {
                if (!Try(part, out var to) || to is null) return null;
                moved.Add(to);
            }

            return new PartRun(moved);
        }

        /// <summary>A stretch inside a part, followed with the part — or null where the part is gone.</summary>
        private PartSlice? Found(PartSlice slice) => Try(slice.Of, out var of) && of is not null ? slice with { Of = of } : null;

        private static ContentPart Root(ContentPart part)
        {
            while (part.Parent is { } up) part = up;
            return part;
        }
    }
}
