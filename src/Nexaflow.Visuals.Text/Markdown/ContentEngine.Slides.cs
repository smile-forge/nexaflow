using System;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Boxes slid sideways: lines kept whole in a box narrower than they are, slid along to show the rest (<see cref="LayoutSlide"/>).
///
/// <para>
/// <strong>How far a box is slid is the reader's, so it is the engine's</strong>, as what the reader has folded away is: kept against
/// the reading of the body the box holds (<see cref="ContentBody.Continuity"/>), which is the same body through every edit, and handed to
/// the builder laying it (<see cref="LayoutHost.SlideOf"/>, <see cref="LayoutHost.Slid"/>), which lays it slid and cut to its box. So
/// pressing, the caret, picking out and finding all work from the tree as laid, and need to know nothing about sliding.
/// </para>
/// <para>
/// What slides it: a sideways turn of the wheel over it (<see cref="ContentSlide"/>), holding its bar's thumb and dragging, pressing the
/// bar either side of the thumb, and a place being brought onto the page — the caret as it moves, a match gone to — that the box has slid
/// out of view (<see cref="SlideOnto"/>). Content laid for paper slides nothing: there a box's lines wrap, so all of them show.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How far along the reader has slid each box, by the reading of the body it holds; a box nobody slid is not here.</summary>
    private readonly ConditionalWeakTable<object, StrongBox<double>> _slid = [];

    /// <summary>Moved on every slide, so a unit laid while any box stood elsewhere is laid again where it is next wanted.</summary>
    private int _slidVersion;

    /// <summary>The thumb being dragged: what it slides, where the drag began across the page, and how far along the box was then.</summary>
    private (LayoutSlide Slide, double From, double At)? _sliding;

    /// <summary>How far past the edge of what shows a place being brought into view is put, so what comes after it shows too.</summary>
    private const double SlideMargin = 24;

    /// <summary>
    /// Whether a box narrower than the lines it keeps whole slides sideways to show them, as on a screen; where not — content laid for
    /// paper, or for a picture of it — its lines wrap inside it instead, so all of them show.
    /// </summary>
    internal bool Slides
    {
        get => _slides;
        set
        {
            if (_slides == value) return;

            _slides = value;
            Forget();
        }
    }

    private bool _slides = true;

    /// <summary>
    /// What the box the holder <paramref name="holder"/> sets its body in is known by, for sliding it, in the reading <paramref name="holding"/>
    /// — the reading of its body, which is the same through every edit — or null where nothing slides, or no reading of the body is kept.
    /// </summary>
    private object? SlideOf(Keeping holding, ContentPart holder) =>
        _slides && holding.Held(holding.At(holder.Start)) is { } body ? body.Continuity : null;

    /// <summary>How far along the reader has slid the box known by <paramref name="reader"/>.</summary>
    private double Slid(object reader) => _slid.TryGetValue(reader, out var at) ? at.Value : 0;

    /// <summary>Whether a line too long for its box wraps, in <paramref name="language"/> held inside other content.</summary>
    private bool WrapsInside(ContentLanguage language) => language.WrapsLines || (language.BoxedAsWritten && !_slides);

    /// <summary>A sideways turn of the wheel at <paramref name="at"/>: the box under it slid <paramref name="by"/> along. False where nothing there slides.</summary>
    private bool Slide(LayoutPoint at, double by)
    {
        if (_laid.Root.SlideAt(at) is not { Slide: var slide }) return false;

        SlideTo(slide, slide.At + by);
        return true;
    }

    /// <summary>
    /// A press on a box's bar: holding the thumb, which a drag then slides, or either side of it, which slides the box a boxful that way.
    /// False where the press is not on a bar.
    /// </summary>
    private bool PressedBar(LayoutPoint at)
    {
        if (_laid.Root.SlideAt(at) is not { Slide: var slide, Within: var within } || !slide.Bar.Contains(within)) return false;

        if (slide.Thumb.Contains(within)) _sliding = (slide, at.X, slide.At);
        else SlideTo(slide, slide.At + (within.X < slide.Thumb.X ? -slide.Shown : slide.Shown));

        return true;
    }

    /// <summary>A drag holding a box's thumb, to <paramref name="at"/>: the box slid as far as the thumb has moved. False where no thumb is held.</summary>
    private bool DraggedThumb(LayoutPoint at)
    {
        if (_sliding is not { Slide: var slide, From: var from, At: var was }) return false;

        // The thumb runs along what of the bar it does not cover, and stands for the whole of how far the box slides.
        var runs = slide.Bar.Width - slide.Thumb.Width;
        if (runs > 0) SlideTo(slide, was + ((at.X - from) * slide.Furthest / runs));

        return true;
    }

    /// <summary>Lets go of a box's thumb. False where none was held.</summary>
    private bool ReleasedThumb()
    {
        if (_sliding is null) return false;

        _sliding = null;
        return true;
    }

    /// <summary>
    /// Slides whatever box has slid <paramref name="offset"/> out of view until the place shows, a little past it so what comes after shows
    /// too, and lays the content again. Whether anything slid — what whoever brings the place onto the page asks first, since where it
    /// stands on the page is where it stands once slid.
    /// </summary>
    internal bool SlideOnto(int offset)
    {
        if (!_slides || _laid.Root.SlideHolding(offset) is not { Slide: var slide, Across: var across }) return false;

        var window = slide.Window;
        var to = across < window.Left ? slide.At - (window.Left - across) - SlideMargin
               : across > window.Right ? slide.At + (across - window.Right) + SlideMargin
               : slide.At;

        return SlideTo(slide, to);
    }

    /// <summary>The box <paramref name="slide"/> says slid to <paramref name="to"/> along, as far as it goes, and laid again. Whether it moved.</summary>
    private bool SlideTo(LayoutSlide slide, double to)
    {
        var along = Math.Clamp(to, 0, slide.Furthest);
        if (along == slide.At) return false;

        _slid.AddOrUpdate(slide.Reader, new StrongBox<double>(along));
        _slidVersion++;

        Relay();
        return true;
    }
}
