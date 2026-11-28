using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What a search asks of each line: whether the line is one it finds — every term of a query written on it, say — and where on the line
/// what it looks for is written. Asked of one line at a time, without its line end, so no match runs across a line break.
/// </summary>
public sealed class LineQuery
{
    /// <summary>Whether a line is one it finds — or null, where a line is found wherever anything is written on it.</summary>
    private readonly Func<string, bool>? _matches;

    private readonly Func<string, IReadOnlyList<(int Index, int Length)>> _occurrences;

    /// <summary>A query finding the lines <paramref name="matches"/> says, where <paramref name="occurrences"/> says what it looks for is written.</summary>
    public LineQuery(Func<string, bool> matches, Func<string, IReadOnlyList<(int Index, int Length)>> occurrences)
    {
        _matches = matches;
        _occurrences = occurrences;
    }

    /// <summary>A query finding every line on which <paramref name="occurrences"/> finds anything written.</summary>
    public LineQuery(Func<string, IReadOnlyList<(int Index, int Length)>> occurrences) => _occurrences = occurrences;

    /// <summary>Whether <paramref name="line"/> is one it finds.</summary>
    public bool Matches(string line) => _matches?.Invoke(line) ?? _occurrences(line).Count > 0;

    /// <summary>Where on <paramref name="line"/> what it looks for is written, where the line is one it finds; nowhere, where it is not.</summary>
    public IReadOnlyList<(int Index, int Length)> On(string line) => _matches is null || _matches(line) ? _occurrences(line) : [];

    /// <summary>
    /// How many times what it looks for is written on <paramref name="line"/>: none where the line is not one it finds, and once where it is
    /// found with nothing on it to point to.
    /// </summary>
    public int TimesOn(string line) =>
        _matches is null ? _occurrences(line).Count
      : _matches(line) ? Math.Max(1, _occurrences(line).Count)
      : 0;
}

/// <summary>
/// A search over what a page shows, as the page drives it: what it found, which of it is being looked at, stepping through it either way
/// round and back to the start past the end, and letting it go — marked on the page by one <see cref="FoundMarks"/>. How it reads the page is
/// its kind's: the lines of a file a page shows (<see cref="FileFind"/>), or the places of a document held whole (<see cref="PlaceFind"/>).
/// </summary>
public abstract class ContentFind : IDisposable
{
    /// <summary>Raised when what it found, or which of it is looked at, has changed — for the page to say so.</summary>
    public event EventHandler? Changed;

    /// <summary>How many it found.</summary>
    public abstract int Count { get; }

    /// <summary>Which of them is being looked at, from nought — -1 for none.</summary>
    public int At { get; protected set; } = -1;

    /// <summary>Whether a search is shown — finding nothing is a result to show too.</summary>
    public bool IsShowing { get; protected set; }

    /// <summary>Goes to the next, coming back round to the first.</summary>
    public bool Next() => this.Step(+1);

    /// <summary>Goes to the one before, coming back round to the last.</summary>
    public bool Previous() => this.Step(-1);

    /// <summary>Goes to the next (<paramref name="delta"/> of one) or the one before, round past either end; false where nothing was found.</summary>
    public bool Step(int delta)
    {
        var count = this.Count;
        if (count == 0) return false;

        var at = this.At < 0 ? (delta >= 0 ? 0 : count - 1) : (((this.At + delta) % count) + count) % count;
        this.GoTo(at);

        return true;
    }

    /// <summary>Goes to the one found at <paramref name="at"/>: it looked at, and brought before the reader.</summary>
    public void GoTo(int at)
    {
        if (at < 0 || at >= this.Count) return;

        this.At = at;
        this.Gone(at);
        this.Said();
    }

    /// <summary>Lets the search go: nothing found, nothing marked.</summary>
    public abstract void Clear();

    public virtual void Dispose() => GC.SuppressFinalize(this);

    /// <summary>Brings the one found at <paramref name="at"/> before the reader, looked at.</summary>
    protected abstract void Gone(int at);

    /// <summary>Says what it found, or which is looked at, has changed.</summary>
    protected void Said() => this.Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// What a read of a whole file found: every line found, in order, from nought; how many matches stand up to and including each of them
/// (<see cref="Ends"/>, so every match is counted, and the line any one of them is on found by halving); and the first lines found, with what
/// is written on each.
/// </summary>
public sealed record FoundLines(long[] Lines, int[] Ends, IReadOnlyList<(long Line, string Text)> First)
{
    /// <summary>How many matches were found, over every line — a line holding two counts twice.</summary>
    public int Count => this.Ends.Length == 0 ? 0 : this.Ends[^1];
}

/// <summary>
/// A search over the lines of the file a page shows (<see cref="ShownFile"/>) — the whole file, whether the page's document is the file or a
/// window onto one too long for a document. It reads the file a line at a time off the page, as the file stood when the search began, and
/// finds the lines a query finds, in order; marks what the query finds on the lines drawn, so marking costs a screen however much it found;
/// steps through every match on them, on from the one looked at or from the caret; narrows to lines chosen; and, as the file is written in or grows, runs again once that
/// pauses, the reader kept on the match they were on.
/// </summary>
public sealed class FileFind : ContentFind
{
    /// <summary>How long writing pauses before the search shown is run again.</summary>
    private static readonly TimeSpan AgainAfter = TimeSpan.FromMilliseconds(300);

    private readonly ShownFile _file;
    private readonly FoundMarks _marks;
    private readonly ShownPlace _brought;

    /// <summary>The lines of the whole file found, from nought, in order.</summary>
    private long[] _lines = [];

    /// <summary>How many matches stand up to and including each of the lines found — what a match, counted from nought, is found on.</summary>
    private int[] _ends = [];

    /// <summary>The lines it was narrowed to, which it is not run again over — or null, every line the query finds.</summary>
    private HashSet<long>? _kept;

    /// <summary>The read under way, or waiting to run again, cancelled by the next.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>How many times the file has been written in or grown — not counting a window moving over it, which changes no line of it.</summary>
    private long _written;

    /// <summary>How many finds are reading the file, which run again themselves where it is written meanwhile.</summary>
    private int _finding;

    /// <param name="file">The file searched, and the view it is shown in.</param>
    /// <param name="marks">What marks what it finds on the view.</param>
    /// <param name="brought">Where on the page a match gone to is put.</param>
    public FileFind(ShownFile file, FoundMarks marks, ShownPlace brought = ShownPlace.Nearest)
    {
        (_file, _marks, _brought) = (file, marks, brought);
        file.Document.Changed += this.Written;
    }

    /// <summary>The query shown, or null.</summary>
    public LineQuery? Query { get; private set; }

    /// <summary>The lines of the whole file found, from nought, in order.</summary>
    public IReadOnlyList<long> Lines => _lines;

    /// <summary>How many matches it found — a line holding two counts twice.</summary>
    public override int Count => _ends.Length == 0 ? 0 : _ends[^1];

    /// <summary>The line of the whole file, from nought, of the match looked at — or -1, where none is.</summary>
    public long LookedAtLine => this.At >= 0 && this.At < this.Count ? _lines[this.LineOfMatch(this.At)] : -1;

    /// <summary>Whether it was narrowed to lines chosen rather than every line the query finds.</summary>
    public bool IsNarrowed => _kept is not null;

    /// <summary>
    /// Reads the whole file for <paramref name="query"/>, changing nothing shown: every line it finds and how many matches each holds, and the
    /// first <paramref name="first"/> of the lines with what is written on each. Read a line at a time off this thread, from the file as it stood
    /// when asked.
    /// </summary>
    public async Task<FoundLines> ReadAsync(LineQuery query, int first, CancellationToken cancel)
    {
        var lines = _file.Lines();
        var found = new List<long>();
        var ends = new List<int>();
        var firsts = new List<(long Line, string Text)>();
        var matches = 0;

        await Task.Run(() => lines((line, _, text) =>
        {
            var times = query.TimesOn(text);
            if (times == 0) return;

            found.Add(line);
            ends.Add(matches += times);
            if (firsts.Count < first) firsts.Add((line, text));
        }, cancel), cancel);

        return new FoundLines([.. found], [.. ends], firsts);
    }

    /// <summary>
    /// Shows what <paramref name="query"/> finds over the whole file — marked, counted — and goes to the first of it. Where the file was written
    /// in or grew while it was read, it is read again at once, so what is counted is the file as it is.
    /// </summary>
    public async Task<FoundLines> FindAsync(LineQuery query, int first, CancellationToken cancel)
    {
        var reading = this.Reading();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(reading.Token, cancel);

        var written = _written;
        FoundLines found;
        _finding++;

        try
        {
            found = await this.ReadAsync(query, first, linked.Token);
        }
        finally
        {
            _finding--;
        }

        if (reading.IsCancellationRequested) return found;

        this.Shown(query, found, at: 0, go: true);
        if (written != _written) await this.AgainAsync();

        return found;
    }

    /// <summary>
    /// Narrows what it found to <paramref name="lines"/> of the whole file, from nought — the count, the marks and the steps through it all
    /// follow them — and goes to the first. Narrowed, it is not run again as the file is written: its lines are carried through each change.
    /// False where none is given.
    /// </summary>
    public bool Narrow(IEnumerable<long> lines)
    {
        var kept = lines.Where(line => line >= 0).Distinct().Order().ToArray();
        if (kept.Length == 0) return false;

        _reading?.Cancel();
        this.Kept(kept);
        this.At = 0;
        this.IsShowing = true;
        this.Gone(0);
        this.Said();

        return true;
    }

    /// <summary>
    /// Goes to the match after the caret (<paramref name="forward"/>) or before it — round past either end — as a click and then Find Next does
    /// in every editor: the next on the caret's own line where one stands past it, and otherwise the first on the next line found.
    /// </summary>
    public bool StepFromCaret(bool forward)
    {
        var count = this.Count;
        if (count == 0) return false;

        var caret = _file.Editor.Caret;
        var line = _file.LineOf(caret);
        var index = LowerBound(_lines, line);

        // From the next line found on, or back from it; past what is on the caret's own line, where it stands on one found.
        var at = forward ? this.FirstMatchOn(index) : this.FirstMatchOn(index) - 1;

        if (index < _lines.Length && _lines[index] == line && this.Query is { } query
            && _file.OffsetOfLine(line) is { } start && _file.HeldOffset(start) is { } held)
        {
            var document = _file.Document.Current;
            var (from, end) = document.LineAt(held);
            var column = caret - from;
            var (upTo, before) = (0, 0);

            foreach (var (place, _) in query.On(document.Slice(from, end - from).TrimEnd('\r', '\n')))
            {
                if (place <= column) upTo++;
                if (place < column) before++;
            }

            var first = this.FirstMatchOn(index);
            at = forward ? first + upTo : first + before - 1;
        }

        this.GoTo(at >= count ? 0 : at < 0 ? count - 1 : at);
        return true;
    }

    /// <summary>
    /// Runs the search shown again over the whole file as it is now, at once — the reader kept on the match they were on, or the first after
    /// it, and nothing moved. Nothing, where it was narrowed by hand — or while a find is reading, which reads again itself where the file
    /// changed meanwhile.
    /// </summary>
    public async Task AgainAsync()
    {
        if (this.Query is not { } query || _kept is not null || _finding > 0) return;

        var reading = this.Reading();
        var parked = this.Parked();

        try
        {
            var found = await this.ReadAsync(query, 0, reading.Token);
            if (reading.IsCancellationRequested || !ReferenceEquals(query, this.Query)) return;

            var line = LowerBound(found.Lines, parked);
            var at = line < found.Lines.Length ? (line == 0 ? 0 : found.Ends[line - 1]) : found.Count - 1;
            this.Shown(query, found, at, go: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Moves what it found on by <paramref name="lines"/> — as many as were put before them, as a log's earlier history landing does.</summary>
    public void Shift(long lines)
    {
        if (lines == 0 || _lines.Length == 0) return;

        if (_kept is not null) this.Kept([.. _lines.Select(line => line + lines)]);
        else _lines = [.. _lines.Select(line => line + lines)];

        this.Said();
    }

    /// <summary>The lines found, up to <paramref name="first"/> of them, with what is written on each now.</summary>
    public IReadOnlyList<(long Line, string Text)> Firsts(int first) =>
        [.. _lines.Take(first).Select(line => (line, _file.LineText(line)))];

    public override void Clear()
    {
        _reading?.Cancel();
        (this.Query, _lines, _ends, _kept, this.At, this.IsShowing) = (null, [], [], null, -1, false);
        _marks.Show(query: null);
        this.Said();
    }

    public override void Dispose()
    {
        _reading?.Cancel();
        _file.Document.Changed -= this.Written;
        base.Dispose();
    }

    protected override void Gone(int at)
    {
        var index = this.LineOfMatch(at);
        if (_file.OffsetOfLine(_lines[index]) is not { } start) return;

        // The window over a file too long for one document brought there first.
        var offset = _file.DocumentOffset(start);
        var document = _file.Document.Current;
        var match = this.MatchOn(document, offset, at - this.FirstMatchOn(index));

        _file.Editor.MoveCaretTo(match?.Start ?? document.LineAt(offset).Start);
        _marks.LookAt(match);
        _file.Editor.ScrollToLine(document.LineOf(offset), _brought);
    }

    /// <summary>
    /// Shows <paramref name="found"/> as what <paramref name="query"/> found, match <paramref name="at"/> the one looked at — gone to where
    /// <paramref name="go"/> says, and otherwise only outlined, where the page holds it.
    /// </summary>
    private void Shown(LineQuery query, FoundLines found, int at, bool go)
    {
        (_lines, _ends, _kept) = (found.Lines, found.Ends, null);
        this.At = found.Count == 0 ? -1 : Math.Clamp(at, 0, found.Count - 1);
        this.IsShowing = true;

        this.Query = query;
        _marks.Show(query);

        if (this.At >= 0 && go) this.Gone(this.At);
        else _marks.LookAt(this.HeldMatch());

        this.Said();
    }

    /// <summary>The match looked at, where the document holds its line — a window onto a file is not moved to it.</summary>
    private (int Start, int Length)? HeldMatch()
    {
        if (this.At < 0) return null;

        var index = this.LineOfMatch(this.At);
        return _file.OffsetOfLine(_lines[index]) is { } start && _file.HeldOffset(start) is { } held
            ? this.MatchOn(_file.Document.Current, held, this.At - this.FirstMatchOn(index))
            : null;
    }

    /// <summary>Keeps <paramref name="lines"/>, in order, as all it found — the marks only on them, and each stepped to once.</summary>
    private void Kept(long[] lines)
    {
        var kept = new HashSet<long>(lines);
        (_lines, _ends, _kept) = (lines, [.. Enumerable.Range(1, lines.Length)], kept);
        this.At = lines.Length == 0 ? -1 : Math.Clamp(this.At, 0, lines.Length - 1);

        _marks.Show(this.Query, line => kept.Contains(_file.LinesBefore + line));
    }

    /// <summary>
    /// Match <paramref name="nth"/>, from nought, on the line of <paramref name="document"/> holding <paramref name="offset"/> — the first, where
    /// the line holds fewer now — where the query finds any.
    /// </summary>
    private (int Start, int Length)? MatchOn(ContentDocument document, int offset, int nth)
    {
        if (this.Query is not { } query) return null;

        var (start, end) = document.LineAt(offset);
        var on = query.On(document.Slice(start, end - start).TrimEnd('\r', '\n'));
        if (on.Count == 0) return null;

        var (index, length) = on[nth < on.Count ? nth : 0];
        return (start + index, length);
    }

    /// <summary>Which of the lines found holds match <paramref name="at"/>, counted from nought — found by halving.</summary>
    private int LineOfMatch(int at)
    {
        var (low, high) = (0, _ends.Length - 1);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_ends[middle] > at) high = middle;
            else low = middle + 1;
        }

        return low;
    }

    /// <summary>The match, counted from nought, that is the first on line <paramref name="index"/> of those found — every match, past the last.</summary>
    private int FirstMatchOn(int index) => index == 0 ? 0 : _ends[index - 1];

    /// <summary>The line the reader is on: the match looked at, carried through what was written since, or the caret's where none is.</summary>
    private long Parked() =>
        _marks.LookedAt is { } looked ? _file.LineOf(looked.Start)
      : this.LookedAtLine >= 0 ? this.LookedAtLine
      : _file.LineOf(_file.Editor.Caret);

    /// <summary>Starts a read, cancelling any under way or waiting.</summary>
    private CancellationTokenSource Reading()
    {
        _reading?.Cancel();
        return _reading = new CancellationTokenSource();
    }

    /// <summary>
    /// The file written in, grown or put in place: a search narrowed by hand is carried through it; any other runs again once writing pauses. A
    /// window moving over the file, or the file held another way, changes none of its lines.
    /// </summary>
    private void Written(object? sender, ContentSourceChange change)
    {
        if (change.Kind is ContentChangeKind.Moved or ContentChangeKind.Rebased) return;

        _written++;
        if (!this.IsShowing || _finding > 0) return;

        if (_kept is null)
        {
            this.AgainSoon();
            return;
        }

        if (change.Kind == ContentChangeKind.Loaded || change.Change.Writes.Count == 0) return;

        this.Kept([.. _lines.Select(line => this.Carried(line, change)).Distinct().Order()]);
        this.Said();
    }

    /// <summary>Where line <paramref name="line"/> of the whole file stands once <paramref name="change"/> to the document is made.</summary>
    private long Carried(long line, ContentSourceChange change)
    {
        var (before, after) = (change.Before.Document, change.After.Document);
        var shown = line - _file.LinesBefore;

        if (shown < 0) return line;
        if (shown >= before.LineCount) return line + (after.LineCount - before.LineCount);

        return _file.LinesBefore + after.LineOf(change.Change.Carried(before.OffsetOf((int)shown), leansAfter: true));
    }

    private async void AgainSoon()
    {
        var waiting = this.Reading();

        try
        {
            await Task.Delay(AgainAfter, waiting.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ReferenceEquals(waiting, _reading)) await this.AgainAsync();
    }

    /// <summary>The first of <paramref name="lines"/>, in order, at or after <paramref name="line"/>.</summary>
    private static int LowerBound(long[] lines, long line)
    {
        var (low, high) = (0, lines.Length);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (lines[middle] < line) low = middle + 1;
            else high = middle;
        }

        return low;
    }
}

/// <summary>
/// A search over a document held whole, a place at a time: each place found in order, by a query asked of each line — found again after an
/// edit only on the lines it touched — or by a finder reading the document some other way, as what a page draws is read, found again whole.
/// Places a host found itself, or narrowed to, are carried through each change instead. What it found is found again when the host says the
/// document has come to read (<see cref="Refind"/>), which is never more often than it is drawn.
/// </summary>
public sealed class PlaceFind : ContentFind
{
    private readonly Func<ContentDocument> _current;
    private readonly Func<FoundMarks?> _marks;
    private readonly Action<(int Start, int Length)> _goTo;

    private List<(int Start, int Length)> _places = [];

    /// <summary>The version of the document the places stand in.</summary>
    private ContentDocument? _foundIn;

    /// <summary>What was looked for a line at a time, found again after an edit only where it wrote — or null.</summary>
    private LineQuery? _query;

    /// <summary>What reads the whole document for what was looked for, found again whole — or null.</summary>
    private Func<IReadOnlyList<(int Start, int Length)>>? _finder;

    /// <param name="current">The document as it reads now.</param>
    /// <param name="marks">What marks what it finds — the view's, as it is when marked; null, where the host shows the places itself.</param>
    /// <param name="goTo">Brings a place found before the reader.</param>
    public PlaceFind(Func<ContentDocument> current, Func<FoundMarks?> marks, Action<(int Start, int Length)> goTo) =>
        (_current, _marks, _goTo) = (current, marks, goTo);

    /// <summary>Every place found, in the order a reader comes to them — the order written, for a document searched a line at a time.</summary>
    public IReadOnlyList<(int Start, int Length)> Places => _places;

    public override int Count => _places.Count;

    /// <summary>The place looked at, where one is.</summary>
    public (int Start, int Length)? LookedAt => this.At >= 0 && this.At < _places.Count ? _places[this.At] : null;

    /// <summary>Finds what <paramref name="query"/> finds on each line, a line at a time, and goes to the first at or after <paramref name="from"/>; how many.</summary>
    public int Find(LineQuery query, int from = 0)
    {
        (_query, _finder) = (query, null);
        return this.Found(ContentSearch.Lines(_current(), query.On), from);
    }

    /// <summary>Finds what <paramref name="finder"/> reads the whole document for, and goes to the first at or after <paramref name="from"/>; how many.</summary>
    public int Find(Func<IReadOnlyList<(int Start, int Length)>> finder, int from = 0)
    {
        (_query, _finder) = (null, finder);
        return this.Found(finder(), from);
    }

    /// <summary>
    /// Shows <paramref name="places"/>, which the host found itself, <paramref name="at"/> the one looked at — moving nothing, since the host says
    /// when to go to one. They are carried through each change, not found again: the host found them.
    /// </summary>
    public void Show(IReadOnlyList<(int Start, int Length)> places, int at)
    {
        (_query, _finder, _foundIn) = (null, null, _current());
        _places = [.. places];
        this.At = _places.Count == 0 ? -1 : Math.Clamp(at, -1, _places.Count - 1);
        this.IsShowing = true;

        this.Marked();
        this.Said();
    }

    /// <summary>Narrows what it found to the places named by their order in <see cref="Places"/>, goes to the first, and says how many are left.</summary>
    public int Restrict(IReadOnlySet<int> keep)
    {
        (_query, _finder) = (null, null);
        _places = [.. _places.Where((_, at) => keep.Contains(at))];
        this.At = _places.Count > 0 ? 0 : -1;

        this.Marked();
        if (this.At >= 0) _goTo(_places[this.At]);
        this.Said();

        return _places.Count;
    }

    /// <summary>
    /// What was found, found again in the document as it now reads — written in here or elsewhere, taken back — the one looked at kept to where
    /// its place went. Nothing is gone to: the reader is writing, not looking. Nothing, where the document is the version searched.
    /// </summary>
    public void Refind()
    {
        if (_foundIn is not { } before || !this.IsShowing) return;

        var now = _current();
        if (ReferenceEquals(before, now)) return;

        var change = ContentDocument.Between(before, now);
        var was = this.LookedAt is { } looked ? change is null ? looked.Start : Carried(change, before, looked.Start) : 0;

        _foundIn = now;
        _places = _query is { } query ? ContentSearch.Again(before, now, _places, query.On)
                : _finder is { } finder ? [.. finder()]
                : change is null ? []
                : Carried(_places, change);
        this.At = _places.Count == 0 ? -1 : this.FirstAtOrAfter(was);

        this.Marked();
        this.Said();
    }

    /// <summary>
    /// Where <paramref name="offset"/> in <paramref name="before"/> stands once <paramref name="change"/> is made — the write over it narrowed
    /// first to what it really changed, so a document written whole, as one write, keeps the reader where its text is the same.
    /// </summary>
    private static int Carried(ContentChange change, ContentDocument before, int offset)
    {
        if (!change.Writes.Any(write => write.Start <= offset && offset < write.End)) return change.Carried(offset);

        var writes = change.Writes.Select(write => write.Start <= offset && offset < write.End ? Narrowed(write, before) : write).ToList();
        return new ContentChange(writes, change.Caret).Carried(offset);
    }

    /// <summary><paramref name="write"/> over <paramref name="before"/> without what it writes again as it was, at either end.</summary>
    private static ContentWrite Narrowed(ContentWrite write, ContentDocument before)
    {
        var (was, text) = (before.Slice(write.Start, write.Length), write.Text);
        var start = was.AsSpan().CommonPrefixLength(text);
        var room = Math.Min(was.Length, text.Length) - start;

        var same = 0;
        while (same < room && was[was.Length - 1 - same] == text[text.Length - 1 - same]) same++;

        return new ContentWrite(write.Start + start, write.Length - start - same, text[start..(text.Length - same)]);
    }

    public override void Clear()
    {
        (_query, _finder, _foundIn, _places) = (null, null, null, []);
        (this.At, this.IsShowing) = (-1, false);

        _marks()?.Clear();
        this.Said();
    }

    protected override void Gone(int at)
    {
        _marks()?.LookAt(_places[at]);
        _goTo(_places[at]);
    }

    private int Found(IReadOnlyList<(int Start, int Length)> places, int from)
    {
        _foundIn = _current();
        _places = [.. places];
        this.IsShowing = true;
        this.At = -1;

        this.Marked();

        if (_places.Count > 0) this.GoTo(this.FirstAtOrAfter(from));
        else this.Said();

        return _places.Count;
    }

    /// <summary>What it found marked on the view, the one looked at outlined.</summary>
    private void Marked() => _marks()?.Show(_places, this.LookedAt);

    /// <summary>
    /// The first place, in the order found, at or after <paramref name="from"/>, or the first of them all where none is — read in order rather
    /// than halved, since what a page draws is found in the order a reader comes to it, which need not be the order it is written in.
    /// </summary>
    private int FirstAtOrAfter(int from)
    {
        for (var at = 0; at < _places.Count; at++)
            if (_places[at].Start >= from) return at;

        return 0;
    }

    /// <summary>Each place carried through <paramref name="change"/>; one whose characters were all taken out gone with them.</summary>
    private static List<(int Start, int Length)> Carried(IReadOnlyList<(int Start, int Length)> places, ContentChange change)
    {
        var carried = new List<(int Start, int Length)>(places.Count);

        foreach (var (start, length) in places)
        {
            var now = change.Carried(start, leansAfter: true);
            var end = change.Carried(start + length);
            if (end > now) carried.Add((now, end - now));
        }

        return carried;
    }
}
