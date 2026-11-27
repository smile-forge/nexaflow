using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A surface as automation sees it: a field whose text a screen reader reads a range at a time (<see cref="ITextProvider"/>) — the whole
/// document, what is picked out, what is on the page, a word or a line around a point — and whose value a test reads and sets as a text
/// box's. What the content draws, its links among it, is the content's own to say, beneath this.
///
/// <para>
/// <strong>Ranges read slices, never the whole.</strong> A range is two places in the document; what it reads is the characters between
/// them, so reading a line of a long file costs that line. Only the value is the whole text, and only for a document short enough to be
/// held as one string (<see cref="ContentDocument.RopeFrom"/>); past that its value is that much of the start, and the text pattern is the
/// way into the rest.
/// </para>
/// <para>
/// <strong>A pane it scrolls.</strong> Content that scrolls itself is scrolled as any pane is (<see cref="IScrollProvider"/>): by a step,
/// a page or to a share of the way, through the same view its bars and the wheel move — so a screen reader or a test moves through it as
/// a reader does.
/// </para>
/// </summary>
internal sealed class NexaTextSurfaceAutomationPeer(NexaTextSurface owner) : UserControlAutomationPeer(owner), IValueProvider, ITextProvider, IScrollProvider
{
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Value or PatternInterface.Text => this,
        PatternInterface.Scroll when owner.Engine.Scrolls => this,
        _ => base.GetPattern(patternInterface),
    };

    // ── Value ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Value
    {
        get
        {
            var document = owner.Document.Current;
            return document.Length <= ContentDocument.RopeFrom ? document.ToString() : document.Slice(0, ContentDocument.RopeFrom);
        }
    }

    /// <inheritdoc/>
    public bool IsReadOnly => owner.IsReadOnly;

    /// <summary>The document's text made <paramref name="value"/>, as one step the reader can take back.</summary>
    public void SetValue(string value)
    {
        if (owner.IsReadOnly) throw new ElementNotEnabledException();

        owner.Replace(0, owner.Document.Current.Length, value);
    }

    // ── Text ────────────────────────────────────────────────────────────────

    internal NexaTextSurface Owner => owner;

    /// <inheritdoc/>
    public ITextRangeProvider DocumentRange => new SurfaceTextRange(this, 0, owner.Document.Current.Length);

    /// <inheritdoc/>
    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;

    /// <inheritdoc/>
    public ITextRangeProvider[] GetSelection()
    {
        var editor = owner.Editor;
        if (!editor.HasSelection) return [new SurfaceTextRange(this, editor.Caret, editor.Caret)];

        return [.. editor.Selection.Select(range => new SurfaceTextRange(this, range.Start, range.End))];
    }

    /// <inheritdoc/>
    public ITextRangeProvider[] GetVisibleRanges()
    {
        var lines = owner.LinesShown();
        var document = owner.Document.Current;

        if (lines.Count == 0) return [new SurfaceTextRange(this, 0, Math.Min(document.Length, ContentDocument.FlatBelow))];

        var start = document.OffsetOf(lines[0].Number - 1);
        var end = document.LineAt(document.OffsetOf(lines[^1].Number - 1)).End;

        return [new SurfaceTextRange(this, start, end)];
    }

    /// <inheritdoc/>
    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement) => DocumentRange;

    /// <inheritdoc/>
    public ITextRangeProvider RangeFromPoint(Point screenLocation)
    {
        var shown = owner.Shown;
        var local = shown.PointFromScreen(screenLocation);
        var at = Math.Clamp(shown.Laid.OffsetAt(new LayoutPoint(local.X / shown.Zoom, local.Y / shown.Zoom)), 0, owner.Document.Current.Length);

        return new SurfaceTextRange(this, at, at);
    }

    internal IRawElementProviderSimple Provider => ProviderFromPeer(this);

    // ── Scroll ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool VerticallyScrollable => owner.ScrollableHeight >= 1;

    /// <inheritdoc/>
    public bool HorizontallyScrollable => owner.ScrollableWidth >= 1;

    /// <inheritdoc/>
    public double VerticalScrollPercent => this.VerticallyScrollable ? Math.Clamp(100 * owner.VerticalOffset / owner.ScrollableHeight, 0, 100) : ScrollPatternIdentifiers.NoScroll;

    /// <inheritdoc/>
    public double HorizontalScrollPercent => this.HorizontallyScrollable ? Math.Clamp(100 * owner.HorizontalOffset / owner.ScrollableWidth, 0, 100) : ScrollPatternIdentifiers.NoScroll;

    /// <inheritdoc/>
    public double VerticalViewSize => owner.ExtentHeight > 0 ? Math.Min(100, 100 * owner.ViewportHeight / owner.ExtentHeight) : 100;

    /// <inheritdoc/>
    public double HorizontalViewSize => owner.ExtentWidth > 0 ? Math.Min(100, 100 * owner.ViewportWidth / owner.ExtentWidth) : 100;

    /// <summary>The view moved by a step — a notch of the wheel — or a page, either way, along whichever way is asked.</summary>
    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        if (!this.IsEnabled()) throw new ElementNotEnabledException();

        if (horizontalAmount != ScrollAmount.NoAmount)
        {
            if (!this.HorizontallyScrollable) throw new InvalidOperationException("The view does not slide sideways.");
            owner.ScrollToHorizontalOffset(owner.HorizontalOffset + Stepped(horizontalAmount, owner.ViewportWidth));
        }

        if (verticalAmount != ScrollAmount.NoAmount)
        {
            if (!this.VerticallyScrollable) throw new InvalidOperationException("The view does not scroll.");
            owner.ScrollToVerticalOffset(owner.VerticalOffset + Stepped(verticalAmount, owner.ViewportHeight));
        }
    }

    /// <summary>The view moved to a share of the way along, either way — <see cref="ScrollPatternIdentifiers.NoScroll"/> leaving that way as it is.</summary>
    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        if (!this.IsEnabled()) throw new ElementNotEnabledException();

        if (horizontalPercent != ScrollPatternIdentifiers.NoScroll) owner.ScrollToHorizontalOffset(owner.ScrollableWidth * Share(horizontalPercent));
        if (verticalPercent != ScrollPatternIdentifiers.NoScroll) owner.ScrollToVerticalOffset(owner.ScrollableHeight * Share(verticalPercent));
    }

    /// <summary>How far <paramref name="amount"/> moves a view <paramref name="view"/> long: a notch of the wheel, or the view's length.</summary>
    private static double Stepped(ScrollAmount amount, double view) => amount switch
    {
        ScrollAmount.SmallDecrement => -NexaTextSurface.Notch,
        ScrollAmount.SmallIncrement => NexaTextSurface.Notch,
        ScrollAmount.LargeDecrement => -Math.Max(1, view),
        ScrollAmount.LargeIncrement => Math.Max(1, view),
        _ => 0,
    };

    /// <summary>A percentage as the share of the way it says, refused outside nought to a hundred.</summary>
    private static double Share(double percent) =>
        percent is >= 0 and <= 100 ? percent / 100 : throw new ArgumentOutOfRangeException(nameof(percent), percent, "A share of the way runs from 0 to 100.");

    /// <summary>Where the view stood as a screen reader was last told, null before it has been told anything.</summary>
    private (double Across, double Down, double Wide, double Tall, bool Slides, bool Scrolls)? _told;

    /// <summary>Tells whoever is listening that the view moved, or what it stands over changed size — each thing that changed since last told.</summary>
    internal void Scrolled()
    {
        var now = (this.HorizontalScrollPercent, this.VerticalScrollPercent, this.HorizontalViewSize, this.VerticalViewSize, this.HorizontallyScrollable, this.VerticallyScrollable);
        var was = _told ?? now;
        _told = now;

        if (was.Across != now.Item1) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontalScrollPercentProperty, was.Across, now.Item1);
        if (was.Down != now.Item2) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalScrollPercentProperty, was.Down, now.Item2);
        if (was.Wide != now.Item3) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontalViewSizeProperty, was.Wide, now.Item3);
        if (was.Tall != now.Item4) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalViewSizeProperty, was.Tall, now.Item4);
        if (was.Slides != now.Item5) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontallyScrollableProperty, was.Slides, now.Item5);
        if (was.Scrolls != now.Item6) this.RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticallyScrollableProperty, was.Scrolls, now.Item6);
    }
}

/// <summary>
/// A stretch of a surface's document as automation reads it: two places, moved by characters, words, lines and the whole; read a slice
/// at a time; bounded on the screen where it is laid.
/// </summary>
internal sealed class SurfaceTextRange(NexaTextSurfaceAutomationPeer peer, int start, int end) : ITextRangeProvider
{
    private int _start = start;
    private int _end = end;

    private ContentDocument Document => peer.Owner.Document.Current;

    /// <summary>Both ends kept inside the document as it now reads, and in order.</summary>
    private (int Start, int End) Ends
    {
        get
        {
            var length = this.Document.Length;
            var (low, high) = (Math.Clamp(_start, 0, length), Math.Clamp(_end, 0, length));

            return low <= high ? (low, high) : (high, low);
        }
    }

    public ITextRangeProvider Clone() => new SurfaceTextRange(peer, _start, _end);

    public bool Compare(ITextRangeProvider range) => range is SurfaceTextRange other && other.Ends == this.Ends;

    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not SurfaceTextRange other) throw new ArgumentException("A range of another control.", nameof(targetRange));

        return Endpoint(endpoint).CompareTo(other.Endpoint(targetEndpoint));
    }

    public void ExpandToEnclosingUnit(TextUnit unit) => (_start, _end) = this.Enclosing(this.Ends.Start, unit);

    public ITextRangeProvider? FindAttribute(int attribute, object value, bool backward) => null;

    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var (from, to) = this.Ends;
        var document = this.Document;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var (first, last) = (document.LineOf(from), document.LineOf(to));

        // A line at a time: no match runs across a line break, and nothing longer than a line is read to find one.
        for (var step = 0; step <= last - first; step++)
        {
            var line = backward ? last - step : first + step;
            var (lineStart, lineEnd) = document.LineAt(document.OffsetOf(line));
            var (low, high) = (Math.Max(lineStart, from), Math.Min(lineEnd, to));
            if (high - low < text.Length) continue;

            var read = document.Slice(low, high - low);
            var at = backward ? read.LastIndexOf(text, comparison) : read.IndexOf(text, comparison);
            if (at >= 0) return new SurfaceTextRange(peer, low + at, low + at + text.Length);
        }

        return null;
    }

    public object GetAttributeValue(int attribute) =>
        attribute == TextPattern.IsReadOnlyAttribute.Id ? peer.Owner.IsReadOnly : AutomationElementIdentifiers.NotSupported;

    public double[] GetBoundingRectangles()
    {
        var (from, to) = this.Ends;
        var shown = peer.Owner.Shown;
        var rects = to > from ? shown.Laid.Root.RangeRects(from, to - from) : [shown.Laid.Root.CaretRect(from)];
        var bounds = new List<double>(rects.Count * 4);

        foreach (var rect in rects.Where(rect => !rect.IsEmpty))
        {
            var corner = shown.PointToScreen(new Point(rect.X * shown.Zoom, rect.Y * shown.Zoom));
            bounds.AddRange([corner.X, corner.Y, rect.Width * shown.Zoom, rect.Height * shown.Zoom]);
        }

        return [.. bounds];
    }

    public IRawElementProviderSimple GetEnclosingElement() => peer.Provider;

    public string GetText(int maxLength)
    {
        var (from, to) = this.Ends;
        var length = maxLength < 0 ? to - from : Math.Min(maxLength, to - from);

        return this.Document.Slice(from, length);
    }

    public int Move(TextUnit unit, int count)
    {
        // A range moved is first made one unit wide, then moved that many units, as UI Automation says.
        var (from, _) = this.Ends;
        var moved = 0;
        var at = this.Enclosing(from, unit).Start;

        while (moved != count)
        {
            var next = this.Step(at, unit, forward: count > 0);
            if (next == at) break;

            at = next;
            moved += Math.Sign(count);
        }

        (_start, _end) = this.Enclosing(at, unit);
        return moved;
    }

    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        var at = this.Endpoint(endpoint);
        var moved = 0;

        while (moved != count)
        {
            var next = this.Step(at, unit, forward: count > 0);
            if (next == at) break;

            at = next;
            moved += Math.Sign(count);
        }

        this.Set(endpoint, at);
        return moved;
    }

    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not SurfaceTextRange other) throw new ArgumentException("A range of another control.", nameof(targetRange));

        this.Set(endpoint, other.Endpoint(targetEndpoint));
    }

    public void Select()
    {
        var (from, to) = this.Ends;
        peer.Owner.Editor.Select(from, to - from);
    }

    public void AddToSelection() => throw new InvalidOperationException("Only one stretch is picked out at a time.");

    public void RemoveFromSelection() => throw new InvalidOperationException("Only one stretch is picked out at a time.");

    public void ScrollIntoView(bool alignToTop) =>
        peer.Owner.ScrollToLine(this.Document.LineOf(this.Ends.Start), alignToTop ? ShownPlace.Top : ShownPlace.Nearest);

    public IRawElementProviderSimple[] GetChildren() => [];

    // ── Mechanics ───────────────────────────────────────────────────────────

    private int Endpoint(TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? this.Ends.Start : this.Ends.End;

    /// <summary>One end moved to <paramref name="at"/>, the other with it where it would otherwise be passed.</summary>
    private void Set(TextPatternRangeEndpoint endpoint, int at)
    {
        var (from, to) = this.Ends;

        if (endpoint == TextPatternRangeEndpoint.Start) (_start, _end) = (at, Math.Max(at, to));
        else (_start, _end) = (Math.Min(from, at), at);
    }

    /// <summary>The unit holding <paramref name="at"/>: a character, a word, a line with its line break, or the whole.</summary>
    private (int Start, int End) Enclosing(int at, TextUnit unit)
    {
        var document = this.Document;
        var length = document.Length;

        switch (unit)
        {
            case TextUnit.Character:
                return (at, Math.Min(at + 1, length));

            case TextUnit.Format or TextUnit.Word:
                var after = ContentWordEdges.Next(document, at, forward: true);
                var begins = ContentWordEdges.Next(document, Math.Min(at + 1, length), forward: false);
                var start = begins <= at ? begins : at;
                return (start, Math.Max(after, ContentWordEdges.Next(document, start, forward: true)));

            case TextUnit.Line or TextUnit.Paragraph:
                var line = document.LineOf(at);
                return (document.OffsetOf(line), line + 1 < document.LineCount ? document.OffsetOf(line + 1) : length);

            default:
                return (0, length);
        }
    }

    /// <summary>Where the start of the next unit after <paramref name="at"/> is — or the one before, back — or <paramref name="at"/> at an end.</summary>
    private int Step(int at, TextUnit unit, bool forward)
    {
        var document = this.Document;

        switch (unit)
        {
            case TextUnit.Character:
                return Math.Clamp(at + (forward ? 1 : -1), 0, document.Length);

            case TextUnit.Format or TextUnit.Word:
                return ContentWordEdges.Next(document, at, forward);

            case TextUnit.Line or TextUnit.Paragraph:
                var line = document.LineOf(at) + (forward ? 1 : -1);
                return line < 0 ? 0 : line >= document.LineCount ? document.Length : document.OffsetOf(line);

            default:
                return forward ? document.Length : 0;
        }
    }
}
