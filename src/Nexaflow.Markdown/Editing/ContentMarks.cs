using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Editing;

/// <summary>What a mark is drawn as over the content it names.</summary>
public enum ContentMarkKind
{
    /// <summary>A wash over the characters — a match found, a term picked out.</summary>
    Wash,

    /// <summary>A line round the characters — the match being looked at.</summary>
    Outline,

    /// <summary>A wash across the whole width of every row of the lines it touches — a level, a line picked out.</summary>
    LineWash,

    /// <summary>A veil across the whole width of every row of the lines it touches, fading what is under it — a line a filter leaves out.</summary>
    Veil,

    /// <summary>A bar under the characters — a colour written in the source, shown in that colour.</summary>
    Underbar,
}

/// <summary>
/// One mark over the content: what it is drawn as, the characters it names, and the theme token its ink is — resolved when it is drawn,
/// never a colour, so a theme repaints it. A bar under a colour the source itself writes carries that colour instead
/// (<see cref="Colour"/>): it shows what the document says, not a choice of the app's. A bar with no colour of its own is drawn in what
/// its token names only where the theme names it, so a bar under a resource key the theme does not hold is not drawn at all.
/// </summary>
public readonly record struct ContentMark(ContentMarkKind Kind, int Start, int Length, string Token)
{
    /// <summary>The colour an <see cref="ContentMarkKind.Underbar"/> shows, where the document names one: ARGB.</summary>
    public uint? Colour { get; init; }

    /// <summary>One past the last character it names.</summary>
    public int End => this.Start + this.Length;
}

/// <summary>
/// The stretch of a document a view is about to draw, which is all a layer of marks is asked about — with the lines it covers, sliced once
/// however many layers read them (<see cref="Lines"/>), and what any layer works out from each of those lines, worked out once for them all
/// (<see cref="Read{T}"/>).
/// </summary>
public sealed class ContentMarkRequest(ContentDocument document, int start, int end)
{
    private IReadOnlyList<DrawnLine>? _lines;

    /// <summary>What has been made of the lines drawn, by the reading that made it.</summary>
    private Dictionary<object, object>? _readings;

    /// <summary>The document drawn.</summary>
    public ContentDocument Document { get; } = document;

    /// <summary>Where the stretch drawn starts.</summary>
    public int Start { get; } = start;

    /// <summary>Where it ends.</summary>
    public int End { get; } = end;

    /// <summary>
    /// What the content drawn was read as, where one reading holds all of it — a code file's grammar, with what it has coloured — so a layer
    /// marking what the source writes asks the reading rather than reading the source again. Null for content read any other way.
    /// </summary>
    public IReadingSyntax? Syntax { get; init; }

    /// <summary>Every line the stretch drawn touches, in order — sliced the first time a layer asks, and handed to every layer after it.</summary>
    public IReadOnlyList<DrawnLine> Lines => _lines ??= this.Sliced();

    /// <summary>
    /// What <paramref name="reading"/> makes of each of <see cref="Lines"/>, in their order — worked out the first time any layer asks, and handed
    /// to every other layer asking with the same reading.
    /// </summary>
    public IReadOnlyList<T> Read<T>(LineReading<T> reading)
    {
        _readings ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        if (_readings.TryGetValue(reading, out var made)) return (IReadOnlyList<T>)made;

        var lines = this.Lines;
        var read = new T[lines.Count];
        for (var at = 0; at < lines.Count; at++) read[at] = reading.Of(lines[at].Text);

        _readings[reading] = read;
        return read;
    }

    private List<DrawnLine> Sliced()
    {
        var document = this.Document;
        var (start, end) = (Math.Clamp(this.Start, 0, document.Length), Math.Clamp(this.End, 0, document.Length));
        var lines = new List<DrawnLine>();

        for (var line = document.LineOf(start); line < document.LineCount; line++)
        {
            var (from, to) = document.LineAt(document.OffsetOf(line));
            if (from > end) break;

            lines.Add(new DrawnLine(line, from, to, document.Slice(from, to - from).TrimEnd('\r', '\n')));
        }

        return lines;
    }
}

/// <summary>
/// A line of the stretch a view draws: which line of the document it is, from nought; where it starts and where it ends, its line end
/// included; and what is written on it, without its end.
/// </summary>
public readonly record struct DrawnLine(int Line, int Start, int End, string Text);

/// <summary>
/// One way of reading a line that more than one layer of marks asks of the lines drawn — a log line read for its level and its time, say.
/// Asked through <see cref="ContentMarkRequest.Read{T}"/>, each line drawn is read once however many layers ask: the layers sharing a reading
/// share the one instance.
/// </summary>
public sealed class LineReading<T>(Func<string, T> read)
{
    /// <summary>What it makes of a line on which <paramref name="text"/> is written.</summary>
    public T Of(string text) => read(text);
}

/// <summary>
/// One layer of marks a host lays over a view of a document (<see cref="ContentEditor.Marks"/>) — what it found, what a filter leaves out,
/// each level a log line is at. Asked only for the stretch about to be drawn, so marking every line of a long document costs a screen's
/// worth; carried through every change to the document before the change is drawn; and saying when what it marks has changed, so it is
/// drawn again.
/// </summary>
public abstract class ContentMarkLayer
{
    /// <summary>Which layers are drawn over which: higher over lower.</summary>
    public virtual int Order => 0;

    /// <summary>Whether it marks nothing at all just now — so a view with nothing marked works out nothing to draw it.</summary>
    public virtual bool IsEmpty => false;

    /// <summary>Raised when what it marks has changed, so it is drawn again.</summary>
    public event EventHandler? Remarked;

    /// <summary>The marks it lays over <paramref name="request"/>'s stretch, added to <paramref name="into"/>.</summary>
    public abstract void Mark(ContentMarkRequest request, ICollection<ContentMark> into);

    /// <summary>What it marks carried through <paramref name="change"/>, made to the document before anything is drawn again.</summary>
    public virtual void Carry(ContentSourceChange change)
    {
    }

    /// <summary>Says what it marks has changed.</summary>
    protected void Remark() => this.Remarked?.Invoke(this, EventArgs.Empty);
}

/// <summary>The layers of marks over a view of a document, in the order they are drawn: what it holds, and what it says when any changes.</summary>
public sealed class ContentMarkLayers : IReadOnlyList<ContentMarkLayer>
{
    private readonly List<ContentMarkLayer> _layers = [];

    /// <summary>Raised when a layer comes or goes, or says what it marks has changed.</summary>
    public event EventHandler? Changed;

    public int Count => _layers.Count;

    public ContentMarkLayer this[int index] => _layers[index];

    /// <summary>Lays <paramref name="layer"/> over the view, among the others by its order.</summary>
    public void Add(ContentMarkLayer layer)
    {
        if (_layers.Contains(layer)) return;

        var at = _layers.FindIndex(other => other.Order > layer.Order);
        _layers.Insert(at < 0 ? _layers.Count : at, layer);
        layer.Remarked += this.Relayed;

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes <paramref name="layer"/> off the view.</summary>
    public bool Remove(ContentMarkLayer layer)
    {
        if (!_layers.Remove(layer)) return false;

        layer.Remarked -= this.Relayed;
        this.Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Every mark the layers lay over <paramref name="request"/>'s stretch, lowest layer first.</summary>
    public List<ContentMark> Mark(ContentMarkRequest request)
    {
        var marks = new List<ContentMark>();
        foreach (var layer in _layers) layer.Mark(request, marks);

        return marks;
    }

    /// <summary>Every layer's marks carried through <paramref name="change"/>.</summary>
    internal void Carry(ContentSourceChange change)
    {
        foreach (var layer in _layers) layer.Carry(change);
    }

    public IEnumerator<ContentMarkLayer> GetEnumerator() => _layers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private void Relayed(object? sender, EventArgs args) => this.Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// What a search found in the view, and which match is being looked at: each washed, and the one looked at outlined as well — a wash alone
/// cannot say which of several is the one. What it marks is either what a query finds on each line drawn — worked out there and nowhere
/// else, so a document of any length costs a screen of matching however many matches it holds, and what is marked stays true through every
/// edit and every move of a window onto a file — or places found some other way, as what a page draws: kept in order, asked only for those
/// reaching the stretch drawn, and carried through every change. Drawn over the content, never written into it, so clearing a search is a
/// repaint.
/// </summary>
/// <param name="washToken">What the matches are washed in: what a search found, unless they are something else's — a term a reader asked to
/// have picked out, say.</param>
/// <param name="order">Where among the layers they are drawn.</param>
public sealed class FoundMarks(string washToken = FoundMarks.WashToken, int order = 1000) : ContentMarkLayer
{
    /// <summary>The theme token a match is washed in.</summary>
    public const string WashToken = "MarkedBrush";

    /// <summary>The theme token the match looked at is outlined in.</summary>
    public const string OutlineToken = "AccentBrush";

    /// <summary>What it finds on each line drawn, or null where it marks places instead.</summary>
    private LineQuery? _query;

    /// <summary>Which lines drawn, by their line in the document from nought, the query is asked of — every one, where null.</summary>
    private Func<int, bool>? _asked;

    /// <summary>The places it marks, in order, where it marks no query's matches.</summary>
    private List<(int Start, int Length)> _places = [];

    /// <summary>The longest place — how far before a stretch drawn one can start and still reach into it.</summary>
    private int _longest;

    public override int Order => order;

    public override bool IsEmpty => _query is null && _places.Count == 0;

    /// <summary>The query whose matches it marks on each line drawn, or null.</summary>
    public LineQuery? Query => _query;

    /// <summary>The places it marks, in order, where it marks places rather than a query's matches.</summary>
    public IReadOnlyList<(int Start, int Length)> Places => _places;

    /// <summary>The match being looked at, outlined, where one is.</summary>
    public (int Start, int Length)? LookedAt { get; private set; }

    /// <summary>
    /// Marks what <paramref name="query"/> finds on each line drawn — on those <paramref name="lines"/> keeps, by their line in the document from
    /// nought, where it is given — and nothing, where the query is null; looking at none of it yet.
    /// </summary>
    public void Show(LineQuery? query, Func<int, bool>? lines = null)
    {
        (_query, _asked, _places, _longest) = (query, query is null ? null : lines, [], 0);
        this.LookedAt = null;
        this.Remark();
    }

    /// <summary>Marks <paramref name="places"/>, found some other way, in place of whatever it marked — <paramref name="lookedAt"/> the one looked at.</summary>
    public void Show(IEnumerable<(int Start, int Length)> places, (int Start, int Length)? lookedAt = null)
    {
        var kept = places.Where(place => place.Start >= 0 && place.Length > 0).ToList();
        if (!InOrder(kept)) kept.Sort((one, other) => one.Start.CompareTo(other.Start));

        (_query, _asked, _places) = (null, null, kept);
        _longest = kept.Count == 0 ? 0 : kept.Max(place => place.Length);
        this.LookedAt = lookedAt;
        this.Remark();
    }

    /// <summary>Marks nothing.</summary>
    public void Clear() => this.Show(query: null);

    /// <summary>Outlines <paramref name="match"/> as the one looked at — none, where it is null.</summary>
    public void LookAt((int Start, int Length)? match)
    {
        if (this.LookedAt == match) return;

        this.LookedAt = match;
        this.Remark();
    }

    public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into)
    {
        if (_query is { } query)
        {
            foreach (var line in request.Lines)
            {
                if (_asked is { } asked && !asked(line.Line)) continue;

                foreach (var (index, length) in query.On(line.Text)) this.Marked(into, line.Start + index, length);
            }

            return;
        }

        for (var index = this.First(request.Start - _longest); index < _places.Count && _places[index].Start <= request.End; index++)
        {
            var (start, length) = _places[index];
            if (start + length >= request.Start) this.Marked(into, start, length);
        }
    }

    /// <summary>
    /// Carries the match looked at, and every place, through <paramref name="change"/>, so each stays over the characters it was found at — one
    /// whose characters were all taken out gone with them. Content loaded in place of what was there takes the places and the one looked at.
    /// </summary>
    public override void Carry(ContentSourceChange change)
    {
        // Nothing placed and nothing looked at: a keystroke asks nothing of the change.
        if (this.LookedAt is null && _places.Count == 0) return;

        if (change.Kind == ContentChangeKind.Loaded)
        {
            (this.LookedAt, _places, _longest) = (null, [], 0);
            return;
        }

        var through = change.Change;
        if (through.Writes.Count == 0) return;

        if (this.LookedAt is { } at) this.LookedAt = Carried(through, at);
        if (_places.Count == 0) return;

        // A place ending before the first write is where it was; every one from there is carried.
        var from = this.First(through.Writes.Min(write => write.Start) - _longest);
        var carried = new List<(int Start, int Length)>(_places.Count);
        carried.AddRange(_places.GetRange(0, from));

        for (var index = from; index < _places.Count; index++)
            if (Carried(through, _places[index]) is { } place) carried.Add(place);

        _places = carried;
        _longest = carried.Count == 0 ? 0 : carried.Max(place => place.Length);
    }

    /// <summary>
    /// Where <paramref name="place"/> stands once <paramref name="change"/> is made — what is written right before or right after it is not what
    /// was found there, so neither end takes it in — or null, where all its characters were taken out.
    /// </summary>
    private static (int Start, int Length)? Carried(ContentChange change, (int Start, int Length) place)
    {
        var start = change.Carried(place.Start, leansAfter: true);
        var end = change.Carried(place.Start + place.Length);

        return end > start ? (start, end - start) : null;
    }

    private void Marked(ICollection<ContentMark> into, int start, int length)
    {
        into.Add(new ContentMark(ContentMarkKind.Wash, start, length, washToken));
        if (this.LookedAt == (start, length)) into.Add(new ContentMark(ContentMarkKind.Outline, start, length, OutlineToken));
    }

    /// <summary>The first place starting at or after <paramref name="offset"/>.</summary>
    private int First(int offset)
    {
        var (low, high) = (0, _places.Count);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_places[middle].Start < offset) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    private static bool InOrder(List<(int Start, int Length)> places)
    {
        for (var index = 1; index < places.Count; index++)
            if (places[index].Start < places[index - 1].Start) return false;

        return true;
    }
}

/// <summary>
/// Each line drawn marked by what is written on it — a level's wash, a filter's veil, a line picked out — asked only of the lines on screen,
/// so a document of any length costs a screen of asking. What a line is marked with is the token the function it was shown with gives it, or
/// nothing; shown again, every line drawn is asked again. A function asking what a shared reading makes of the line (<see cref="LineReading{T}"/>)
/// has each line read once however many layers ask the same reading.
/// </summary>
/// <param name="kind">How a line is marked: across its whole width, as a wash or a veil.</param>
/// <param name="order">Where among the layers the lines are drawn.</param>
public sealed class LineMarks(ContentMarkKind kind, int order) : ContentMarkLayer
{
    /// <summary>What marks the lines of a stretch drawn, or null for no marking.</summary>
    private Action<ContentMarkRequest, ICollection<ContentMark>>? _marking;

    public override int Order => order;

    public override bool IsEmpty => _marking is null;

    /// <summary>How a line is marked.</summary>
    public ContentMarkKind Kind => kind;

    /// <summary>
    /// Marks each line drawn with the token <paramref name="tokenFor"/> gives it — asked the line's place in the document, from nought, and what
    /// is written on it without its end — or leaves it where it gives none; null marks nothing. Shown again with the same function, every line
    /// drawn is asked again: what it answers from has changed.
    /// </summary>
    public void Show(Func<int, string, string?>? tokenFor) =>
        this.Shown(tokenFor is null ? null : (request, into) =>
        {
            foreach (var line in request.Lines) this.Marked(into, line, tokenFor(line.Line, line.Text));
        });

    /// <summary>
    /// Marks each line drawn with the token <paramref name="tokenFor"/> gives it — asked the line's place in the document, from nought, what is
    /// written on it without its end, and what <paramref name="reading"/> makes of it, read once for every layer asking the same reading — or
    /// leaves it where it gives none; null marks nothing.
    /// </summary>
    public void Show<T>(LineReading<T> reading, Func<int, string, T, string?>? tokenFor) =>
        this.Shown(tokenFor is null ? null : (request, into) =>
        {
            var (lines, read) = (request.Lines, request.Read(reading));
            for (var at = 0; at < lines.Count; at++) this.Marked(into, lines[at], tokenFor(lines[at].Line, lines[at].Text, read[at]));
        });

    public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into) => _marking?.Invoke(request, into);

    private void Shown(Action<ContentMarkRequest, ICollection<ContentMark>>? marking)
    {
        _marking = marking;
        this.Remark();
    }

    private void Marked(ICollection<ContentMark> into, DrawnLine line, string? token)
    {
        if (token is not null) into.Add(new ContentMark(kind, line.Start, line.End - line.Start, token));
    }
}
