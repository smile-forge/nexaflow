using System;

using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The view of the page: which part of it is on screen, held here — where the content is laid out — in the content's own units.
///
/// <para>
/// <strong>The engine's, not the control's.</strong> The engine already decides what to read and lay from what is on screen
/// (<see cref="OnScreen"/>), holds the reader still while what stands above them comes to its height, and brings a place onto the page; so
/// where the view stands is its to keep too. What shows the content draws the part the view stands over, measured from the view's corner
/// (<see cref="ViewOrigin"/>), and turns a scrollbar, the wheel and the keys into moves of the view.
/// </para>
/// <para>
/// <strong>Why it is measured from the view.</strong> Drawing takes its places in single precision: a line millions of pixels down a long
/// document, handed over where it stands on the page, lands somewhere else, or nowhere. Measured from the view, nothing drawn is ever
/// further than a screen or two from nought, however long the document.
/// </para>
/// <para>
/// Content shown whole inside something that scrolls it — a reply in a conversation — has no view of its own (<see cref="Scrolls"/> is
/// false): it is drawn where it stands, and whatever holds it says which part of it is on screen.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>Room kept round the content inside the view — part of what scrolls, in the content's own units: left, top, right, bottom.</summary>
    private (double Left, double Top, double Right, double Bottom) _inset;

    /// <summary>
    /// Whether the view of the page is the engine's own: the content scrolls itself, and is drawn from where the view stands. False for content
    /// shown whole inside something that scrolls it.
    /// </summary>
    internal bool Scrolls { get; set; }

    /// <summary>Whether the view slides sideways as well — for lines kept whole that reach past the page's edge.</summary>
    internal bool ViewSlides { get; set; }

    /// <summary>
    /// Raised when the view moved or changed its size — scrolled, brought to a place, held still while the page above it changed, or the content
    /// under it laid again — and when the page under it came to another length or width, though the view stood still.
    /// </summary>
    internal event EventHandler? ViewMoved;

    /// <summary>
    /// Where the view's corner stands on the page, in the content's own units — what everything drawn is measured from. Nought for content with
    /// no view of its own.
    /// </summary>
    internal LayoutVector ViewOrigin => Scrolls && _showing is { } view ? new LayoutVector(view.X, view.Y) : default;

    /// <summary>How far the view can scroll through, in the content's own units: the content, and the room kept round it.</summary>
    internal LayoutSize Extent
    {
        get
        {
            var size = (Preview?.Laid ?? Laid).Size;
            return new LayoutSize(size.Width + _inset.Left + _inset.Right, size.Height + _inset.Top + _inset.Bottom);
        }
    }

    /// <summary>The view's top as a scrollbar counts it — nought where the room kept above the content starts.</summary>
    internal double ScrolledDown => _showing is { } view ? view.Y + _inset.Top : 0;

    /// <summary>The view's left edge as a scrollbar counts it.</summary>
    internal double ScrolledAcross => _showing is { } view ? view.X + _inset.Left : 0;

    /// <summary>
    /// Keeps <paramref name="left"/>, <paramref name="top"/>, <paramref name="right"/> and <paramref name="bottom"/> of room round the content, in its
    /// own units — and lays it again where the room at its sides changed, since that is room it is no longer laid in.
    /// </summary>
    internal void Inset(double left, double top, double right, double bottom)
    {
        if (_inset == (left, top, right, bottom)) return;

        var (x, y) = _showing is { } view ? (view.X + _inset.Left, view.Y + _inset.Top) : (0, 0);
        var sides = _inset.Left + _inset.Right != left + right;
        _inset = (left, top, right, bottom);

        if (sides && _laidFor > 0 && _laid.Tree.Count > 0) this.Relay();

        // Where the view stood against the room kept round the content stays where it stood.
        this.ViewAt(x - left, y - top, _showing?.Width ?? 0, _showing?.Height ?? 0);
    }

    /// <summary>The view is <paramref name="width"/> by <paramref name="height"/>, in the content's own units — standing where it stood, as far as the content lets it.</summary>
    internal void ViewSized(double width, double height)
    {
        var (x, y) = _showing is { } view ? (view.X, view.Y) : (-_inset.Left, -_inset.Top);
        this.ViewAt(x, y, width, height);
    }

    /// <summary>The view kept inside the content, after the content was laid again — and said to have moved where that moved it.</summary>
    internal void ViewKept()
    {
        if (_showing is { } view) this.ViewAt(view.X, view.Y, view.Width, view.Height);
    }

    /// <summary>Scrolls the view so its top stands <paramref name="down"/> into the content, as a scrollbar counts it.</summary>
    internal void ScrollTo(double down)
    {
        if (_showing is not { } view) return;

        this.ViewAt(view.X, down - _inset.Top, view.Width, view.Height);
    }

    /// <summary>Stands the view's top at <paramref name="top"/> on the page — what a line gone to is stood at.</summary>
    internal void TopAt(double top)
    {
        if (_showing is not { } view) return;

        this.ViewAt(view.X, top, view.Width, view.Height);
    }

    /// <summary>Slides the view so its left edge stands <paramref name="across"/> into the content, as a scrollbar counts it.</summary>
    internal void SlideViewTo(double across)
    {
        if (_showing is not { } view || !ViewSlides) return;

        this.ViewAt(across - _inset.Left, view.Y, view.Width, view.Height);
    }

    /// <summary>Scrolls the view by <paramref name="by"/>, down where positive — and says whether it moved at all.</summary>
    internal bool ScrollBy(double by)
    {
        if (_showing is not { } view) return false;

        return this.ViewAt(view.X, view.Y + by, view.Width, view.Height);
    }

    /// <summary>Slides the view by <paramref name="by"/>, towards the end of the lines where positive — and says whether it moved at all.</summary>
    internal bool SlideViewBy(double by)
    {
        if (_showing is not { } view || !ViewSlides) return false;

        return this.ViewAt(view.X + by, view.Y, view.Width, view.Height);
    }

    /// <summary>Whether the view can scroll at all by <paramref name="by"/> — what decides whether a turn of the wheel is this content's, or goes on to what holds it.</summary>
    internal bool CanScroll(double by)
    {
        if (!Scrolls || _showing is not { } view || by == 0) return false;

        var (top, bottom) = this.Reach(view.Height);
        return by < 0 ? view.Y > top : view.Y < bottom;
    }

    /// <summary>
    /// Moves the view no further than it must for <paramref name="place"/> to stand in it — what a caret moving keeps to, so the page moves a
    /// line at a time as it goes, and not at all while it stays on screen.
    /// </summary>
    internal void KeepInView(LayoutRect place)
    {
        if (_showing is not { } view || place.IsEmpty) return;

        var y = place.Y < view.Y || place.Height > view.Height ? place.Y
              : place.Bottom > view.Bottom ? place.Bottom - view.Height
              : view.Y;
        var x = !ViewSlides ? view.X
              : place.X < view.X ? place.X
              : place.Right > view.Right ? place.Right - view.Width
              : view.X;

        this.ViewAt(x, y, view.Width, view.Height);
    }

    /// <summary>
    /// Brings <paramref name="place"/> onto the view as a place gone to is brought: left where it stands where it is on the screen already, and
    /// otherwise stood half way down it, so what comes before and after it is there to read it by — or, taller than the screen, its top at
    /// the top.
    /// </summary>
    internal void BringToView(LayoutRect place)
    {
        if (_showing is not { } view || place.IsEmpty) return;

        if (view.Contains(place) || (place.Height > view.Height && view.Y <= place.Y && place.Y < view.Bottom)) return;

        var y = place.Height < view.Height ? place.Y + (place.Height / 2) - (view.Height / 2) : place.Y;
        var x = !ViewSlides || (place.X >= view.X && place.Right <= view.Right) ? view.X : place.X - (view.Width / 4);

        this.ViewAt(x, y, view.Width, view.Height);
    }

    /// <summary>How far up and down the view's top can stand, for a view <paramref name="height"/> tall.</summary>
    private (double Top, double Bottom) Reach(double height) =>
        (-_inset.Top, Math.Max(-_inset.Top, (Preview?.Laid ?? Laid).Size.Height + _inset.Bottom - height));

    /// <summary>
    /// Stands the view at <paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/> by <paramref name="height"/>, kept inside the
    /// content — and returns whether that moved or sized it. Says so where it did, and where the page under it has come to another length or
    /// width since last said, which a scrollbar stands for as much as for where the view is.
    /// </summary>
    private bool ViewAt(double x, double y, double width, double height)
    {
        var (top, bottom) = this.Reach(height);
        var across = (Preview?.Laid ?? Laid).Size.Width + _inset.Right - width;

        var next = new LayoutRect(
            ViewSlides ? Math.Clamp(x, -_inset.Left, Math.Max(-_inset.Left, across)) : -_inset.Left,
            Math.Clamp(y, top, bottom),
            Math.Max(0, width),
            Math.Max(0, height));

        var (moved, extent) = (_showing != next, this.Extent);
        if (!moved && extent == _extentTold) return false;

        if (moved) OnScreen = next;
        _extentTold = extent;
        ViewMoved?.Invoke(this, EventArgs.Empty);

        return moved;
    }

    /// <summary>How far the view could scroll through when it was last said to have moved.</summary>
    private LayoutSize _extentTold;
}
