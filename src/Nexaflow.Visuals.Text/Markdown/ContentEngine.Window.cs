using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;

using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;


namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A large document shown a stretch at a time: read by its language's windowed parse (<see cref="ContentLanguage.Windowed"/>), and
/// laid out only where the reader is — the screen and a screen either side of it, the caret, and wherever the reader went.
///
/// <para>
/// <strong>The window is the engine's.</strong> The element says what is on screen (<see cref="OnScreen"/>) and nothing else; how much
/// is read, which units are laid and when more are, are decided here.
/// </para>
/// <para>
/// <strong>Every unit read stands on the page, laid or not.</strong> A unit laid stands at the height it was laid at; one not laid at
/// what its characters come to, going by what is laid, and what is not read yet the same. Each laying out lays the units the screen,
/// the caret and what the reader went to reach where the page now puts them, and goes on while laying them moved what the screen
/// reaches. So the page is as long as the whole document from the start, and a jump to its end lays its end and nothing before it.
/// </para>
/// <para>
/// <strong>What the reader is looking at stays still.</strong> The first unit laid on screen holds the page: where laying changes the
/// height of anything above it — an estimate becoming a height — the engine says how far it moved (<see cref="Shifted"/>) and whatever
/// shows the content scrolls by as much, so the scrollbar settles and the page does not.
/// </para>
/// <para>
/// <strong>What is not read yet is read in the background</strong>, and every unit read is worked over there on its own by the
/// content's stages, with nothing a host said — so nothing of the host's is asked off its thread — for what it says to the rest of the
/// document (<see cref="UnitFacts"/>): its headings, for the outline, and what it defines, for its links. So the outline, the names
/// the headings answer to and what a link names reach the whole document, not what happens to be laid. What lands is taken in the next
/// time the content is laid, and whatever shows the content is told (<see cref="Grew"/>) so that is soon.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>
    /// How long, in characters, content has to be before it is read and laid a stretch at a time — as long as one stretch, so no unit
    /// worked over and laid on a keystroke is ever much longer than <see cref="StretchLength"/>, however long the content. Shorter content
    /// is read and laid whole.
    /// </summary>
    internal int WindowedFrom { get; set; } = 32 * 1024;

    /// <summary>How far a stretch is read before it looks for somewhere to stop (<see cref="ContentStretch.SoftMaxLength"/>).</summary>
    internal int StretchLength { get; set; } = 32 * 1024;

    /// <summary>How long one laying out spends on what the reader has moved towards before showing what it has and going on.</summary>
    private static readonly TimeSpan Spent = TimeSpan.FromMilliseconds(40);

    /// <summary>How many units the background works over between saying there is more to lay, so what it finds is taken in a batch at a time.</summary>
    private const int WorkedBetween = 16;

    /// <summary>The window over the content being shown, where it is shown a stretch at a time.</summary>
    private ContentWindow? _window;

    /// <summary>Whether the content is being laid where the reader is — so a body read a stretch at a time lays only what is near the screen too — rather than whole.</summary>
    private bool _windowing;

    /// <summary>
    /// Raised, on any thread, when there is more of a windowed document to lay — the reader has moved towards what is not laid
    /// yet, or the background reading has landed. Whatever shows the content lays it again on its own thread (<see cref="Relay"/>),
    /// which keeps everything already laid.
    /// </summary>
    internal event EventHandler? Grew;

    /// <summary>
    /// Raised as the content is laid when what the reader was looking at has moved down the page by as much as it says — up, where it
    /// is less than nothing — because what stands above it came to another height than it was taken to. Whatever shows the content
    /// scrolls by as much, so what the reader is looking at stays where it was on the screen. In the content's own units.
    /// </summary>
    internal event EventHandler<double>? Shifted;

    /// <summary>Whether a raised <see cref="Grew"/> is still to be answered, so the reader moving again does not raise it twice.</summary>
    private int _growing;

    /// <summary>
    /// Where in a long document the reader moved to: set where the caret, a place found or a link went somewhere not laid, and let go
    /// once it is. Where a document merely starts with its caret at the end, nothing is laid there.
    /// </summary>
    private int? _reveal;

    /// <summary>Where, once laid, what the reader moved to stands — brought onto the page as soon as it is shown (<see cref="Relay"/>).</summary>
    private int? _revealed;

    /// <summary>
    /// Whether whatever shows the content is moving the screen — a place brought onto the page, or the page put somewhere — and has not said
    /// where it came to: the page is not held still by what was on screen, which the reader is leaving. Set where the engine brings a place
    /// onto the page, and by whatever moves the screen itself; let go when the screen is next said (<see cref="OnScreen"/>), and by whatever
    /// shows the content once the move is done.
    /// </summary>
    internal bool ScreenMoving { get; set; }

    /// <summary>
    /// Whether content shown a stretch at a time has more to lay than it has laid — a unit not laid, or not read — false once it is all
    /// laid, and for content laid whole.
    /// </summary>
    internal bool Unlaid =>
        _window is { } window
        && (!window.Units.Done || window.Standing.Length != window.Listed.Count
            || Enumerable.Range(0, window.Listed.Count).Any(index => !window.Standing[index] && this.Shows(window, index)));

    /// <summary>
    /// The reading of a windowed document away from the thread that lays it, done once all of it is read and worked over — done
    /// already where nothing is reading.
    /// </summary>
    internal Task ReadingRest => _window?.Read ?? Task.CompletedTask;

    /// <summary>Whether content in <paramref name="language"/> as long as <paramref name="document"/> is shown a stretch at a time.</summary>
    private bool Windows(ContentLanguage language, ContentDocument document) => language.Windowed is not null && document.Length >= WindowedFrom;

    /// <summary>
    /// <paramref name="state"/>'s source laid out where the reader is: reading on where the page reaches past what is read, laying the
    /// units the screen, the caret and what the reader went to reach, and again while that moved what the screen reaches — keeping what
    /// the reader is looking at still (<see cref="Shifted"/>) — and starting what is left to read and work over in the background.
    /// </summary>
    private Laid Windowed(ContentLanguage language, EditState state, LayoutStyle style, double room, bool readOnly)
    {
        Interlocked.Exchange(ref _growing, 0);

        if (_window is not { } window || !ReferenceEquals(window.Language, language)) window = Opened(language, state.Document);
        else if (!ReferenceEquals(window.Document, state.Document)) window = Rewritten(window, state.Document);

        Received(window);
        _shown = window;

        var spent = Stopwatch.StartNew();
        var shifted = 0.0;
        var bodiesShort = false;
        Laid laid;

        _windowing = true;

        try
        {
            while (true)
            {
                this.ReadFurther(window);

                var anchor = ScreenMoving ? null : this.StillPoint(window);
                laid = LaidFrom(language, _named, window, state, style, room, readOnly, new KeptInContent(window, state.Document), tops => this.Wanted(window, state, tops));

                // Where each body laid a stretch at a time stands on the page now, so what of it the screen reaches is asked of that.
                this.FoundOnPage(laid.Tree);

                // The screen moves with what the reader is looking at, so what it reaches is asked of the page as it now stands.
                if (this.Moved(window, anchor) is var by && by != 0 && _showing is { } screen)
                {
                    _showing = new LayoutRect(screen.X, screen.Y + by, screen.Width, screen.Height);
                    shifted += by;
                }

                bodiesShort = this.BodiesShort();
                if ((!this.Short(window, state) && !bodiesShort) || spent.Elapsed > Spent) break;
            }
        }
        finally
        {
            _windowing = false;
        }

        if (_reveal is { } target && this.LaidAt(window, target)) (_revealed, _reveal) = (target, null);
        if (this.Short(window, state) || bodiesShort) Grow();

        // A background reading done since this began has handed back what it did: taken in before asking what is left to do.
        if (window.Read is { IsCompleted: true } && Received(window)) Grow();
        if (window.Read is not { IsCompleted: false } && (!window.Units.Done || this.Unworked(window))) Background(window, style);
        if (shifted != 0) Shifted?.Invoke(this, shifted);

        return laid;
    }

    /// <summary>
    /// The units to lay, at the places on the page <paramref name="tops"/> gives them: those the screen and a screen either side of it
    /// reach, the caret's and the one either side of it — so it steps into the next as it steps through its own — each end of what is
    /// picked out, and what the reader went to.
    /// </summary>
    private SortedSet<int> Wanted(ContentWindow window, EditState state, double[] tops)
    {
        var wanted = new SortedSet<int>();
        var (top, bottom) = this.Reach();

        for (var index = Reaching(tops, top); index < window.Listed.Count && tops[index] < bottom; index++) wanted.Add(index);

        if (Holding(window,state.Caret) is >= 0 and var caret)
            for (var at = Math.Max(caret - 1, 0); at <= Math.Min(caret + 1, window.Listed.Count - 1); at++) wanted.Add(at);

        foreach (var range in state.Selection)
            foreach (var end in new[] { range.Start, range.End })
                if (Holding(window,end) is >= 0 and var at) wanted.Add(at);

        if (_reveal is { } target && Holding(window,target) is >= 0 and var revealed) wanted.Add(revealed);

        return wanted;
    }

    /// <summary>Which of the units placed at <paramref name="tops"/> is the first reaching below <paramref name="y"/> — their count, where none does.</summary>
    private static int Reaching(double[] tops, double y)
    {
        var (low, high) = (0, tops.Length - 1);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (tops[middle + 1] > y) high = middle;
            else low = middle + 1;
        }

        return low;
    }

    /// <summary>
    /// Whether what is laid falls short of what it has to reach: a unit to lay that is not laid, the page reaching past what is read,
    /// or what the reader went to not read yet.
    /// </summary>
    private bool Short(ContentWindow window, EditState state)
    {
        if (window.Standing.Length != window.Listed.Count) return true;

        foreach (var index in this.Wanted(window, state, window.Tops))
            if (!window.Standing[index] && this.Shows(window, index)) return true;

        return !window.Units.Done && (this.Reach().Bottom > window.Tops[^1] || _reveal >= window.Units.Reached);
    }

    /// <summary>
    /// Reads on — a few stretches at most, so a jump far down is laid a pass at a time and the page is shown between them — where the
    /// page reaches past what is read, going by what a character comes to, or what the reader went to is not read yet.
    /// </summary>
    private void ReadFurther(ContentWindow window)
    {
        var (bottom, most) = (window.Tops[^1], window.Units.Reached + (4 * StretchLength));
        var perCharacter = window.PerCharacter > 0 ? window.PerCharacter : Unsaid(_style, Room);
        var reach = this.Reach().Bottom;

        while (!window.Units.Done && window.Units.Reached < most && (bottom < reach || _reveal >= window.Units.Reached))
        {
            var reached = window.Units.Reached;
            window.Units = window.Units.ReadOn(window.Parse, window.Document, StretchLength);
            window.Altered();

            bottom += (window.Units.Reached - reached) * perCharacter;
        }
    }

    /// <summary>
    /// What the page holds still as it is laid: where the first unit laid on screen started, and where on the page it stood — the first
    /// on screen, where none of it is laid — or null where nothing is on screen. Where the screen is over a body read a stretch at a time,
    /// the first unit of the innermost such body laid on screen holds it: what stands above that inside the body moves it too.
    /// </summary>
    private (int Start, double Top)? StillPoint(ContentWindow window)
    {
        var (units, tops, standing) = (window.Listed, window.Tops, window.Standing);
        if (_showing is not { } screen || standing.Length != units.Count || units.Count == 0) return null;

        for (var inner = _onPage.Count - 1; inner >= 0; inner--)
        {
            var body = _onPage[inner];
            if (body.PageTop is not { } at || screen.Y < at || screen.Y >= at + body.Tops[^1] || body.Standing.Length != body.Listed.Count) continue;

            for (var index = Reaching(body.Tops, screen.Y - at); index < body.Listed.Count && at + body.Tops[index] < screen.Bottom; index++)
                if (body.Standing[index]) return (Where(body, body.Listed[index].Start), at + body.Tops[index]);
        }

        int? first = null;

        for (var index = Reaching(tops, screen.Y); index < units.Count && tops[index] < screen.Bottom; index++)
        {
            first ??= index;
            if (standing[index]) return (units[index].Start, tops[index]);
        }

        return first is { } unit ? (units[unit].Start, tops[unit]) : null;
    }

    /// <summary>
    /// How far down the page the unit <paramref name="anchor"/> names has moved since it was taken — a unit of a body on the page, or of
    /// <paramref name="window"/> — nothing, where no unit starts there now.
    /// </summary>
    private double Moved(ContentWindow window, (int Start, double Top)? anchor)
    {
        if (anchor is not var (start, top)) return 0;

        foreach (var body in _onPage)
            if (body.PageTop is { } at && OwnIn(body, start) is { } own && Holding(body, own) is >= 0 and var index && Where(body, body.Listed[index].Start) == start)
                return at + body.Tops[index] - top;

        if (Holding(window, start) is not (>= 0 and var unit) || window.Listed[unit].Start != start) return 0;

        return window.Tops[unit] - top;
    }

    /// <summary>Lays a long document where <paramref name="offset"/> is, and brings what stands there onto the page once it is.</summary>
    private void Uncovering(int offset)
    {
        _reveal = offset;
        Relay();
    }

    /// <summary>
    /// Lays the unit of a long document holding <paramref name="offset"/>, where it is not laid — and nothing before it — and brings what
    /// stands there onto the page once it is: for a place found, or gone to, that the reader has not scrolled anywhere near. False where
    /// there is nothing to lay: it is laid already, or the content is not shown a stretch at a time.
    /// </summary>
    internal bool Uncover(int offset)
    {
        if (_window is not { } window || offset < 0 || offset > window.Document.Length) return false;
        if (this.LaidAt(window, offset)) return false;

        Uncovering(offset);

        return true;
    }

    /// <summary>
    /// Whether <paramref name="state"/>'s caret is somewhere a long document has not laid — remembering it, so the next laying out lays
    /// it and brings it onto the page.
    /// </summary>
    private bool Reaches(EditState state)
    {
        if (_window is not { } window) return false;
        if (this.LaidAt(window, state.Caret)) return false;

        _reveal = state.Caret;

        return true;
    }

    /// <summary>
    /// Whether what stands at <paramref name="offset"/> is laid: the unit of <paramref name="window"/> holding it, and the part of that unit
    /// holding it where only part was laid — and, inside a body on the page read a stretch at a time, the unit of the body holding it, at
    /// every depth.
    /// </summary>
    private bool LaidAt(ContentWindow window, int offset)
    {
        if (Holding(window, offset) is not (>= 0 and var at) || at >= window.Standing.Length || !window.Standing[at] || !LaidHolds(window, at, offset))
            return false;

        foreach (var body in _onPage)
            if (OwnIn(body, offset) is { } own
                && (Holding(body, own) is not (>= 0 and var held) || held >= body.Standing.Length || !body.Standing[held] || !LaidHolds(body, held, offset)))
                return false;

        return true;
    }

    /// <summary>
    /// What the content has to be laid across, down the page: a screen either side of what is on screen, or two widths of room from the
    /// top where nothing has said what is on screen yet.
    /// </summary>
    private (double Top, double Bottom) Reach() =>
        _showing is { } showing ? (showing.Y - Math.Max(showing.Height, 1), showing.Bottom + Math.Max(showing.Height, 1)) : (0, Room * 2);

    /// <summary>Starts showing <paramref name="document"/> a stretch at a time, stopping whatever was reading the content before.</summary>
    private ContentWindow Opened(ContentLanguage language, ContentDocument document)
    {
        _window?.Reading?.Cancel();
        _targets.Forget();
        _whole = null;

        var parse = language.Windowed!();
        var first = ContentUnit.Read(parse, document, 0, StretchLength, null);
        var named = (first.Tree as BlockNode)?.Language ?? _named ?? string.Empty;

        return _window = new ContentWindow(language, document, new ContentUnits(named).WithSyntax(language.Syntax?.Invoke(document)).Added(first), parse)
                {
                    Length = StretchLength,
                    Targets = _targets,
                };
    }

    /// <summary>
    /// The window over what the document says now it has been written in: read again from where each change since is
    /// (<see cref="ContentUnits.Edited"/>) — a stretch past the change at most, the rest left to read again as the page reaches it or
    /// in the background — so every unit the change did not reach is the one it was, laid as it was laid.
    ///
    /// <para>
    /// <strong>Read again from the changes, not from the two documents.</strong> Every version the engine moves the document to
    /// while it is shown here is told to the window with the change that made it (<see cref="ContentWindow.Wrote"/>), so what to
    /// read again is where each change wrote. Only a version made some other way — loaded, or handed in whole — is compared with
    /// the one the window holds, once, for the one stretch where they differ.
    /// </para>
    /// </summary>
    private ContentWindow Rewritten(ContentWindow window, ContentDocument document)
    {
        window.Reading?.Cancel();

        // What the background had read and worked over of the document as it was is the document still, after the change.
        Received(window);

        var changes = window.Following(document) ?? [(document.ChangesFrom(window.Document).Forward, document)];
        var units = window.Units;

        foreach (var (change, after) in changes)
        {
            var (from, to, by) = Spanned(change);
            (units, _, _) = units.Edited(from, to, by, after, window.Parse, StretchLength, to + by + StretchLength, window.Language.Bounds);
        }

        window.Became(document, units);
        return window;
    }

    /// <summary>Where <paramref name="change"/> wrote, in the document it was made to — from its first write to the end of its last — and how far it moved what is after.</summary>
    private static (int From, int To, int By) Spanned(ContentChange change) =>
        (change.Writes.Min(write => write.Start), change.Writes.Max(write => write.End), change.Writes.Sum(write => write.Text.Length - write.Length));

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
    /// Takes in what the background reading of <paramref name="window"/> has read and worked over since it was last asked — what a unit
    /// laid here already said is kept — and says whether anything came.
    /// </summary>
    private bool Received(ContentWindow window)
    {
        var came = false;

        // Read in the background from where the reading stood when it started; what was read here since is in already.
        while (window.Arrived.TryDequeue(out var arrived))
        {
            if (arrived.Start != window.Units.Reached || window.Units.Done) continue;

            window.Units = window.Units.Added(arrived.Unit);
            window.Altered();
            came = true;
        }

        while (window.Worked.TryDequeue(out var worked))
        {
            if (_facts.TryGetValue(worked.Tree, out _)) continue;

            _facts.Add(worked.Tree, worked.Facts);
            came = true;
        }

        return came;
    }

    /// <summary>Whether a unit read has not been worked over yet, here or in the background, for what it says to the rest of the document.</summary>
    private bool Unworked(ContentWindow window)
    {
        foreach (var (unit, _) in window.Listed)
            if (!_facts.TryGetValue(unit.Tree, out _)) return true;

        return false;
    }

    /// <summary>Says there is more to lay, once until it is laid.</summary>
    private void Grow()
    {
        if (Interlocked.Exchange(ref _growing, 1) == 0) Grew?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says there is more to lay where what is on screen has come to a unit not laid, or past what is read.</summary>
    private void Approached()
    {
        if (_window is not { } window || _showing is null) return;

        // A body the screen has come into the part of that is not laid has its holder laid again, so it lays that part.
        var bodiesShort = this.BodiesShort();
        if (bodiesShort || this.Short(window, _state)) Grow();
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

            // Content read whole is one unit, as it was worked over.
            var tree = _shown is { Listed.Count: > 0 } shown && shown.Staged.TryGetValue(shown.Listed[0].Unit.Tree, out var staged) ? staged.Tree : NothingRead.Node;
            if (!ReferenceEquals(_wordsOf, tree)) (_words, _wordsOf) = (DrawnText.Of(tree), tree);

            return _words!;
        }
    }

    /// <summary>What <see cref="DrawnWords"/> came to, and the reading it was read from.</summary>
    private DrawnText? _words;
    private ContentNode? _wordsOf;

    /// <summary>All of a windowed document, read to its end — what the background reading has read taken in, and the rest read here.</summary>
    private ContentNode Everything(ContentWindow window)
    {
        if (!window.Units.Done)
        {
            window.Read?.Wait();

            // What the background found may say something new of the document: it is laid again.
            if (Received(window)) Grow();

            while (!window.Units.Done) window.Units = window.Units.ReadOn(window.Parse, window.Document, window.Length);

            window.Altered();
        }

        return window.Units.Tree(window.Document);
    }

    /// <summary>
    /// Where on the page what stands at <paramref name="offset"/> would come, going by where the unit holding it stands — for what a
    /// long document has not laid, a unit or the part of one; null where it is laid, or the content is laid whole.
    /// </summary>
    internal double? UnlaidTop(int offset)
    {
        if (_window is not { } window || window.Standing.Length != window.Listed.Count) return null;

        var tops = window.Tops;

        if (Holding(window, offset) is not (>= 0 and var at))
            return tops[^1] + ((offset - window.Units.Reached) * (window.PerCharacter > 0 ? window.PerCharacter : Unsaid(_style, Room)));

        if (window.Standing[at] && LaidHolds(window, at, offset)) return null;

        var (unit, start) = window.Listed[at];

        return tops[at] + ((tops[at + 1] - tops[at]) * (offset - start) / Math.Max(unit.Width, 1));
    }

    /// <summary>
    /// Reads the rest of <paramref name="window"/> away from the thread that lays content out — its tree only, a stretch at a time,
    /// each handed back as it is read — and works every unit read that nothing has worked over yet over on its own, in order, for what
    /// it says to the rest of the document, handing that back too. Says so when it is done.
    /// </summary>
    private void Background(ContentWindow window, LayoutStyle style)
    {
        var cancel = new CancellationTokenSource();
        window.Reading = cancel;

        // Read on from where reading here stopped, into units of its own: the units and the document are immutable, so what is
        // read there never disturbs what is laid here.
        var (read, document) = (window.Units, window.Document);
        var (language, named, length) = (window.Language, _named ?? string.Empty, StretchLength);
        var (facts, parse) = (_facts, language.Windowed!());

        // Handed back into the queues of the version it reads: the window's own are another's once the document is written in.
        var (arriving, working) = (window.Arrived, window.Worked);

        window.Read = Task.Run(() =>
        {
            var (looked, worked) = (0, 0);

            while (!cancel.IsCancellationRequested)
            {
                foreach (var (unit, start) in read.From(looked))
                {
                    if (cancel.IsCancellationRequested) return;

                    looked++;
                    if (facts.TryGetValue(unit.Tree, out _)) continue;

                    working.Enqueue((unit.Tree, Worked(language, named, style, unit, start)));
                    if (++worked % WorkedBetween == 0) Grow();
                }

                if (read.Done) break;

                var reached = read.Reached;
                read = read.ReadOn(parse, document, length);
                arriving.Enqueue((reached, read[read.Count - 1].Unit));
            }

            if (!cancel.IsCancellationRequested) Grew?.Invoke(this, EventArgs.Empty);
        }, cancel.Token);
    }

    /// <summary>
    /// What <paramref name="unit"/>, starting at <paramref name="start"/>, says to the rest of its document: its tree worked over on its
    /// own by <paramref name="language"/>'s stages that are not slow, with nothing the host said and nothing any other unit defines. A
    /// unit its stages cannot work over says nothing, as it shows nothing but itself written.
    /// </summary>
    private static UnitFacts Worked(ContentLanguage language, string named, LayoutStyle style, ContentUnit unit, int start)
    {
        try
        {
            var targets = new LinkTargets(new LinkTargets());
            var showing = new ContentShowing(named, style, false, null, start, ContentInputs.None) { Reads = ContentLanguages.Reads, Targets = targets };
            var stages = language.Stages(unit.Tree, showing).OfType<IAstStage>().Where(stage => stage is not ISlowStage).ToList();

            var worked = stages.Count > 0 ? new AstPipeline(stages).Run(unit.Tree) : unit.Tree;

            return new UnitFacts(OutlineUnit.Of(worked.Children, unit.Width), targets.Reported, targets.Defined);
        }
        catch (Exception)
        {
            return new UnitFacts(OutlineUnit.Of([], unit.Width), false, []);
        }
    }

    /// <summary>
    /// Content being shown, and what has been read of it: a large document a stretch at a time — with what the background reading has
    /// read and worked over, waiting to be taken in — or content read whole, as one unit. Either way read again from each change the engine
    /// makes to it (<see cref="Wrote"/>), with the readings of what is written inside it attached to their holders
    /// (<see cref="ContentUnit.Bodies"/>). The engine's, and only the engine's — and the same window from one version of the content to
    /// the next (<see cref="Became"/>), so what is laid of it is kept with it.
    /// </summary>
    private sealed class ContentWindow(ContentLanguage language, ContentDocument document, ContentUnits units, Func<ContentStretch, ContentParse> parse)
    {
        /// <summary>
        /// What the document has been written to since <see cref="Document"/>, each version with the change that made it, in order — or
        /// null once it was moved to a version not made from the last one by one change.
        /// </summary>
        private List<(ContentChange Change, ContentDocument After)>? _following = [];

        /// <summary>The last version the document was written to, which the next is to be made from.</summary>
        private ContentDocument _latest = document;

        public ContentLanguage Language { get; } = language;

        /// <summary>The version of the document what is read here was read from.</summary>
        public ContentDocument Document { get; private set; } = document;

        /// <summary>What has been read, a unit at a time.</summary>
        public ContentUnits Units { get; set; } = units;

        /// <summary>The parse the stretches read here are read by — another reads them in the background.</summary>
        public Func<ContentStretch, ContentParse> Parse { get; } = parse;

        /// <summary>How far a stretch is read before it looks for somewhere to stop.</summary>
        public required int Length { get; init; }

        /// <summary>What the background reading has read, waiting to be taken in.</summary>
        public ConcurrentQueue<(int Start, ContentUnit Unit)> Arrived { get; private set; } = new();

        /// <summary>What the background has worked the units read over into, by the tree each was read into, waiting to be taken in.</summary>
        public ConcurrentQueue<(ContentNode Tree, UnitFacts Facts)> Worked { get; private set; } = new();

        /// <summary>What stops the background reading, once one has started.</summary>
        public CancellationTokenSource? Reading { get; set; }

        /// <summary>The background reading, once one has started.</summary>
        public Task? Read { get; set; }

        /// <summary>What all of it reads as, made when something asks for it and let go when another chunk is read.</summary>
        public DrawnText? Words { get; set; }

        // ── What is laid of it ──────────────────────────────────────────────────

        /// <summary>What the link targets its units define are gathered into — the content's own, for the content.</summary>
        public required LinkTargets Targets { get; init; }

        /// <summary>Every unit it read, with where each starts, in order — as last laid out.</summary>
        public List<(ContentUnit Unit, int Start)> Listed { get; set; } = [];

        /// <summary>The version of its characters <see cref="Listed"/> is of.</summary>
        public ContentDocument ListedIn { get; set; } = document;

        /// <summary>What the reading keeps of all of it (<see cref="ContentUnits.Syntax"/>), as last laid out.</summary>
        public IReadingSyntax? LaidSyntax { get; set; }

        /// <summary>Each unit worked over, by the tree it was read into — kept while that tree is read, laid or not, so its height outlives being off screen.</summary>
        public Dictionary<ContentNode, StagedUnit> Staged { get; set; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Where each unit stood as last laid out — and, one past the last, where what is read ends.</summary>
        public double[] Tops { get; set; } = [0];

        /// <summary>Which units stood laid as last laid out.</summary>
        public bool[] Standing { get; set; } = [];

        /// <summary>How tall a character of what was laid came to, as last laid out — zero before anything has been.</summary>
        public double PerCharacter { get; set; }

        /// <summary>The language and showing its units were last worked over in, which a unit worked over to be read rather than laid is worked over in too.</summary>
        public (ContentLanguage Language, ContentShowing Showing)? StagedBy { get; set; }

        /// <summary>How many times what its units define has come to mean something else — what a unit kept was last held to.</summary>
        public int Defined { get; set; }

        /// <summary>What each unit said of itself when what the units define was last gathered, in order — null for a unit nothing had worked over.</summary>
        public List<UnitFacts?> DefinedBy { get; set; } = [];

        // ── A body's ────────────────────────────────────────────────────────────

        /// <summary>
        /// For a body: how its own characters stand in the document, and the document — null for the content's own reading, whose characters
        /// are the document's.
        /// </summary>
        public (ContentSpace Space, ContentDocument Document)? Placing { get; set; }

        /// <summary>
        /// For a body: the reading and the unit it was last laid in, and where among that reading's own characters its holder starts — what is
        /// laid again when more of the body is to be laid.
        /// </summary>
        public (ContentWindow Window, ContentNode Unit, int At)? Holder { get; set; }

        /// <summary>For a body: where its top stood on the page as the content was last laid out — going by a unit of it laid — null before it was found there.</summary>
        public double? PageTop { get; set; }

        /// <summary>
        /// Says the document went from <paramref name="before"/> to <paramref name="after"/>: a change to read again from, where it was
        /// made by one change from the last version told, and otherwise the end of following it.
        /// </summary>
        public void Wrote(ContentDocument before, ContentDocument after)
        {
            if (_following is null) return;

            if (!ReferenceEquals(before, _latest) || !after.MadeFrom(before))
            {
                _following = null;
                return;
            }

            _following.Add((after.Change!, after));
            _latest = after;
        }

        /// <summary>
        /// Says what is read here now reads as <paramref name="units"/>, of <paramref name="document"/>. Whatever the background was reading of
        /// the version before — stopped already — hands what it still does to queues nothing takes from any more.
        /// </summary>
        public void Became(ContentDocument document, ContentUnits units)
        {
            (this.Document, this.Units, _latest, _following) = (document, units, document, []);
            (this.Arrived, this.Worked, this.Reading, this.Read) = (new(), new(), null, null);
            this.Altered();
        }

        /// <summary>The changes that made <paramref name="now"/> from <see cref="Document"/>, in order — or null where they were not all told.</summary>
        public IReadOnlyList<(ContentChange Change, ContentDocument After)>? Following(ContentDocument now) =>
            _following is not null && ReferenceEquals(_latest, now) ? _following : null;

        /// <summary>Lets go of everything made of what was read, because more has been read, or read again.</summary>
        public void Altered() => Words = null;
    }
}
