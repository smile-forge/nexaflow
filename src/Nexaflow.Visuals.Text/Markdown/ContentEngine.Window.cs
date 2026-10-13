using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A large document shown a stretch at a time: read by its language's windowed parse (<see cref="ContentLanguage.Windowed"/>),
/// and laid out only as far as the reader has looked — the screen, a screen past it, and wherever the caret has gone.
///
/// <para>
/// <strong>The window is the engine's.</strong> The element says what is on screen (<see cref="OnScreen"/>) and nothing else;
/// how much is read, how much is laid and when more is, are decided here. A stretch is laid exactly as nested content is: its
/// own reading, worked over by the language's stages and laid by the language's builder, which never learns it is laying part
/// of something.
/// </para>
/// <para>
/// <strong>Stretches are laid as the whole would be.</strong> What stands between two blocks is the builder's to say, so each
/// stretch is laid with the last block laid before it in front — the block it follows — and only what comes after that
/// block is kept, moved to stand where that block stands in what is laid. The pieces of every stretch are gathered into one
/// tree, so the element paints, hit-tests and picks out one layout as it always has.
/// </para>
/// <para>
/// <strong>What is not read yet is read in the background</strong> — its tree only, never laid — so what asks of the whole
/// document reaches all of it, and the stretches the reader scrolls to are already read. What lands is taken in the next time
/// the content is laid, and whatever shows the content is told (<see cref="Grew"/>) so that is soon.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>
    /// How long, in characters, content has to be before it is read and laid a stretch at a time. Shorter content is read and
    /// laid whole.
    /// </summary>
    internal int WindowedFrom { get; set; } = 128 * 1024;

    /// <summary>How far a stretch is read before it looks for somewhere to stop (<see cref="ContentStretch.SoftMaxLength"/>).</summary>
    internal int StretchLength { get; set; } = 32 * 1024;

    /// <summary>How long one laying out spends on what the reader has moved towards before showing what it has and going on.</summary>
    private static readonly TimeSpan Spent = TimeSpan.FromMilliseconds(40);

    /// <summary>The window over the content being shown, where it is shown a stretch at a time.</summary>
    private ContentWindow? _window;

    /// <summary>
    /// Raised, on any thread, when there is more of a windowed document to lay — the reader has moved towards what is not laid
    /// yet, or the background reading has landed. Whatever shows the content lays it again on its own thread (<see cref="Relay"/>),
    /// which keeps everything already laid.
    /// </summary>
    internal event EventHandler? Grew;

    /// <summary>Whether a raised <see cref="Grew"/> is still to be answered, so the reader moving again does not raise it twice.</summary>
    private int _growing;

    /// <summary>
    /// Whether content shown a stretch at a time has more to lay than it has laid — false once it is all laid, and for content
    /// laid whole.
    /// </summary>
    internal bool Unlaid => _window is { } window && window.LaidTo < window.Source.Length;

    /// <summary>
    /// The reading of a windowed document away from the thread that lays it, done once all of it is read and taken in at the
    /// next laying out — done already where nothing is reading.
    /// </summary>
    internal Task ReadingRest => _window?.Read ?? Task.CompletedTask;

    /// <summary>Whether content in <paramref name="language"/> as long as <paramref name="source"/> is shown a stretch at a time.</summary>
    private bool Windows(ContentLanguage language, string source) => language.Windowed is not null && source.Length >= WindowedFrom;

    /// <summary>
    /// <paramref name="state"/>'s source laid out as far as the reader has looked — and as far as the caret and what is picked
    /// out reach — reading what that needs where it is not read yet, and starting the rest reading in the background.
    /// </summary>
    private Laid Windowed(ContentLanguage language, EditState state, StyleFormat style, double room, bool readOnly)
    {
        Interlocked.Exchange(ref _growing, 0);

        if (_window is not { } window || !ReferenceEquals(window.Language, language)) window = Opened(language, state.Source);
        else if (!string.Equals(window.Source, state.Source, StringComparison.Ordinal)) window = Rewritten(window, state.Source);

        Received(window);

        // What the reader has opened in a diagram is handed out in the order the diagrams are written, so it is handed out
        // afresh down what is laid, and on down each stretch as it is laid. Content that is not a document is one view.
        _views.Clear();
        _opened.Rewind();
        _top = language == ContentLanguages.Markdown ? null : _opened.Next();
        if (_top is { } opened) style = style with { Expansion = opened };

        // Laid in another style, at another width, or read-only where it is now written in, nothing laid is what it would be now.
        var shown = (style, room, readOnly);
        if (!Equals(window.LaidAs, shown))
        {
            window.Laid.Clear();
            window.LaidAs = shown;
        }

        // A block shown as written, or no longer, is laid again — and everything after it, which stood where it did because of it.
        if (window.Raw != state.Raw)
        {
            var touched = window.Laid.FindIndex(stretch => Touches(stretch, window.Raw) || Touches(stretch, state.Raw));
            if (touched >= 0) window.Laid.RemoveRange(touched, window.Laid.Count - touched);

            window.Raw = state.Raw;
        }

        foreach (var stretch in window.Laid) Viewed(stretch.Chunk);

        var spent = Stopwatch.StartNew();
        var reach = Reach();
        var needed = _reveal ?? -1;

        while (window.Laid.Count == 0 || Short(window, reach, needed))
        {
            if (!Next(window, style, room, readOnly, state.Raw) || spent.Elapsed > Spent) break;
        }

        if (window.LaidTo > needed) _reveal = null;
        if (Short(window, reach, needed)) Grow();
        if (window.Reading is null && (!window.Chunks.Done || window.Chunks.Unsettled().Any())) Background(window);

        return Composed(window);
    }

    /// <summary>Whether <paramref name="zone"/> — a stretch shown as written — lies in what <paramref name="stretch"/> was laid from.</summary>
    private static bool Touches(LaidStretch stretch, RawZone? zone) =>
        zone is { } shown && shown.Start <= stretch.To && stretch.Reading.Root.Start <= shown.End;

    /// <summary>
    /// The window over what the document says now it has been written in: the stretches read and laid before the change kept
    /// as they are, the change read again, and every stretch after it that reads as it did kept, moved along — laid again only
    /// as the reader comes to it (<see cref="ContentChunks.Edited"/>).
    /// </summary>
    private ContentWindow Rewritten(ContentWindow window, string source)
    {
        window.Reading?.Cancel();

        // What the background had read of the document as it was is the document still, after the change.
        Received(window);

        var (chunks, kept) = window.Chunks.Edited(source, window.Parse, StretchLength);
        var rewritten = new ContentWindow(window.Language, source, chunks, window.Parse) { LaidAs = window.LaidAs, Raw = window.Raw };
        rewritten.Laid.AddRange(window.Laid.Take(Math.Min(kept, window.Laid.Count)));

        return _window = rewritten;
    }

    /// <summary>Whether what is laid falls short of where it has to reach, with more of the document still to lay.</summary>
    private static bool Short(ContentWindow window, double reach, int needed) =>
        window.LaidTo < window.Source.Length && (window.Bottom < reach || window.LaidTo <= needed);

    /// <summary>The furthest the caret or anything picked out reaches, which has to be laid for either to be shown.</summary>
    private static int Needed(EditState state) =>
        state.Selection.Count == 0 ? state.Caret : Math.Max(state.Caret, state.Selection.Max(range => range.End));

    /// <summary>
    /// How far a long document has to be laid for what the reader moved to to be on the page: set where the caret, or what is
    /// picked out, went past what was laid, and let go once that is laid. Where a document merely starts with its caret at
    /// the end, nothing is laid to it.
    /// </summary>
    private int? _reveal;

    /// <summary>
    /// Whether <paramref name="state"/>'s caret, or what it picks out, reaches past what the window has laid — remembering how
    /// far, which the next laying out lays to.
    /// </summary>
    private bool Reaches(EditState state)
    {
        if (_window is not { Laid.Count: > 0 } window || !Short(window, double.NegativeInfinity, Needed(state))) return false;

        _reveal = Needed(state);

        return true;
    }

    /// <summary>Starts showing <paramref name="source"/> a stretch at a time, stopping whatever was reading the content before.</summary>
    private ContentWindow Opened(ContentLanguage language, string source)
    {
        _window?.Reading?.Cancel();

        var parse = language.Windowed!();
        var first = ContentChunk.Read(parse, source, 0, StretchLength, null, null);
        var named = (first.Tree as BlockNode)?.Language ?? _named ?? string.Empty;

        var window = new ContentWindow(language, source, new ContentChunks(source, named), parse);
        window.Chunks.Add(first);

        return _window = window;
    }

    /// <summary>Stops showing content a stretch at a time, where it was.</summary>
    private void Closed()
    {
        _window?.Reading?.Cancel();
        _window = null;
        _reveal = null;
    }

    /// <summary>Takes in what the background reading of <paramref name="window"/> has read since it was last asked.</summary>
    private static void Received(ContentWindow window)
    {
        var chunks = window.Chunks;

        while (window.Arrived.TryDequeue(out var arrived))
        {
            var chunk = arrived.Chunk;

            if (!arrived.Settles)
            {
                // Read in the background from where the reading stood when it started; what was read here since is in already.
                if (chunk.From == chunks.Reached && !chunks.Done)
                {
                    chunks.Add(chunk);
                    window.Document = null;
                }

                continue;
            }

            // Read again knowing what the whole document says. Whatever was laid from what it read before is laid again, and so
            // is everything after it, which stood where it did because of it.
            var at = chunks.Chunks.Count - 1;
            while (at >= 0 && chunks.Chunks[at].From > chunk.From) at--;
            if (at < 0 || chunks.Chunks[at].From != chunk.From) continue;

            chunks.Settle(at, chunk);
            window.Document = null;

            if (at < window.Laid.Count) window.Laid.RemoveRange(at, window.Laid.Count - at);
        }
    }

    /// <summary>
    /// How far down the content has to be laid: a screen past the bottom of what is on screen, or two widths of room where
    /// nothing has said what is on screen yet.
    /// </summary>
    private double Reach() => _showing is { } showing ? showing.Bottom + Math.Max(showing.Height, 1) : Room * 2;

    /// <summary>Lays the stretch after what is laid — reading it first where it has not been read — and says whether there was one.</summary>
    private bool Next(ContentWindow window, StyleFormat style, double room, bool readOnly, RawZone? raw)
    {
        var chunks = window.Chunks;
        var index = window.Laid.Count;

        if (index >= chunks.Chunks.Count)
        {
            if (chunks.Next(StretchLength) is not { } next) return false;

            chunks.Add(ContentChunk.Read(window.Parse, chunks.Source, next.From, next.SoftMaxLength, next.Resume, null));
            window.Document = null;
        }

        Viewed(chunks.Chunks[index]);
        window.Laid.Add(Stretched(window, index, style, room, readOnly, raw));

        return true;
    }

    /// <summary>
    /// The chunk at <paramref name="index"/>, laid as the whole document would lay it: read with the block it follows in front of
    /// it, worked over by the language's stages, laid by the language's builder, and only its own pieces kept.
    /// </summary>
    private LaidStretch Stretched(ContentWindow window, int index, StyleFormat style, double room, bool readOnly, RawZone? raw)
    {
        var chunks = window.Chunks.Chunks;
        var chunk = chunks[index];
        var source = window.Source;
        var end = chunk.End(source.Length);
        var before = index > 0 ? window.Laid[index - 1] : null;
        var follows = before?.Follows;

        // The block it follows, and whatever stands between that block and it — from the chunk that block was read in on.
        var parts = new List<ContentNode>();

        if (follows is { } block)
        {
            var from = index - 1;
            while (from > 0 && chunks[from].From > block.Start) from--;

            for (var at = from; at < index; at++)
                foreach (var part in chunks[at].Placed.Children)
                    if (!part.IsDerived && part.Offset >= block.Start) parts.Add(part);
        }

        foreach (var part in chunk.Placed.Children) if (!part.IsDerived) parts.Add(part);
        foreach (var part in chunk.Placed.Children) if (part.IsDerived) parts.Add(part);

        var start = parts.Count > 0 && parts[0].Offset is { } first ? Math.Min(first, chunk.From) : chunk.From;
        var text = source[start..end];
        var named = window.Chunks.Language;
        var showing = Showing(_named ?? string.Empty, style, !readOnly, raw, start);

        Laid laid;
        ContentReading reading;

        try
        {
            reading = ContentReading.Of(Staged(new Parsed(window.Language, named, text, new BlockNode(named, parts, offset: start)), showing),
                                        start, text);
            laid = Builder(window.Language, reading, showing).Lay(room);
        }
        catch (Exception error)
        {
            reading = ContentReading.Of(ContentNode.Shown(text, offset: start), start, text);
            laid = Unreadable(text, start, $"This could not be read: {error.Message}", style, readOnly, room);
        }

        return LaidStretch.Of(chunk, end, reading, laid, follows, before?.Bottom ?? 0);
    }

    /// <summary>What the reader has opened in each diagram <paramref name="chunk"/>'s own blocks hold, handed out next.</summary>
    private void Viewed(ContentChunk chunk)
    {
        if (_top is not null) return;

        foreach (var part in chunk.Placed.Children)
            if (!part.IsDerived && part.Offset is { } at) Held(part, at);
    }

    /// <summary>
    /// The pieces of every laid stretch, gathered into one tree under the first stretch's root — with as much room below them
    /// as what is not laid yet will take, going by what has been: the page is as long as the document, wherever the reader is.
    /// </summary>
    private static Laid Composed(ContentWindow window)
    {
        if (window.Laid.Count == 0) return Laid.Nothing;

        var top = window.Laid[0].Root;
        var into = new LayoutBuilder(window.Laid.Sum(stretch => stretch.Count));

        // The root stands for the whole document, as the root of the whole document laid at once does — not for the first
        // stretch it was laid as the root of.
        into.Open(top.Kind, window.Whole, new Point(top.Offset.X, top.Offset.Y), top.Stops);

        foreach (var stretch in window.Laid)
            foreach (var piece in stretch.Pieces)
                into.Graft(piece, new Vector(0, stretch.Shift));

        into.Close();

        var (laid, height) = (window.LaidTo, window.Bottom);

        // What is not laid yet, at the height a character of what is laid came to.
        if (laid > 0 && laid < window.Source.Length) height += (window.Source.Length - laid) * (height / laid);

        return new Laid(into.Seal(), new Size(Math.Max(window.Laid.Max(stretch => stretch.Reach), 1), Math.Max(height, 1)),
                        [.. window.Laid.SelectMany(stretch => stretch.Trouble)]);
    }

    /// <summary>Says there is more to lay, once until it is laid.</summary>
    private void Grow()
    {
        if (Interlocked.Exchange(ref _growing, 1) == 0) Grew?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says there is more to lay where what is on screen has come within a screen of what is not laid yet.</summary>
    private void Approached()
    {
        if (_window is { Laid.Count: > 0 } window && _showing is { } showing
            && Short(window, showing.Bottom + Math.Max(showing.Height, 1), -1))
            Grow();
    }

    /// <summary>
    /// Reads the rest of <paramref name="window"/> away from the thread that lays content out — its tree only, a stretch at a
    /// time, each handed back as it is read — and then, where the language reads a stretch by what the whole document says,
    /// every stretch again knowing it. Says so when it is done.
    /// </summary>
    private void Background(ContentWindow window)
    {
        var cancel = new CancellationTokenSource();
        window.Reading = cancel;

        // Read on from where reading here stopped, and kept, of every chunk, what is needed to read it again knowing what the
        // whole document came to: where it starts and stops, what it started in, and what it was read knowing.
        var chunks = window.Chunks;
        var (source, length, done, whole) = (chunks.Source, StretchLength, chunks.Done, chunks.Whole);
        var from = chunks.Reached;
        var resume = chunks.Chunks[^1].Ended;
        var read = chunks.Chunks.Select(chunk => (chunk.From, chunk.Stopped, chunk.Begun, chunk.Whole)).ToList();
        var parse = window.Language.Windowed!();

        window.Read = Task.Run(() =>
        {
            while (!done && !cancel.IsCancellationRequested)
            {
                var chunk = ContentChunk.Read(parse, source, from, length, resume, null);
                window.Arrived.Enqueue((chunk, false));
                read.Add((chunk.From, chunk.Stopped, chunk.Begun, chunk.Whole));

                if (chunk.Stopped is { } stopped) (from, resume) = (stopped, chunk.Ended);
                else (done, whole) = (true, chunk.Ended);
            }

            foreach (var (at, stopped, begun, knew) in read)
            {
                if (cancel.IsCancellationRequested) return;
                if (Equals(knew, whole)) continue;

                var extent = stopped is { } end ? end - at : source.Length - at + 1;
                window.Arrived.Enqueue((ContentChunk.Read(parse, source, at, extent, begun, whole), true));
            }

            if (!cancel.IsCancellationRequested) Grew?.Invoke(this, EventArgs.Empty);
        }, cancel.Token);
    }
}

/// <summary>
/// A large document being shown a stretch at a time: what has been read of it, what has been laid, and what the background
/// reading has handed back that is not taken in yet. The engine's, and only the engine's.
/// </summary>
internal sealed class ContentWindow(ContentLanguage language, string source, ContentChunks chunks, Func<ContentStretch, ContentParse> parse)
{
    public ContentLanguage Language { get; } = language;

    public string Source { get; } = source;

    /// <summary>What has been read, a stretch at a time.</summary>
    public ContentChunks Chunks { get; } = chunks;

    /// <summary>The parse the stretches read here are read by — another reads them in the background.</summary>
    public Func<ContentStretch, ContentParse> Parse { get; } = parse;

    /// <summary>The stretches laid, one for each chunk from the first, in order.</summary>
    public List<LaidStretch> Laid { get; } = [];

    /// <summary>What they were laid as: the style, the room, and whether only read.</summary>
    public object? LaidAs { get; set; }

    /// <summary>What was shown as written when they were laid.</summary>
    public RawZone? Raw { get; set; }

    /// <summary>What the background reading has read, waiting to be taken in.</summary>
    public ConcurrentQueue<(ContentChunk Chunk, bool Settles)> Arrived { get; } = new();

    /// <summary>What stops the background reading, once one has started.</summary>
    public CancellationTokenSource? Reading { get; set; }

    /// <summary>The background reading, once one has started.</summary>
    public Task? Read { get; set; }

    /// <summary>
    /// What the root of the whole layout stands for: the whole document as written, in its language, with nothing read under
    /// it — what a press on nothing but the page, or a walk up from a piece past every block, comes to.
    /// </summary>
    public ContentPart Whole => field ??= ContentPart.Of(
        new BlockNode(Chunks.Language, [ContentNode.Leaf(Kinds.Verbatim, Source, Roles.Body, offset: 0)], offset: 0));

    /// <summary>Every chunk read so far as one reading, made when something asks for it and let go when another is read.</summary>
    public ContentReading? Document { get; set; }

    /// <summary>Where what is laid ends in the source.</summary>
    public int LaidTo => Laid.Count == 0 ? 0 : Laid[^1].To;

    /// <summary>How far down the page what is laid reaches.</summary>
    public double Bottom => Laid.Count == 0 ? 0 : Laid[^1].Bottom;

    /// <summary>Every block laid, in order, each from the reading it was laid from — a stretch's own, not the one it follows.</summary>
    public IEnumerable<ContentPart> Blocks()
    {
        foreach (var stretch in Laid)
            foreach (var block in stretch.Reading.Root.Children)
                if (!block.Derived && block.Role != Roles.Trivia && block.Start >= stretch.Chunk.From) yield return block;
    }

    /// <summary>The laid block <paramref name="offset"/> is in, or null where it is in none — or in none laid yet.</summary>
    public ContentPart? Blocked(int offset)
    {
        foreach (var stretch in Laid)
        {
            if (offset < stretch.Chunk.From || offset >= stretch.To) continue;

            foreach (var block in stretch.Reading.Root.Children)
                if (!block.Derived && block.Role != Roles.Trivia && block.Start >= stretch.Chunk.From && offset >= block.Start && offset < block.End)
                    return block;
        }

        return null;
    }
}

/// <summary>
/// One stretch of a windowed document, laid: the reading it was laid from, the pieces of it that are its own, and how far
/// down they are moved to stand below what was laid before it.
/// </summary>
/// <param name="Chunk">The stretch.</param>
/// <param name="To">Where it ends in the source.</param>
/// <param name="Reading">What it was laid from: its own blocks, and the one it follows.</param>
/// <param name="Root">The piece its builder laid everything under.</param>
/// <param name="Pieces">The pieces under that root that are its own, in order.</param>
/// <param name="Shift">How far down they are moved.</param>
/// <param name="Bottom">How far down the page it reaches, moved.</param>
/// <param name="Reach">How far across.</param>
/// <param name="Follows">The last block laid by the end of it — what the stretch after it follows.</param>
/// <param name="Trouble">What was wrong in its own blocks.</param>
/// <param name="Count">How many pieces its own come to.</param>
internal sealed record LaidStretch(ContentChunk Chunk, int To, ContentReading Reading, Piece Root, IReadOnlyList<Piece> Pieces, double Shift,
                                   double Bottom, double Reach, Followed? Follows, IReadOnlyList<Diagnostic> Trouble, int Count)
{
    /// <summary>How far apart two places can be and still be the one place, once a stretch has been moved to stand where it does.</summary>
    private const double Hair = 1e-6;

    /// <summary>
    /// What a builder laid for <paramref name="chunk"/> — with the block <paramref name="follows"/> in front of it, and what
    /// stood after that block, where there is one — as a stretch: the pieces laid for those let go, and the rest moved so that
    /// block stands where it already stands, or so they start at <paramref name="below"/> where nothing went before.
    ///
    /// <para>
    /// What went before is told apart from what is this stretch's own by how many pieces it came to last time, not by what
    /// each piece stands for: an empty paragraph stands for nothing written and draws nothing, and two of them, one either side
    /// of the line between the stretches, look the same. The builder lays the same blocks into the same pieces wherever they
    /// stand, so the first that many pieces are what went before — checked against the block it follows, which does stand for
    /// something.
    /// </para>
    /// </summary>
    public static LaidStretch Of(ContentChunk chunk, int to, ContentReading reading, Laid laid, Followed? follows, double below)
    {
        var pieces = laid.Root.Children.ToList();
        var skip = 0;
        double? stood = null;

        if (follows is { } block && block.Trailing <= pieces.Count && Begun(reading, pieces, block.Start) is { } first)
        {
            skip = block.Trailing;
            stood = first.Bounds.Top;
        }
        else if (follows is not null)
        {
            // Laid into other pieces than last time — which a builder does not do — so what went before is what stands for no
            // block of this stretch's own.
            while (skip < pieces.Count && !(Block(reading, pieces[skip]) is { } at && at >= chunk.From)) skip++;
        }

        // Moved so the block it follows stands where it already does — or, following nothing, where its builder put it, below
        // whatever there is.
        var shift = follows is { } prior && stood is { } top ? prior.Top - top : below;
        var own = pieces.Skip(skip).ToList();

        // How far down it reaches is how far its builder went, moved — which is what the whole document's builder would have
        // gone by the end of these blocks.
        var bottom = own.Count == 0 ? below : laid.Size.Height + shift;

        return new LaidStretch(chunk, to, reading, laid.Root, own, shift, bottom, laid.Size.Width, Followed.After(reading, own, shift, follows),
                               [.. laid.Trouble.Where(said => said.Start >= chunk.From)],
                               own.Sum(piece => piece.SelfAndDescendants().Count()) + 1);
    }

    /// <summary>The first piece of <paramref name="pieces"/>, where it was laid for the block at <paramref name="start"/> and draws something.</summary>
    private static Piece? Begun(ContentReading reading, List<Piece> pieces, int start) =>
        pieces.Count > 0 && Block(reading, pieces[0]) == start && !pieces[0].Bounds.IsEmpty ? pieces[0] : null;

    /// <summary>
    /// Where the block a piece at the top of a layout was drawn for begins in <paramref name="reading"/> — the block holding the
    /// first thing in the piece drawn from anything, which may stand well into it: a heading's words come after its hashes, and
    /// an empty paragraph's are no characters long but still stand somewhere — or null for a piece drawn from nothing.
    /// </summary>
    internal static int? Block(ContentReading reading, Piece piece)
    {
        if (piece.SelfAndDescendants().Select(inside => inside.Part).FirstOrDefault(part => part is not null) is not { } written)
            return null;

        foreach (var block in reading.Root.Children)
            if (!block.Derived && written.Start >= block.Start && written.Start < block.End) return block.Start;

        return written.Start;
    }
}

/// <summary>
/// The last block laid by the end of a stretch, which the next stretch is laid behind: where it begins, where its first piece
/// stands on the page, and how many of the pieces at the top of the layout it and whatever after it came to.
/// </summary>
internal readonly record struct Followed(int Start, double Top, int Trailing)
{
    /// <summary>
    /// What follows <paramref name="own"/> — a stretch's own pieces, moved by <paramref name="shift"/>: its last block that was
    /// drawn from something, or, where none of them was, what the stretch followed, with these pieces after it too.
    /// </summary>
    public static Followed? After(ContentReading reading, List<Piece> own, double shift, Followed? before)
    {
        for (var last = own.Count - 1; last >= 0; last--)
        {
            if (LaidStretch.Block(reading, own[last]) is not { } start) continue;

            var first = own.FindIndex(piece => LaidStretch.Block(reading, piece) == start && !piece.Bounds.IsEmpty);
            if (first < 0) continue;

            return new Followed(start, own[first].Bounds.Top + shift, own.Count - first);
        }

        return before is { } prior ? prior with { Trailing = prior.Trailing + own.Count } : null;
    }
}
