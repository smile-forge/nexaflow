using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Barcode;
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
    private readonly Dictionary<ContentNode, LaidBlock> _blocks = [];

    /// <summary>What was laid for <paramref name="block"/>, the node as this reading has it — null for one never read.</summary>
    public LaidBlock? For(ContentNode block) => _blocks.GetValueOrDefault(block);

    /// <summary>Says <paramref name="block"/> carries <paramref name="laid"/>.</summary>
    public void Add(ContentNode block, LaidBlock laid) => _blocks[block] = laid;

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

    /// <summary>Where each part of the old reading stands in the new one — found as asked for, since most of a block is never asked about.</summary>
    private sealed class Moved(ContentPart was, ContentPart now)
    {
        private readonly Dictionary<object, ISourcePart> _to = new() { [was] = now };

        private readonly ContentPart _document = Root(was);

        /// <summary>How far the block has moved.</summary>
        public int By { get; } = now.Start - was.Start;

        public bool Try(ISourcePart? part, out ISourcePart? to)
        {
            to = part switch
            {
                null => null,
                ContentPart written => Found(written),
                TexSourcePart named => Found(named),
                BarcodePart printed => Found(printed),
                SourceSpan span => span with { Start = span.Start + By },

                // Something this cannot follow stays put, which is right only where nothing moved.
                _ => By == 0 ? part : null,
            };

            return part is null || to is not null;
        }

        private ContentPart? Found(ContentPart part)
        {
            if (_to.TryGetValue(part, out var found)) return (ContentPart)found;

            if (part.Parent is not { } up)
            {
                // The document's own reading is found through the block, never from its top: a part outside the block
                // is nothing this layout drew.
                if (ReferenceEquals(part, _document)) return null;

                var moved = By == 0 ? part : ContentPart.Of(part.Node, part.Start + By);
                _to[part] = moved;
                return moved;
            }

            if (Found(up) is not { } there || there.Children.Count != up.Children.Count) return null;

            for (var at = 0; at < up.Children.Count; at++) _to[up.Children[at]] = there.Children[at];
            return (ContentPart)_to[part];
        }

        private TexSourcePart? Found(TexSourcePart named)
        {
            if (_to.TryGetValue(named, out var found)) return (TexSourcePart)found;
            if (Found(named.Of) is not { } of) return null;

            var moved = ReferenceEquals(of, named.Of) ? named : new TexSourcePart(of);
            _to[named] = moved;
            return moved;
        }

        private BarcodePart? Found(BarcodePart part)
        {
            if (By == 0) return part;
            if (_to.TryGetValue(part, out var found)) return (BarcodePart)found;

            if (part.Parent is not { } up)
            {
                var moved = part.At(By);
                _to[part] = moved;
                return moved;
            }

            if (Found(up) is not { } there || there.Children.Count != up.Children.Count) return null;

            for (var at = 0; at < up.Children.Count; at++) _to[up.Children[at]] = there.Children[at];
            return (BarcodePart)_to[part];
        }

        private static ContentPart Root(ContentPart part)
        {
            while (part.Parent is { } up) part = up;
            return part;
        }
    }
}
