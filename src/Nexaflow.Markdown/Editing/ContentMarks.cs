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

/// <summary>The stretch of a document a view is about to draw, which is all a layer of marks is asked about.</summary>
public sealed record ContentMarkRequest(ContentDocument Document, int Start, int End)
{
    /// <summary>
    /// What the content drawn was read as, where one reading holds all of it — a code file's grammar, with what it has coloured — so a layer
    /// marking what the source writes asks the reading rather than reading the source again. Null for content read any other way.
    /// </summary>
    public IReadingSyntax? Syntax { get; init; }
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
/// Stretches a host names, each drawn as <paramref name="kind"/> in <paramref name="token"/> — the matches of a whole-file search, the
/// terms a reader asked to see, the lines picked out. Kept in order, so drawing asks only those reaching the stretch drawn; and carried
/// through every change, so a stretch stays over the characters it named. Content loaded in place of what was there takes them all.
/// </summary>
public sealed class RangeMarks(ContentMarkKind kind, string token, int order = 0) : ContentMarkLayer
{
    private List<(int Start, int Length)> _ranges = [];

    /// <summary>The longest stretch it holds — how far before a stretch drawn one can start and still reach into it.</summary>
    private int _longest;

    public override int Order => order;

    public override bool IsEmpty => _ranges.Count == 0;

    /// <summary>What it is drawn as.</summary>
    public ContentMarkKind Kind => kind;

    /// <summary>The theme token its ink is.</summary>
    public string Token => token;

    /// <summary>The stretches it marks, in order.</summary>
    public IReadOnlyList<(int Start, int Length)> Ranges => _ranges;

    /// <summary>Marks <paramref name="ranges"/> in place of whatever it marked.</summary>
    public void Set(IEnumerable<(int Start, int Length)> ranges)
    {
        _ranges = [.. ranges.Where(range => range.Start >= 0 && range.Length >= 0).OrderBy(range => range.Start)];
        _longest = _ranges.Count == 0 ? 0 : _ranges.Max(range => range.Length);

        this.Remark();
    }

    /// <summary>Marks nothing.</summary>
    public void Clear() => this.Set([]);

    public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into)
    {
        for (var index = this.First(request.Start - _longest); index < _ranges.Count && _ranges[index].Start <= request.End; index++)
        {
            var (start, length) = _ranges[index];
            if (start + length >= request.Start) into.Add(new ContentMark(kind, start, length, token));
        }
    }

    public override void Carry(ContentSourceChange change)
    {
        if (_ranges.Count == 0) return;

        if (change.Kind == ContentChangeKind.Loaded)
        {
            this.Clear();
            return;
        }

        var through = change.Change;
        if (through.Writes.Count == 0) return;

        // A stretch ending before the first write is where it was; every one from there is carried.
        var from = this.First(through.Writes.Min(write => write.Start) - _longest);
        var carried = new List<(int Start, int Length)>(_ranges.Count);
        carried.AddRange(_ranges.Take(from));

        for (var index = from; index < _ranges.Count; index++)
        {
            var (start, length) = _ranges[index];
            var now = through.Carried(start);
            var end = through.Carried(start + length, leansAfter: length > 0);

            // A stretch whose characters were all taken is gone with them; one that named a place still names it.
            if (length > 0 && end <= now) continue;

            carried.Add((now, Math.Max(0, end - now)));
        }

        _ranges = carried;
    }

    /// <summary>The first stretch starting at or after <paramref name="offset"/>.</summary>
    private int First(int offset)
    {
        var (low, high) = (0, _ranges.Count);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_ranges[middle].Start < offset) low = middle + 1;
            else high = middle;
        }

        return low;
    }
}

/// <summary>
/// What a search turned up in the view, and which is being looked at: each washed, and the one looked at outlined as well — a wash
/// alone cannot say which of several is the one. Drawn over the content, never written into it, so clearing a search is a repaint.
/// </summary>
public sealed class FoundMarks : ContentMarkLayer
{
    /// <summary>The theme token a match is washed in.</summary>
    public const string WashToken = "MarkedBrush";

    /// <summary>The theme token the match looked at is outlined in.</summary>
    public const string OutlineToken = "AccentBrush";

    public override int Order => 1000;

    public override bool IsEmpty => this.Places.Count == 0;

    /// <summary>The places found, in order.</summary>
    public IReadOnlyList<(int Start, int Length)> Places { get; private set; } = [];

    /// <summary>Which of them is being looked at, or -1 for none.</summary>
    public int At { get; private set; } = -1;

    /// <summary>Shows <paramref name="places"/> found, <paramref name="at"/> the one looked at.</summary>
    public void Show(IReadOnlyList<(int Start, int Length)> places, int at)
    {
        (this.Places, this.At) = (places ?? [], at);
        this.Remark();
    }

    public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into)
    {
        for (var index = 0; index < this.Places.Count; index++)
        {
            var (start, length) = this.Places[index];
            if (start > request.End || start + length < request.Start) continue;

            into.Add(new ContentMark(ContentMarkKind.Wash, start, length, WashToken));
            if (index == this.At) into.Add(new ContentMark(ContentMarkKind.Outline, start, length, OutlineToken));
        }
    }
}
