using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Images.ViewModels;
using SkiaSharp;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// The Paint and Move tools' hands on the edit canvas, over the editing chrome while either is out: the brush's outline at the
/// pointer and its strokes — a pen's pressure taken from each stylus point — the eyedropper's and the fill's clicks, a layer
/// dragged, and the transform's frame with its handles. Laid out at the canvas's size, as the chrome is; what it reports is in
/// canvas pixels.
/// </summary>
public sealed class LayerToolsOverlay : FrameworkElement
{
    private const double HandleSize = 9;
    private const double GrabReach = 8;
    private const double RotateReach = 26;

    private enum Drag
    {
        None,
        Stroke,
        Move,
        Handle,
    }

    private ImageViewModel? _viewer;
    private double _zoom = 1;
    private Point? _pointer;
    private Drag _drag;

    public LayerToolsOverlay()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <summary>The surface's zoom, so the chrome keeps one size on screen.</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Max(0.01, value); InvalidateVisual(); }
    }

    public void Attach(ImageViewModel viewer)
    {
        _viewer = viewer;
        viewer.PropertyChanged += OnChanged;
        viewer.Editor.PropertyChanged += OnChanged;
        viewer.Painting.PropertyChanged += OnChanged;
        viewer.Arranging.PropertyChanged += OnChanged;
        Follow();
    }

    public void Detach()
    {
        if (_viewer is null) return;
        Finish(cancelled: true);
        _viewer.PropertyChanged -= OnChanged;
        _viewer.Editor.PropertyChanged -= OnChanged;
        _viewer.Painting.PropertyChanged -= OnChanged;
        _viewer.Arranging.PropertyChanged -= OnChanged;
        _viewer = null;
        Follow();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        Follow();
        InvalidateVisual();
    }

    // Takes the pointer only while the Paint or Move tool is out; otherwise presses go to the chrome beneath.
    private void Follow()
    {
        bool active = _viewer is { IsEditing: true } viewer && (viewer.Editor.IsPaintTool || viewer.Editor.IsMoveTool);
        IsHitTestVisible = active;
        Cursor = !active ? null : _viewer!.Editor.IsMoveTool ? Cursors.SizeAll : _viewer.Painting.PaintsWithTip ? Cursors.None : Cursors.Cross;
    }

    private SKMatrix? ToCanvas => _viewer?.Canvas?.DocumentToCanvas;

    // A point on the layout to canvas pixels.
    private SKPoint? OnCanvas(Point at)
        => ToCanvas is { } toLayout && toLayout.TryInvert(out var toCanvas) ? toCanvas.MapPoint(new SKPoint((float)at.X, (float)at.Y)) : null;

    private Point OnLayout(SKPoint at) => ToCanvas is { } m ? Wpf(m.MapPoint(at)) : Wpf(at);

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    // ── Drawing ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsHitTestVisible || _viewer is not { } viewer) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // hit-testable everywhere
        double px = 1 / _zoom;

        if (viewer.Editor.IsPaintTool && viewer.Painting.PaintsWithTip && _pointer is { } pointer && ToCanvas is { } toLayout)
        {
            // The tip's outline at the pointer, light over dark so it shows on any picture.
            double radius = viewer.Painting.Size / 2 * Math.Sqrt(Math.Abs(toLayout.ScaleX * toLayout.ScaleY - toLayout.SkewX * toLayout.SkewY));
            radius = Math.Max(radius, 2 * px);
            dc.DrawEllipse(null, new Pen(Brush("BgBrush"), 3 * px), pointer, radius, radius);
            dc.DrawEllipse(null, new Pen(Brush("TextBrush"), px), pointer, radius, radius);
        }

        if (viewer.Editor.IsMoveTool && viewer.Arranging.Frame is { } frame)
        {
            var corners = frame.Corners.Select(OnLayout).ToArray();
            var accent = Brush("AccentBrush");
            var onAccent = Brush("OnAccentBrush");
            var outline = new StreamGeometry();
            using (var g = outline.Open())
            {
                g.BeginFigure(corners[0], false, true);
                g.PolyLineTo(corners[1..], true, false);
            }
            dc.DrawGeometry(null, new Pen(accent, 1.5 * px), outline);
            var topMiddle = Mid(corners[0], corners[1]);
            var rotate = RotateHandle(frame);
            dc.DrawLine(new Pen(accent, px), topMiddle, rotate);
            double h = HandleSize * px;
            var handlePen = new Pen(onAccent, px);
            foreach (var (_, at) in ScaleHandles(corners))
                dc.DrawRectangle(accent, handlePen, new Rect(at.X - h / 2, at.Y - h / 2, h, h));
            dc.DrawEllipse(accent, handlePen, rotate, h / 2, h / 2);
        }
    }

    private Point RotateHandle(ArrangeFrame frame)
    {
        var corners = frame.Corners.Select(OnLayout).ToArray();
        var top = Mid(corners[0], corners[1]);
        var centre = OnLayout(frame.Centre);
        var up = top - centre;
        double length = up.Length;
        var unit = length > 0 ? up / length : new Vector(0, -1);
        return top + unit * (RotateReach / _zoom);
    }

    private static IEnumerable<(ArrangeHandle Handle, Point At)> ScaleHandles(Point[] c)
    {
        yield return (ArrangeHandle.TopLeft, c[0]);
        yield return (ArrangeHandle.Top, Mid(c[0], c[1]));
        yield return (ArrangeHandle.TopRight, c[1]);
        yield return (ArrangeHandle.Right, Mid(c[1], c[2]));
        yield return (ArrangeHandle.BottomRight, c[2]);
        yield return (ArrangeHandle.Bottom, Mid(c[2], c[3]));
        yield return (ArrangeHandle.BottomLeft, c[3]);
        yield return (ArrangeHandle.Left, Mid(c[3], c[0]));
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    // What of the frame is under the pointer: a handle, the body, or nothing.
    private ArrangeHandle HandleAt(ArrangeFrame frame, Point at)
    {
        double reach = GrabReach / _zoom;
        if ((RotateHandle(frame) - at).Length <= reach) return ArrangeHandle.Rotate;
        var corners = frame.Corners.Select(OnLayout).ToArray();
        foreach (var (handle, point) in ScaleHandles(corners))
            if ((point - at).Length <= reach) return handle;
        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            g.BeginFigure(corners[0], true, true);
            g.PolyLineTo(corners[1..], false, false);
        }
        return outline.FillContains(at) ? ArrangeHandle.Body : ArrangeHandle.None;
    }

    // ── Pointer ─────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_viewer is not { } viewer || OnCanvas(e.GetPosition(this)) is not { } at) return;
        Keyboard.Focus(this);
        if (viewer.Editor.IsPaintTool)
        {
            var painting = viewer.Painting;
            bool picks = painting.IsEyedropper || painting.PaintsWithTip && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            if (picks) painting.PickAt(at);
            else if (painting.IsFill) painting.FillAt(at);
            else if (painting.BeginStroke(at, Pressure(e)))
            {
                _drag = Drag.Stroke;
                CaptureMouse();
            }
        }
        else if (viewer.Editor.IsMoveTool)
        {
            var arranging = viewer.Arranging;
            if (arranging.Frame is { } frame)
            {
                var handle = HandleAt(frame, e.GetPosition(this));
                if (handle == ArrangeHandle.None) return;
                arranging.BeginHandle(handle, at);
                _drag = Drag.Handle;
                CaptureMouse();
            }
            else if (arranging.BeginMove(at))
            {
                _drag = Drag.Move;
                CaptureMouse();
            }
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _pointer = e.GetPosition(this);
        if (_viewer is { } viewer && OnCanvas(_pointer.Value) is { } at)
        {
            switch (_drag)
            {
                case Drag.Stroke:
                    // Every stylus point since the last move, each with its pressure; one point for a mouse.
                    if (e.StylusDevice?.GetStylusPoints(this) is { Count: > 0 } points)
                    {
                        foreach (var point in points)
                            if (OnCanvas(point.ToPoint()) is { } along) viewer.Painting.ContinueStroke(along, point.PressureFactor);
                    }
                    else viewer.Painting.ContinueStroke(at, 1);
                    break;
                case Drag.Move:
                    viewer.Arranging.ContinueMove(at);
                    break;
                case Drag.Handle:
                    viewer.Arranging.DragHandle(at, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    break;
                default:
                    if (viewer.Editor.IsMoveTool && viewer.Arranging.Frame is { } frame) Cursor = CursorFor(HandleAt(frame, _pointer.Value));
                    break;
            }
        }
        InvalidateVisual();
    }

    private static Cursor CursorFor(ArrangeHandle handle) => handle switch
    {
        ArrangeHandle.TopLeft or ArrangeHandle.BottomRight => Cursors.SizeNWSE,
        ArrangeHandle.TopRight or ArrangeHandle.BottomLeft => Cursors.SizeNESW,
        ArrangeHandle.Top or ArrangeHandle.Bottom          => Cursors.SizeNS,
        ArrangeHandle.Left or ArrangeHandle.Right          => Cursors.SizeWE,
        ArrangeHandle.Rotate                               => Cursors.Hand,
        ArrangeHandle.Body                                 => Cursors.SizeAll,
        _                                                  => Cursors.Arrow,
    };

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Finish(cancelled: false);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => Finish(cancelled: false);

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_drag == Drag.None) _pointer = null;
        InvalidateVisual();
    }

    private void Finish(bool cancelled)
    {
        var drag = _drag;
        _drag = Drag.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_viewer is not { } viewer) return;
        switch (drag)
        {
            case Drag.Stroke when cancelled: viewer.Painting.CancelStroke(); break;
            case Drag.Stroke:                viewer.Painting.EndStroke(); break;
            case Drag.Move:                  viewer.Arranging.EndMove(); break;
            case Drag.Handle:                viewer.Arranging.EndHandle(); break;
        }
    }

    private float Pressure(MouseEventArgs e)
        => e.StylusDevice?.GetStylusPoints(this) is { Count: > 0 } points ? points[^1].PressureFactor : 1f;

    private static Point Wpf(SKPoint p) => new(p.X, p.Y);
}
