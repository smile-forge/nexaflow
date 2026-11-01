using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Maths;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// Where each part a layout was drawn from stands once what it was drawn from is read somewhere else: the same content, read the same,
/// only standing further along — a block kept through an edit before it, a unit laid on its own and put down where it now starts.
///
/// <para>
/// The content reads exactly as it did, so it is the same parts; only where they stand has moved, and by the one amount. A part of the
/// reading the layout was drawn from is found again by the way down to it — which of its parent's parts it was, all the way from what
/// was laid (<see cref="ContentPart.Order"/>) — rather than by looking it up; a part of a language's own reading, read when the layout
/// was drawn, is that reading made again, set that much further along, and then only once.
/// </para>
/// </summary>
internal sealed class MovedParts
{
    private readonly ContentPart _was;
    private readonly Func<ContentPart> _reading;
    private readonly ContentPart _document;

    /// <summary>The way down from what was laid to the part being found, innermost first.</summary>
    private readonly List<int> _way = [];

    /// <summary>A language's reading, set further along, for each one met — and whatever else had to be made again.</summary>
    private Dictionary<object, ISourcePart>? _made;

    private ContentPart? _now;

    private MovedParts(ContentPart was, Func<ContentPart> now, int by)
    {
        _was = was;
        _reading = now;
        _document = Root(was);
        this.By = by;
    }

    /// <param name="was">What the layout was drawn from, as it read then.</param>
    /// <param name="now">The same, as it reads now.</param>
    public MovedParts(ContentPart was, ContentPart now) : this(was, () => now, now.Start - was.Start)
    {
    }

    /// <param name="was">What the layout was drawn from, as it read then: a reading's top.</param>
    /// <param name="by">How far it has moved: it reads now as it did, that much further along — read so only when a part is asked after.</param>
    public MovedParts(ContentPart was, int by) : this(was, () => ContentPart.Of(was.Node, was.Start + by), by)
    {
    }

    /// <summary>How far what the layout was drawn from has moved.</summary>
    public int By { get; }

    /// <summary>What the layout was drawn from as it reads now, standing where it now stands.</summary>
    private ContentPart Now => _now ??= _reading();

    /// <summary>Where <paramref name="part"/> stands now, in <paramref name="to"/> — false where it cannot be found again.</summary>
    public bool Try(ISourcePart? part, out ISourcePart? to)
    {
        to = part switch
        {
            null => null,
            ContentPart written => Found(written),
            TexPart named => Found(named),
            PartRun run => Found(run),
            PartSlice slice => Found(slice),
            SourceSpan span => span with { Start = span.Start + this.By },

            // Something this cannot follow stays put, which is right only where nothing moved.
            _ => this.By == 0 ? part : null,
        };

        return part is null || to is not null;
    }

    /// <summary>Where <paramref name="part"/> stands now, or null where it cannot be found again.</summary>
    public ISourcePart? Moved(ISourcePart? part) => this.Try(part, out var to) ? to : null;

    private ContentPart? Found(ContentPart part)
    {
        _way.Clear();

        var at = part;
        while (!ReferenceEquals(at, _was))
        {
            if (at.Parent is not { } up) return this.Rooted(at, part);

            _way.Add(at.Order);
            at = up;
        }

        return this.Down(this.Now);
    }

    /// <summary>
    /// A part of a reading other than the one the layout was drawn from — a language's own, which that language read when the layout
    /// was drawn. Where nothing moved it is where it was; otherwise that reading is made again, set further along, and the same way is
    /// walked down it. The reading the layout was drawn from is only ever reached through what was laid: a part of it outside is
    /// nothing this layout drew.
    /// </summary>
    private ContentPart? Rooted(ContentPart root, ContentPart part)
    {
        if (ReferenceEquals(root, _document)) return null;
        if (this.By == 0) return part;

        _made ??= [];

        if (!_made.TryGetValue(root, out var moved)) _made[root] = moved = ContentPart.Of(root.Node, root.Start + this.By);
        return this.Down((ContentPart)moved);
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

    private TexPart? Found(TexPart named)
    {
        _made ??= [];
        if (_made.TryGetValue(named, out var found)) return (TexPart)found;
        if (this.Found(named.Of) is not { } of) return null;

        var moved = ReferenceEquals(of, named.Of) ? named : new TexPart(of);
        _made[named] = moved;
        return moved;
    }

    /// <summary>A run of parts, each followed to where it now is — or null where any of them is gone.</summary>
    private PartRun? Found(PartRun run)
    {
        var moved = new List<ISourcePart>(run.Parts.Count);

        foreach (var part in run.Parts)
        {
            if (!this.Try(part, out var to) || to is null) return null;
            moved.Add(to);
        }

        return new PartRun(moved);
    }

    /// <summary>A stretch inside a part, followed with the part — or null where the part is gone.</summary>
    private PartSlice? Found(PartSlice slice) => this.Try(slice.Of, out var of) && of is not null ? slice with { Of = of } : null;

    private static ContentPart Root(ContentPart part)
    {
        while (part.Parent is { } up) part = up;
        return part;
    }
}
