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
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A large document shown a stretch at a time: read by its language's windowed parse (<see cref="ContentLanguage.Windowed"/>),
/// and laid out only as far as the reader has looked — the screen, a screen past it, and wherever the caret has gone.
///
/// <para>
/// <strong>The window is the engine's.</strong> The element says what is on screen (<see cref="OnScreen"/>) and nothing else;
/// how much is read, how much is laid and when more is, are decided here.
/// </para>
/// <para>
/// <strong>What is laid is one reading.</strong> The stretches taken into the layout are stitched into one tree, what is not
/// taken yet standing after them as one leaf of trivia, and that tree goes through the pipeline as any content does — its
/// stages, then its builder. Laying further takes another stretch into the tree and lays it again, and every block that reads
/// as it did is set down as it was laid (<see cref="Stages.WithUnchanged"/>), so only the new blocks are laid — and the block
/// the new ones follow is in the same reading, so what stands between them is the builder's to say, as it always is.
/// </para>
/// <para>
/// <strong>What is not taken yet is read in the background</strong> — its tree only — so the stretches the reader scrolls to
/// are already read. What lands is taken in the next time the content is laid, and whatever shows the content is told
/// (<see cref="Grew"/>) so that is soon.
/// </para>
/// <para>
/// <strong>And worked over there, where its stages say what its links can name</strong> (<see cref="LinkTargets"/>): what a
/// definition at the bottom says, or where a heading three pages down is, is known before anything there is laid. All of what
/// is read is worked over by the content's stages, with nothing a host said — so nothing of the host's is asked off its thread
/// — and what they found stands for everything past what is laid (<see cref="LinkTargets.Further"/>). Where that changes what a
/// name means, the content is laid again, and every block reading as it did is set down as it was.
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
    /// How far a long document has to be laid for what the reader moved to to be on the page: set where the caret, or what is
    /// picked out, went past what was laid, and let go once that is laid. Where a document merely starts with its caret at
    /// the end, nothing is laid to it.
    /// </summary>
    private int? _reveal;

    /// <summary>Where, once laid, what the reader moved to stands — brought onto the page as soon as it is shown (<see cref="Relay"/>).</summary>
    private int? _revealed;

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
    /// <paramref name="state"/>'s source laid out as far as the reader has looked — and as far as the caret has been moved —
    /// taking a stretch at a time into the reading that is laid, reading it first where it has not been read, and starting the
    /// rest reading in the background.
    /// </summary>
    private Laid Windowed(ContentLanguage language, EditState state, StyleFormat style, double room, bool readOnly)
    {
        Interlocked.Exchange(ref _growing, 0);

        if (_window is not { } window || !ReferenceEquals(window.Language, language)) window = Opened(language, state.Source);
        else if (!string.Equals(window.Source, state.Source, StringComparison.Ordinal)) window = Rewritten(window, state.Source);

        Received(window);

        var spent = Stopwatch.StartNew();
        var reach = Reach();
        var needed = _reveal ?? -1;
        Laid laid;

        // Short of where it has to reach when last laid: more is taken before anything is laid, so every laying out goes further,
        // however long laying what is taken already takes.
        if (window.Bottom > 0 && Short(window, reach, needed)) Took(window, reach, needed);

        while (true)
        {
            Turn();
            var taken = new Parsed(language, _named ?? string.Empty, state.Source, window.Chunks.Tree(window.Taken));

            laid = LaidFrom(language, _named, taken, state, style, room, readOnly);
            window.Bottom = laid.Size.Height;

            if (!Short(window, reach, needed) || spent.Elapsed > Spent || !Took(window, reach, needed)) break;
        }

        if (_reveal is { } target && window.LaidTo > target) (_revealed, _reveal) = (target, null);
        if (Short(window, reach, needed)) Grow();
        if (window.Reading is null && (!window.Chunks.Done || (window.Unstaged && _targets.Reported))) Background(window, style);

        // The page is as long as the whole document, going by what a character of what is laid came to.
        var (to, bottom) = (window.LaidTo, window.Bottom);
        if (to <= 0 || to >= window.Source.Length) return laid;

        return laid with { Size = new Size(laid.Size.Width, bottom + ((window.Source.Length - to) * (bottom / to))) };
    }

    /// <summary>
    /// Takes more stretches into the reading that is laid — as many as the page has room for going by what a character came to
    /// so far, and enough to reach <paramref name="needed"/>, a few at most — reading what is not read yet; and says whether there
    /// was more.
    /// </summary>
    private bool Took(ContentWindow window, double reach, int needed)
    {
        var chunks = window.Chunks;
        var (to, bottom) = (window.LaidTo, window.Bottom);
        var wanted = bottom > 0 && to > 0 ? to + (int)Math.Min((reach - bottom) * (to / bottom), int.MaxValue / 2) : to + 1;
        var took = false;

        // A few stretches at most each time, so a jump far down is laid a pass at a time and the page is shown between them.
        var most = to + (4 * StretchLength);

        while (window.LaidTo < Math.Min(Math.Max(wanted, needed + 1), most) || !took)
        {
            if (window.Taken >= chunks.Chunks.Count)
            {
                if (chunks.Next(StretchLength) is not { } next) break;

                chunks.Add(ContentChunk.Read(window.Parse, chunks.Source, next.From, next.SoftMaxLength, next.Resume));
                window.Altered();
            }

            window.Taken++;
            took = true;
        }

        return took;
    }

    /// <summary>Whether what is laid falls short of where it has to reach, with more of the document still to lay.</summary>
    private static bool Short(ContentWindow window, double reach, int needed) =>
        window.LaidTo < window.Source.Length && (window.Bottom < reach || window.LaidTo <= needed);

    /// <summary>The furthest the caret or anything picked out reaches, which has to be laid for either to be shown.</summary>
    private static int Needed(EditState state) =>
        state.Selection.Count == 0 ? state.Caret : Math.Max(state.Caret, state.Selection.Max(range => range.End));

    /// <summary>
    /// Whether <paramref name="state"/>'s caret, or what it picks out, reaches past what the window has laid — remembering how
    /// far, which the next laying out lays to.
    /// </summary>
    private bool Reaches(EditState state)
    {
        if (_window is not { Taken: > 0 } window || !Short(window, double.NegativeInfinity, Needed(state))) return false;

        _reveal = Needed(state);

        return true;
    }

    /// <summary>Lays a long document as far as <paramref name="offset"/>, and brings what stands there onto the page once it is.</summary>
    private void Uncovering(int offset)
    {
        _reveal = offset;
        Relay();
    }

    /// <summary>
    /// How far down the content has to be laid: a screen past the bottom of what is on screen, or two widths of room where
    /// nothing has said what is on screen yet.
    /// </summary>
    private double Reach() => _showing is { } showing ? showing.Bottom + Math.Max(showing.Height, 1) : Room * 2;

    /// <summary>Starts showing <paramref name="source"/> a stretch at a time, stopping whatever was reading the content before.</summary>
    private ContentWindow Opened(ContentLanguage language, string source)
    {
        _window?.Reading?.Cancel();
        _targets.Forget();

        var parse = language.Windowed!();
        var first = ContentChunk.Read(parse, source, 0, StretchLength, null);
        var named = (first.Tree as BlockNode)?.Language ?? _named ?? string.Empty;

        var window = new ContentWindow(language, source, new ContentChunks(source, named), parse) { Taken = 1, Length = StretchLength };
        window.Chunks.Add(first);

        return _window = window;
    }

    /// <summary>
    /// The window over what the document says now it has been written in: read again from where the change is
    /// (<see cref="ContentChunks.Edited"/>), and as much of it taken into the reading that is laid as was before — so the
    /// blocks laid before the change, and every one after it that reads as it did, are set down as they were laid.
    ///
    /// <para>
    /// What was found past what is laid is moved along with it. Where the change read again past what is laid — a fence left
    /// open — what is there may define otherwise than it did, and all of it is worked over again.
    /// </para>
    /// </summary>
    private ContentWindow Rewritten(ContentWindow window, string source)
    {
        window.Reading?.Cancel();

        // What the background had read of the document as it was is the document still, after the change.
        Received(window);

        var by = source.Length - window.Source.Length;
        var laidTo = window.LaidTo + by;

        // Read again as far as what is laid, and a stretch past it, at most: the rest is the background's to read.
        var (chunks, _) = window.Chunks.Edited(source, window.Parse, StretchLength, laidTo + StretchLength);

        var taken = 1;
        while (taken < chunks.Chunks.Count && chunks.Chunks[taken].From < laidTo) taken++;

        _targets.Moved(window.LaidTo, by);

        var was = new HashSet<ContentNode>(window.Chunks.Chunks.Select(chunk => chunk.Tree), ReferenceEqualityComparer.Instance);
        var unstaged = window.Unstaged || chunks.Chunks.Skip(taken).Any(chunk => !was.Contains(chunk.Tree));

        return _window = new ContentWindow(window.Language, source, chunks, window.Parse) { Taken = taken, Length = StretchLength, Unstaged = unstaged };
    }

    /// <summary>Stops showing content a stretch at a time, where it was.</summary>
    private void Closed()
    {
        if (_window is not { } window) return;

        window.Reading?.Cancel();
        _targets.Forget();
        _window = null;
        _reveal = null;
    }

    /// <summary>
    /// Takes in what the background reading of <paramref name="window"/> has read and found since it was last asked; and says
    /// whether what was found changes what any name past what is laid means.
    /// </summary>
    private bool Received(ContentWindow window)
    {
        var chunks = window.Chunks;

        // Read in the background from where the reading stood when it started; what was read here since is in already.
        while (window.Arrived.TryDequeue(out var chunk))
        {
            if (chunk.From != chunks.Reached || chunks.Done) continue;

            chunks.Add(chunk);
            window.Altered();
        }

        var further = false;

        while (window.Staged.TryDequeue(out var staged))
        {
            further |= _targets.Further(staged.Reach, staged.Targets);
            if (staged.Whole) window.Unstaged = false;
        }

        return further;
    }

    /// <summary>Says there is more to lay, once until it is laid.</summary>
    private void Grow()
    {
        if (Interlocked.Exchange(ref _growing, 1) == 0) Grew?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says there is more to lay where what is on screen has come within a screen of what is not laid yet.</summary>
    private void Approached()
    {
        if (_window is { Taken: > 0 } window && _showing is { } showing
            && Short(window, showing.Bottom + Math.Max(showing.Height, 1), -1))
            Grow();
    }

    /// <summary>
    /// What the content reads as where that is what it draws (<see cref="ReadsAsDrawn"/>), read from what it read to — for a long
    /// document shown a stretch at a time, all of it, read to its end first, so a search or a copy reaches every word rather than
    /// what the reader has scrolled past.
    /// </summary>
    internal DrawnText DrawnWords
    {
        get
        {
            if (_window is { } window) return window.Words ??= DrawnText.Of(Everything(window));

            if (!ReferenceEquals(_wordsOf, Reading)) (_words, _wordsOf) = (DrawnText.Of((Reading?.Root ?? NothingRead).Node), Reading);

            return _words!;
        }
    }

    /// <summary>What <see cref="DrawnWords"/> came to, and the reading it was read from.</summary>
    private DrawnText? _words;
    private ContentReading? _wordsOf;

    /// <summary>All of a windowed document, read to its end — what the background reading has read taken in, and the rest read here.</summary>
    private ContentNode Everything(ContentWindow window)
    {
        if (!window.Chunks.Done)
        {
            window.Read?.Wait();

            // What working all of it over found may make a name laid already mean something: it is laid again.
            if (Received(window)) Grow();

            while (window.Chunks.Next(window.Length) is { } next)
                window.Chunks.Add(ContentChunk.Read(window.Parse, window.Source, next.From, next.SoftMaxLength, next.Resume));

            window.Altered();
        }

        return window.Chunks.Tree();
    }

    /// <summary>
    /// Where on the page what stands at <paramref name="offset"/> would come, going by what is laid — for what a long document
    /// has not laid yet; null where it is laid, or the content is laid whole.
    /// </summary>
    internal double? UnlaidTop(int offset) =>
        _window is { LaidTo: > 0 } window && offset >= window.LaidTo
            ? window.Bottom + ((offset - window.LaidTo) * (window.Bottom / window.LaidTo))
            : null;

    /// <summary>
    /// Reads the rest of <paramref name="window"/> away from the thread that lays content out — its tree only, a stretch at a
    /// time, each handed back as it is read — and, where the content's stages say what its links can name, works all of what is
    /// read over with them: each time as much again has been read, so what the first stretches define is known early and all of
    /// it costs no more than working the whole over twice, and once it is all read. Says so when it is done.
    /// </summary>
    private void Background(ContentWindow window, StyleFormat style)
    {
        var cancel = new CancellationTokenSource();
        window.Reading = cancel;

        // Read on from where reading here stopped, into chunks of its own.
        var read = window.Chunks.Copy();
        var (language, named, length) = (window.Language, _named ?? string.Empty, StretchLength);
        var staging = window.Unstaged && _targets.Reported;
        var parse = language.Windowed!();

        window.Read = Task.Run(() =>
        {
            var staged = 0;

            while (!cancel.IsCancellationRequested && read.Next(length) is { } next)
            {
                var chunk = ContentChunk.Read(parse, read.Source, next.From, next.SoftMaxLength, next.Resume);
                read.Add(chunk);
                window.Arrived.Enqueue(chunk);

                if (staging && !read.Done && read.Reached >= 2 * staged) staged = Staged();
            }

            if (staging && !cancel.IsCancellationRequested) Staged();
            if (!cancel.IsCancellationRequested) Grew?.Invoke(this, EventArgs.Empty);

            // Everything read so far worked over from its top, as the reading that is laid is, with nothing the host said.
            int Staged()
            {
                var targets = new LinkTargets();
                var showing = new ContentShowing(named, style, false, null, 0, ContentInputs.None) { Reads = ContentLanguages.Reads, Targets = targets };
                var tree = read.Tree();
                var stages = language.Stages(tree, showing).OfType<IAstStage>().Where(stage => stage is not ISlowStage).ToList();

                if (stages.Count > 0) new AstPipeline(stages).Run(tree);

                window.Staged.Enqueue(new ContentStaging(targets.Reach, targets.Targets, read.Done));
                Grow();

                return read.Reached;
            }
        }, cancel.Token);
    }
}

/// <summary>
/// A large document being shown a stretch at a time: what has been read of it, how much of that is taken into the reading
/// that is laid, and what the background reading has handed back that is not taken in yet. The engine's, and only the engine's.
/// </summary>
internal sealed class ContentWindow(ContentLanguage language, string source, ContentChunks chunks, Func<ContentStretch, ContentParse> parse)
{
    public ContentLanguage Language { get; } = language;

    public string Source { get; } = source;

    /// <summary>What has been read, a stretch at a time.</summary>
    public ContentChunks Chunks { get; } = chunks;

    /// <summary>The parse the stretches read here are read by — another reads them in the background.</summary>
    public Func<ContentStretch, ContentParse> Parse { get; } = parse;

    /// <summary>How far a stretch is read before it looks for somewhere to stop.</summary>
    public required int Length { get; init; }

    /// <summary>How many of the chunks, from the first, are taken into the reading that is laid.</summary>
    public int Taken { get; set; }

    /// <summary>How far down the page what is laid reaches, as it was last laid.</summary>
    public double Bottom { get; set; }

    /// <summary>What the background reading has read, waiting to be taken in.</summary>
    public ConcurrentQueue<ContentChunk> Arrived { get; } = new();

    /// <summary>What working over all that the background reading had read found, waiting to be taken in.</summary>
    public ConcurrentQueue<ContentStaging> Staged { get; } = new();

    /// <summary>
    /// Whether what the whole document defines is still to be found by working all of it over — until the background has, and
    /// again once a change has read again past what is laid.
    /// </summary>
    public bool Unstaged { get; set; } = true;

    /// <summary>What stops the background reading, once one has started.</summary>
    public CancellationTokenSource? Reading { get; set; }

    /// <summary>The background reading, once one has started.</summary>
    public Task? Read { get; set; }

    /// <summary>What all of it reads as, made when something asks for it and let go when another chunk is read.</summary>
    public DrawnText? Words { get; set; }

    /// <summary>Where what is laid ends in the source: the end of the last chunk taken into it.</summary>
    public int LaidTo => Taken == 0 ? 0 : Chunks.Chunks[Taken - 1].End(Source.Length);

    /// <summary>Lets go of everything made of what was read, because more has been read, or read again.</summary>
    public void Altered() => Words = null;
}

/// <summary>What working over a windowed document as far as <paramref name="Reach"/> found its links can name — all of it, where <paramref name="Whole"/>.</summary>
internal sealed record ContentStaging(int Reach, IReadOnlyList<LinkTarget> Targets, bool Whole);
