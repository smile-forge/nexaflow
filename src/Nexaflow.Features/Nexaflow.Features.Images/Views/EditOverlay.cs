using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.ViewModels;
using SkiaSharp;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Images.Editing.Selection;
using System.Windows.Media.Animation;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// The editing chrome over the picture — never part of what is saved: the crop frame with its handles, the outside
/// dimmed and a rule-of-thirds grid; a fine grid while straightening; the level line being drawn; the markup
/// selection; what is selected for Cut out and Erase, with marching ants round it, and the marquee or lasso being drawn.
/// It is also where the pointer is read, in the picture's layout coordinates, and handed to the editor: crop drags as they
/// are, markup, picks and drawn shapes mapped back to source pixels.
/// </summary>
/// <remarks>
/// It sits inside the pan/zoom surface beside the picture, so it scales with it; lines and handles are drawn at
/// <see cref="Zoom"/>'s inverse to keep one size on screen. A press it does not want (outside the frame, on nothing
/// selectable) is left unhandled, and the surface pans.
/// </remarks>
public sealed class EditOverlay : FrameworkElement
{
    private const double HandleSize = 9, GrabReach = 12, HitReach = 6, StrokeStep = 1.5, DimOpacity = 0.55, DragReach = 4, MarkerSize = 9, AntDash = 4;

    /// <summary>How strongly what the cut-out tool picks is tinted, of 255.</summary>
    private const byte TintAlpha = 110;

    private ImageViewModel? _viewer;
    private Gesture _gesture;
    private Point _levelFrom, _levelTo;
    private double _zoom = 1;
    private bool _alignmentGrid;
    private Point _cutFrom, _cutTo;
    private bool _cutDragging;
    private RegionSelection? _tintFor;
    private BitmapSource? _tint;

    private enum Gesture { None, Crop, Level, Markup, CutOut, Erase, Marquee, Lasso }

    // A marquee or freehand lasso being drawn: where it began, where the pointer is, the keys held as it began and the mode
    // they gave it; the freehand outline so far, in the photo's pixels.
    private Point _regionFrom, _regionTo;
    private ModifierKeys _regionHeld;
    private RegionMode _regionMode;
    private readonly List<MarkupPoint> _freehand = [];

    // The outline of what is selected, made once for each selection and place on the canvas.
    private (RegionSelection Shown, SKMatrix ToCanvas)? _outlineFor;
    private Geometry? _outline;

    // The ants' dashes, walking only while there is an outline to walk.
    private readonly DashStyle _ants = new([AntDash, AntDash], 0);
    private bool _marching;

    // Where the pointer is over the photo while the Erase tool brushes, for the brush's outline.
    private Point? _brushAt;

    // Where the pointer is while a polygon is being drawn, for the edge to it.
    private Point? _pointer;

    public EditOverlay()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>The surface's zoom, so chrome keeps one size on screen.</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Max(0.01, value); InvalidateVisual(); }
    }

    /// <summary>A fine grid over the crop, while the straighten slider is dragged.</summary>
    public bool ShowAlignmentGrid
    {
        get => _alignmentGrid;
        set { _alignmentGrid = value; InvalidateVisual(); }
    }

    public void Attach(ImageViewModel viewer)
    {
        _viewer = viewer;
        viewer.PropertyChanged += OnChanged;
        viewer.Editor.PropertyChanged += OnChanged;
        viewer.Cutter.PropertyChanged += OnChanged;
        viewer.Editor.EraseMarks.PropertyChanged += OnChanged;
        viewer.Editor.RegionMarks.PropertyChanged += OnChanged;
    }

    public void Detach()
    {
        if (_viewer is null) return;
        _viewer.PropertyChanged -= OnChanged;
        _viewer.Editor.PropertyChanged -= OnChanged;
        _viewer.Cutter.PropertyChanged -= OnChanged;
        _viewer.Editor.EraseMarks.PropertyChanged -= OnChanged;
        _viewer.Editor.RegionMarks.PropertyChanged -= OnChanged;
        March(false);
        _viewer = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private ImageEditorViewModel? Editor => _viewer?.IsEditing == true ? _viewer.Editor : null;

    // ── Drawing ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Hit-testable everywhere, so a press anywhere on the picture reaches the tools.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Editor is not { ShowOriginal: false } editor)
        {
            March(false);
            return;
        }

        if (editor.IsCropTool && editor.ShownCrop is { } crop)
            DrawCrop(dc, Wpf(crop), editor.IsLevelTool || _alignmentGrid);
        if (_gesture == Gesture.Level)
            DrawLevelLine(dc);
        if (editor.IsMarkupTool && editor.SelectedOutline is { } outline)
            DrawSelection(dc, outline);
        bool marching = editor.TakesSelection && DrawRegion(dc, editor);
        March(marching);
        if (editor.IsRetouchTool)
            DrawErase(dc, editor.EraseMarks);
    }

    private void DrawCrop(DrawingContext dc, Rect crop, bool alignment)
    {
        double px = 1 / _zoom;
        var accent = Brush("AccentBrush");
        var onAccent = Brush("OnAccentBrush");

        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(crop));
        dc.PushOpacity(DimOpacity);
        dc.DrawGeometry(Brush("ScrimBrush"), null, outside);
        dc.Pop();

        var guide = new Pen(onAccent, px) { DashStyle = DashStyles.Solid };
        dc.PushOpacity(0.6);
        int divisions = alignment ? 9 : 3;
        for (int i = 1; i < divisions; i++)
        {
            double x = crop.Left + crop.Width * i / divisions, y = crop.Top + crop.Height * i / divisions;
            dc.DrawLine(guide, new Point(x, crop.Top), new Point(x, crop.Bottom));
            dc.DrawLine(guide, new Point(crop.Left, y), new Point(crop.Right, y));
        }
        dc.Pop();

        dc.DrawRectangle(null, new Pen(accent, 1.5 * px), crop);

        double h = HandleSize * px;
        var handlePen = new Pen(onAccent, px);
        foreach (var p in HandlePoints(crop))
            dc.DrawRectangle(accent, handlePen, new Rect(p.X - h / 2, p.Y - h / 2, h, h));
    }

    private void DrawLevelLine(DrawingContext dc)
    {
        double px = 1 / _zoom;
        dc.DrawLine(new Pen(Brush("OnAccentBrush"), 3 * px), _levelFrom, _levelTo);
        dc.DrawLine(new Pen(Brush("AccentBrush"), 1.5 * px), _levelFrom, _levelTo);
    }

    private void DrawSelection(DrawingContext dc, SKPoint[] outline)
    {
        double px = 1 / _zoom;
        var figure = new PathFigure { StartPoint = Wpf(outline[0]), IsClosed = true };
        foreach (var p in outline.Skip(1)) figure.Segments.Add(new LineSegment(Wpf(p), true));
        var geometry = new PathGeometry([figure]);
        dc.DrawGeometry(null, new Pen(Brush("OnAccentBrush"), 3 * px), geometry);
        dc.DrawGeometry(null, new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = new DashStyle([4, 3], 0) }, geometry);
    }

    /// <summary>What is selected — the cut-out tool's picks and the region drawn over them — tinted over the photo where it
    /// lies, marching ants round it; the picks' clicks marked — on the thing in the accent, off it in the danger colour — and
    /// the box drawn round it, or being drawn; the marquee or lasso being drawn. Gives whether ants were drawn.</summary>
    private bool DrawRegion(DrawingContext dc, ImageEditorViewModel editor)
    {
        if (_viewer?.Canvas is not { } canvas || PhotoSize is not { } source) return false;
        var toCanvas = canvas.SourceToCanvas;
        double px = 1 / _zoom;
        var marks = editor.RegionMarks;
        var cutter = editor.Cutter;
        var halo = new Pen(Brush("OnAccentBrush"), 3 * px);
        var dashed = new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = new DashStyle([4, 3], 0) };

        bool marching = false;
        if (marks.Shown is { IsEmpty: false } shown)
        {
            dc.PushTransform(Transform(toCanvas));
            dc.DrawImage(Tint(shown), new Rect(0, 0, source.Width, source.Height));
            dc.Pop();
            var outline = Outline(shown, marks.ShownScale, toCanvas);
            dc.DrawGeometry(null, new Pen(Brush("OnAccentBrush"), 1.5 * px), outline);
            dc.DrawGeometry(null, new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = _ants }, outline);
            marching = true;
        }

        var outlinePen = new Pen(Brush("OnAccentBrush"), 1.5 * px);
        if (cutter.Box is { } box)
        {
            var corners = new[] { new SKPoint((float)box.Left, (float)box.Top), new SKPoint((float)box.Right, (float)box.Top),
                                  new SKPoint((float)box.Right, (float)box.Bottom), new SKPoint((float)box.Left, (float)box.Bottom) };
            var geometry = Polyline(corners.Select(c => Wpf(toCanvas.MapPoint(c))), closed: true);
            dc.DrawGeometry(null, halo, geometry);
            dc.DrawGeometry(null, dashed, geometry);
        }
        if (_gesture == Gesture.CutOut && _cutDragging)
        {
            var dragged = new Rect(_cutFrom, _cutTo);
            dc.DrawRectangle(null, halo, dragged);
            dc.DrawRectangle(null, dashed, dragged);
        }
        double r = MarkerSize * px / 2;
        foreach (var point in cutter.Points)
            dc.DrawEllipse(Brush(point.Include ? "AccentBrush" : "DangerBrush"), outlinePen, Wpf(toCanvas.MapPoint(new SKPoint((float)point.X, (float)point.Y))), r, r);

        // The shape being drawn, as it will be added.
        Geometry? drawing = null;
        if (_gesture == Gesture.Marquee)
        {
            var drawn = MarqueeRect();
            drawing = marks.Marquee == MarqueeShape.Ellipse ? new EllipseGeometry(drawn) : new RectangleGeometry(drawn);
        }
        else if (_gesture == Gesture.Lasso && _freehand.Count > 1)
            drawing = Polyline(_freehand.Select(p => Wpf(toCanvas.MapPoint(new SKPoint((float)p.X, (float)p.Y)))), closed: false);
        else if (marks.Draft.Count > 0 && editor.DrawsLasso)
        {
            var points = marks.Draft.Select(p => Wpf(toCanvas.MapPoint(new SKPoint((float)p.X, (float)p.Y)))).ToList();
            if (_pointer is { } pointer) points.Add(pointer);
            drawing = Polyline(points, closed: false);
            var first = points[0];
            double reach = GrabReach * px / 2;
            dc.DrawEllipse(Brush("AccentBrush"), outlinePen, first, reach, reach);
        }
        if (drawing is not null)
        {
            dc.DrawGeometry(null, halo, drawing);
            dc.DrawGeometry(null, dashed, drawing);
        }
        return marching;
    }

    /// <summary>The ants set walking, or stopped — an animation left running would keep the window drawing every frame.</summary>
    private void March(bool marching)
    {
        if (marching == _marching) return;
        _marching = marching;
        _ants.BeginAnimation(DashStyle.OffsetProperty, marching
            ? new DoubleAnimation(0, -2 * AntDash, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }

    /// <summary>The loops round what is selected, on the canvas: made once for each selection and place.</summary>
    private Geometry Outline(RegionSelection shown, double shownScale, SKMatrix toCanvas)
    {
        if (_outline is not null && _outlineFor is { } made && ReferenceEquals(made.Shown, shown) && made.ToCanvas == toCanvas) return _outline;
        var toPhoto = SKMatrix.CreateScale((float)(1 / shownScale), (float)(1 / shownScale));
        var shownToCanvas = toPhoto.PostConcat(toCanvas);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
            foreach (var loop in shown.Outline)
            {
                context.BeginFigure(Wpf(shownToCanvas.MapPoint(loop[0])), isFilled: false, isClosed: true);
                context.PolyLineTo([.. loop.Skip(1).Select(p => Wpf(shownToCanvas.MapPoint(p)))], isStroked: true, isSmoothJoin: false);
            }
        geometry.Freeze();
        (_outlineFor, _outline) = ((shown, toCanvas), geometry);
        return geometry;
    }

    private static Geometry Polyline(IEnumerable<Point> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            bool begun = false;
            foreach (var point in points)
            {
                if (!begun) context.BeginFigure(point, isFilled: false, isClosed: closed);
                else context.LineTo(point, isStroked: true, isSmoothJoin: false);
                begun = true;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static MatrixTransform Transform(SKMatrix m) => new(m.ScaleX, m.SkewY, m.SkewX, m.ScaleY, m.TransX, m.TransY);

    private SKSize? PhotoSize => _viewer?.CurrentSession?.Proxy?.SourceSize;

    /// <summary>The Erase tool's marks: the strokes, tinted where they mark and outlined where they take marking back; where the
    /// healing brush copies from, and where it copies from for the brush's place now; the brush's outline under the pointer.</summary>
    private void DrawErase(DrawingContext dc, EraseMarking marks)
    {
        if (_viewer?.Canvas is not { } canvas) return;
        var toCanvas = canvas.SourceToCanvas;
        double px = 1 / _zoom, sourceScale = Math.Sqrt(Math.Abs(toCanvas.ScaleX * toCanvas.ScaleY - toCanvas.SkewX * toCanvas.SkewY));

        dc.PushTransform(new MatrixTransform(toCanvas.ScaleX, toCanvas.SkewY, toCanvas.SkewX, toCanvas.ScaleY, toCanvas.TransX, toCanvas.TransY));
        var marked = Brush("DangerBrush").Clone();
        marked.Opacity = 0.45;
        foreach (var stroke in marks.Strokes)
        {
            if (stroke.Points.IsEmpty) continue;
            var pen = stroke.Marks
                ? new Pen(marked, stroke.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }
                : new Pen(Brush("OnAccentBrush"), stroke.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (!stroke.Marks) pen.Brush = new SolidColorBrush(Colors.Transparent);
            var figure = new PathFigure { StartPoint = new Point(stroke.Points[0].X, stroke.Points[0].Y) };
            foreach (var point in stroke.Points.Skip(1)) figure.Segments.Add(new LineSegment(new Point(point.X, point.Y), true));
            if (stroke.Points.Length == 1) figure.Segments.Add(new LineSegment(new Point(stroke.Points[0].X, stroke.Points[0].Y), true));
            var path = new PathGeometry([figure]);
            dc.DrawGeometry(null, pen, path);
            if (!stroke.Marks)
                dc.DrawGeometry(null, new Pen(Brush("OnAccentBrush"), 1.5 * px / sourceScale) { DashStyle = new DashStyle([4, 3], 0) },
                                path.GetWidenedPathGeometry(new Pen(Brushes.Black, stroke.Width)));
        }
        dc.Pop();

        var accent = new Pen(Brush("AccentBrush"), 1.5 * px);
        var halo = new Pen(Brush("OnAccentBrush"), 3 * px);
        if (marks.HealSource is { } source)
        {
            var at = Wpf(toCanvas.MapPoint(new SKPoint((float)source.X, (float)source.Y)));
            double arm = MarkerSize * px;
            foreach (var pen in new[] { halo, accent })
            {
                dc.DrawLine(pen, new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
                dc.DrawLine(pen, new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
            }
        }
        if (marks.IsBrushing && _brushAt is { } pointer && toCanvas.TryInvert(out var toSource))
        {
            double radius = marks.BrushSize / 2 * sourceScale;
            dc.DrawEllipse(null, halo, pointer, radius, radius);
            dc.DrawEllipse(null, accent, pointer, radius, radius);
            // Where the healing brush copies from for the brush's place now.
            if (marks.HealOffset is { } offset)
            {
                var under = toSource.MapPoint(Sk(pointer));
                var copied = Wpf(toCanvas.MapPoint(new SKPoint(under.X + (float)offset.X, under.Y + (float)offset.Y)));
                dc.DrawEllipse(null, new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = new DashStyle([3, 3], 0) }, copied, radius, radius);
            }
        }
    }

    /// <summary>What is selected as a tint of the accent colour, as strong as each pixel is selected, made once for each selection.</summary>
    private BitmapSource Tint(RegionSelection shown)
    {
        if (ReferenceEquals(shown, _tintFor) && _tint is not null) return _tint;
        var colour = (Brush("AccentBrush") as SolidColorBrush)?.Color ?? Colors.Transparent;
        var coverage = shown.Coverage.ToBytes();
        var pixels = new byte[shown.Width * shown.Height * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            int alpha = TintAlpha * coverage[i] / 255;
            if (alpha == 0) continue;
            pixels[i * 4] = (byte)(colour.B * alpha / 255);
            pixels[i * 4 + 1] = (byte)(colour.G * alpha / 255);
            pixels[i * 4 + 2] = (byte)(colour.R * alpha / 255);
            pixels[i * 4 + 3] = (byte)alpha;
        }
        var tint = BitmapSource.Create(shown.Width, shown.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, shown.Width * 4);
        tint.Freeze();
        (_tintFor, _tint) = (shown, tint);
        return tint;
    }

    private static IEnumerable<Point> HandlePoints(Rect r)
    {
        double mx = r.Left + r.Width / 2, my = r.Top + r.Height / 2;
        yield return r.TopLeft;   yield return new Point(mx, r.Top);    yield return r.TopRight;
        yield return new Point(r.Right, my);                           yield return r.BottomRight;
        yield return new Point(mx, r.Bottom); yield return r.BottomLeft; yield return new Point(r.Left, my);
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    // ── Pointer ─────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Editor is not { ShowOriginal: false } editor || _viewer?.Canvas is not { } canvas) return;
        var at = e.GetPosition(this);
        var p = Sk(at);

        if (editor.IsCropTool)
        {
            if (editor.IsLevelTool)
            {
                _levelFrom = _levelTo = at;
                Begin(Gesture.Level, e);
                return;
            }
            var handle = editor.CropHandleAt(p, (float)(GrabReach / _zoom));
            if (handle == CropHandle.None) return;
            editor.BeginCropDrag(handle, p);
            Begin(Gesture.Crop, e);
        }
        else if (editor.IsMarkupTool && canvas.SourceToCanvas.TryInvert(out var toSource))
        {
            float reach = (float)(HitReach / _zoom * CropGeometry.SourcePixelsPerCanvasPixel(canvas.SourceToCanvas));
            editor.MarkupPress(toSource.MapPoint(p), reach);
            if (editor.MarkupTool == MarkupTool.Select && !editor.HasSelection) return;   // nothing hit: pan
            Begin(Gesture.Markup, e);
        }
        else if (editor.DrawsMarquee)
        {
            _regionFrom = _regionTo = at;
            _regionHeld = Keyboard.Modifiers;
            _regionMode = ModeHeld(Keyboard.Modifiers, editor.RegionMarks.Mode);
            Begin(Gesture.Marquee, e);
        }
        else if (editor.DrawsLasso && canvas.SourceToCanvas.TryInvert(out var lassoToSource))
        {
            var under = lassoToSource.MapPoint(p);
            var marks = editor.RegionMarks;
            if (marks.Lasso == LassoShape.Freehand)
            {
                _freehand.Clear();
                _freehand.Add(new MarkupPoint(under.X, under.Y));
                _regionMode = ModeHeld(Keyboard.Modifiers, marks.Mode);
                Begin(Gesture.Lasso, e);
                return;
            }
            // Click to click: the first click begins it, one on its first point or a double-click closes it.
            if (marks.Draft.Count > 0 && (e.ClickCount >= 2 || NearFirstDraftPoint(marks, at, canvas.SourceToCanvas))) marks.CloseDraft();
            else marks.AddDraftPoint(new MarkupPoint(under.X, under.Y), ModeHeld(Keyboard.Modifiers, marks.Mode));
            e.Handled = true;
        }
        else if (editor.PicksByModel)
        {
            if (!editor.PickingModels.IsReady) return;   // nothing picks until picture selection is here: the press pans
            _cutFrom = _cutTo = at;
            _cutDragging = false;
            Begin(Gesture.CutOut, e);
        }
        else if (editor.IsRetouchTool && editor.EraseMarks.IsBrushing && canvas.SourceToCanvas.TryInvert(out var brushToSource))
        {
            var under = brushToSource.MapPoint(p);
            if (OnPhoto(under) is not { } onPhoto) return;
            // Alt-click with the healing brush: where it copies from.
            if (editor.EraseMarks.IsHealing && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                editor.EraseMarks.SetHealSource(new MarkupPoint(onPhoto.X, onPhoto.Y));
                e.Handled = true;
                return;
            }
            editor.EraseMarks.BeginStroke(new MarkupPoint(onPhoto.X, onPhoto.Y), marks: true);
            Begin(Gesture.Erase, e);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Editor is not { } editor) return;
        var at = e.GetPosition(this);

        switch (_gesture)
        {
            case Gesture.Crop:
                editor.ContinueCropDrag(Sk(at));
                break;
            case Gesture.Level:
                _levelTo = at;
                InvalidateVisual();
                break;
            case Gesture.CutOut:
                _cutTo = at;
                _cutDragging |= (at - _cutFrom).Length * _zoom > DragReach;
                if (_cutDragging) InvalidateVisual();
                break;
            case Gesture.Marquee:
                _regionTo = at;
                InvalidateVisual();
                break;
            case Gesture.Lasso when _viewer?.Canvas is { } canvas && canvas.SourceToCanvas.TryInvert(out var toSource):
                var traced = toSource.MapPoint(Sk(at));
                double reach = StrokeStep / _zoom * CropGeometry.SourcePixelsPerCanvasPixel(canvas.SourceToCanvas);
                var last = _freehand[^1];
                if (Math.Abs(traced.X - last.X) < reach && Math.Abs(traced.Y - last.Y) < reach) break;
                _freehand.Add(new MarkupPoint(traced.X, traced.Y));
                InvalidateVisual();
                break;
            case Gesture.Markup when _viewer?.Canvas is { } canvas && canvas.SourceToCanvas.TryInvert(out var toSource):
                float step = (float)(StrokeStep / _zoom * CropGeometry.SourcePixelsPerCanvasPixel(canvas.SourceToCanvas));
                editor.MarkupDrag(toSource.MapPoint(Sk(at)), step);
                break;
            case Gesture.Erase when _viewer?.Canvas is { } canvas && canvas.SourceToCanvas.TryInvert(out var toSource):
                var under = toSource.MapPoint(Sk(at));
                editor.EraseMarks.ContinueStroke(new MarkupPoint(under.X, under.Y), StrokeStep / _zoom * CropGeometry.SourcePixelsPerCanvasPixel(canvas.SourceToCanvas));
                _brushAt = at;
                break;
            default:
                Cursor = HoverCursor(editor, Sk(at));
                if (editor.DrawsLasso && editor.RegionMarks.Draft.Count > 0)
                {
                    _pointer = at;
                    InvalidateVisual();
                }
                if (editor.IsRetouchTool && editor.EraseMarks.IsBrushing)
                {
                    _brushAt = at;
                    InvalidateVisual();
                }
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_gesture == Gesture.None) return;
        Finish(cancelled: false);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_gesture != Gesture.Erase) return;
        Finish(cancelled: false);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_brushAt is null && _pointer is null) return;
        _brushAt = null;
        _pointer = null;
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture != Gesture.None) Finish(cancelled: true);
    }

    private void Begin(Gesture gesture, MouseButtonEventArgs e)
    {
        _gesture = gesture;
        CaptureMouse();
        e.Handled = true;
    }

    private void Finish(bool cancelled)
    {
        var gesture = _gesture;
        _gesture = Gesture.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (Editor is not { } editor) return;

        switch (gesture)
        {
            case Gesture.Crop:   editor.EndCropDrag(); break;
        case Gesture.Markup: editor.MarkupRelease(); break;
            case Gesture.CutOut:
                if (!cancelled) CutOutReleased(editor.Cutter);
                InvalidateVisual();
                break;
            case Gesture.Level:
                if (!cancelled) editor.Level(Sk(_levelFrom), Sk(_levelTo));
                InvalidateVisual();
                break;
            case Gesture.Erase:
                editor.EraseMarks.EndStroke();
                break;
            case Gesture.Marquee:
                if (!cancelled) MarqueeReleased(editor);
                InvalidateVisual();
                break;
            case Gesture.Lasso:
                if (!cancelled && _freehand.Count >= 3) editor.RegionMarks.Add(new RegionPolygon { Points = [.. _freehand], Mode = _regionMode });
                _freehand.Clear();
                InvalidateVisual();
                break;
        }
    }

    /// <summary>A press and release in the cut-out tool: a box where it was dragged, else a click on the thing — off it with
    /// Alt held.</summary>
    private void CutOutReleased(PictureCutter cutter)
    {
        if (_viewer?.Canvas is not { } canvas || !canvas.SourceToCanvas.TryInvert(out var toSource)) return;
        if (_cutDragging)
        {
            var corners = new[] { Sk(_cutFrom), Sk(new Point(_cutTo.X, _cutFrom.Y)), Sk(_cutTo), Sk(new Point(_cutFrom.X, _cutTo.Y)) }
                .Select(toSource.MapPoint).ToList();
            cutter.Boxed(new PictureBox(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y)));
            return;
        }
        if (OnPhoto(toSource.MapPoint(Sk(_cutFrom))) is { } at)
            cutter.Click(at.X, at.Y, include: (Keyboard.Modifiers & ModifierKeys.Alt) == 0);
    }

    /// <summary>A marquee let go: the shape dragged out added in the mode it began in — a click that dragged nothing, in the
    /// mode that replaces, selects nothing.</summary>
    private void MarqueeReleased(ImageEditorViewModel editor)
    {
        if (_viewer?.Canvas is not { } canvas || !canvas.SourceToCanvas.TryInvert(out var toSource)) return;
        if ((_regionTo - _regionFrom).Length * _zoom <= DragReach)
        {
            if (_regionMode == RegionMode.Replace) editor.RegionMarks.DeselectCommand.Execute(null);
            return;
        }
        editor.RegionMarks.Add(MarqueeIn(MarqueeRect(), editor.RegionMarks.Marquee, _regionMode, canvas.SourceToCanvas, toSource));
    }

    /// <summary>The marquee as dragged: from where it began to the pointer — square with Shift, and out from where it began
    /// with Alt, either pressed once it had begun (held as it began, they gave its mode).</summary>
    private Rect MarqueeRect()
    {
        var added = Keyboard.Modifiers & ~_regionHeld;
        double dx = _regionTo.X - _regionFrom.X, dy = _regionTo.Y - _regionFrom.Y;
        if ((added & ModifierKeys.Shift) != 0)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            (dx, dy) = (dx < 0 ? -side : side, dy < 0 ? -side : side);
        }
        return (added & ModifierKeys.Alt) != 0
            ? new Rect(new Point(_regionFrom.X - dx, _regionFrom.Y - dy), new Point(_regionFrom.X + dx, _regionFrom.Y + dy))
            : new Rect(_regionFrom, new Point(_regionFrom.X + dx, _regionFrom.Y + dy));
    }

    /// <summary>
    /// A marquee dragged out on the canvas as a shape in the photo's pixels: a rectangle or an ellipse where the canvas shows
    /// the photo square to its pixels — turned by quarters or flipped at most — else, straightened, the shape as it was drawn
    /// as an outline, an ellipse's close enough that no edge strays a quarter of a pixel.
    /// </summary>
    private static RegionShape MarqueeIn(Rect drawn, MarqueeShape shape, RegionMode mode, SKMatrix toCanvas, SKMatrix toSource)
    {
        if (SquareTo(toSource))
        {
            SKPoint a = toSource.MapPoint(Sk(drawn.TopLeft)), b = toSource.MapPoint(Sk(drawn.BottomRight));
            double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y), right = Math.Max(a.X, b.X), bottom = Math.Max(a.Y, b.Y);
            return shape == MarqueeShape.Ellipse
                ? new RegionEllipse(left, top, right, bottom) { Mode = mode }
                : new RegionRectangle(left, top, right, bottom) { Mode = mode };
        }
        IEnumerable<Point> outline;
        if (shape == MarqueeShape.Rectangle) outline = [drawn.TopLeft, drawn.TopRight, drawn.BottomRight, drawn.BottomLeft];
        else
        {
            double radius = Math.Max(drawn.Width, drawn.Height) / 2 * CropGeometry.SourcePixelsPerCanvasPixel(toCanvas);
            int sides = radius < 1 ? 16 : Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(1 - 0.25 / radius)), 16, 4096);
            double cx = drawn.Left + drawn.Width / 2, cy = drawn.Top + drawn.Height / 2;
            outline = Enumerable.Range(0, sides).Select(i => new Point(cx + drawn.Width / 2 * Math.Cos(2 * Math.PI * i / sides),
                                                                      cy + drawn.Height / 2 * Math.Sin(2 * Math.PI * i / sides)));
        }
        return new RegionPolygon { Points = [.. outline.Select(p => toSource.MapPoint(Sk(p))).Select(p => new MarkupPoint(p.X, p.Y))], Mode = mode };
    }

    /// <summary>Whether <paramref name="m"/> keeps a rectangle a rectangle square to the axes: no turn but by quarters.</summary>
    private static bool SquareTo(SKMatrix m)
    {
        double size = Math.Max(Math.Max(Math.Abs(m.ScaleX), Math.Abs(m.ScaleY)), Math.Max(Math.Abs(m.SkewX), Math.Abs(m.SkewY)));
        double slack = size * 1e-9;
        return Math.Abs(m.SkewX) <= slack && Math.Abs(m.SkewY) <= slack || Math.Abs(m.ScaleX) <= slack && Math.Abs(m.ScaleY) <= slack;
    }

    /// <summary>How a shape begun with <paramref name="held"/> meets the selection: Shift adds, Alt subtracts, both intersect,
    /// neither as the tool is set.</summary>
    private static RegionMode ModeHeld(ModifierKeys held, RegionMode chosen) => (held & (ModifierKeys.Shift | ModifierKeys.Alt)) switch
    {
        ModifierKeys.Shift | ModifierKeys.Alt => RegionMode.Intersect,
        ModifierKeys.Shift => RegionMode.Add,
        ModifierKeys.Alt => RegionMode.Subtract,
        _ => chosen,
    };

    /// <summary>Whether <paramref name="at"/> is on the first point of the outline being drawn, as it shows.</summary>
    private bool NearFirstDraftPoint(RegionMarking marks, Point at, SKMatrix toCanvas)
    {
        var first = marks.Draft[0];
        var shown = Wpf(toCanvas.MapPoint(new SKPoint((float)first.X, (float)first.Y)));
        return (shown - at).Length * _zoom <= GrabReach;
    }

    /// <summary>A right-click while picking — in the cut-out tool, or the Erase tool picking — is a click off the thing; a
    /// right-drag while the Erase tool brushes takes marking back.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Editor is not { ShowOriginal: false } editor || !(editor.IsCutOutTool || editor.IsRetouchTool) || _viewer?.Canvas is not { } canvas
            || !canvas.SourceToCanvas.TryInvert(out var toSource)) return;
        // Drawing a polygon, a right-click takes its last point back.
        if (editor.DrawsMarquee || editor.DrawsLasso)
        {
            if (editor.DrawsLasso && editor.RegionMarks.RemoveDraftPoint()) e.Handled = true;
            return;
        }
        if (OnPhoto(toSource.MapPoint(Sk(e.GetPosition(this)))) is not { } at) return;
        if (editor.IsRetouchTool && editor.EraseMarks.IsBrushing)
        {
            editor.EraseMarks.BeginStroke(new MarkupPoint(at.X, at.Y), marks: false);
            Begin(Gesture.Erase, e);
            return;
        }
        if (!editor.PicksByModel || !editor.PickingModels.IsReady) return;
        editor.Cutter.Click(at.X, at.Y, include: false);
        e.Handled = true;
    }

    /// <summary>A point of the photo, in its pixels; null where it falls off the photo.</summary>
    private SKPoint? OnPhoto(SKPoint source)
        => _viewer?.CurrentSession?.Proxy?.SourceSize is { } size && source.X >= 0 && source.Y >= 0 && source.X < size.Width && source.Y < size.Height
            ? source : null;

    private Cursor HoverCursor(ImageEditorViewModel editor, SKPoint at)
    {
        if (editor.IsCropTool)
        {
            if (editor.IsLevelTool) return Cursors.Cross;
            return editor.CropHandleAt(at, (float)(GrabReach / _zoom)) switch
            {
                CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
                CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
                CropHandle.Left or CropHandle.Right          => Cursors.SizeWE,
                CropHandle.Top or CropHandle.Bottom          => Cursors.SizeNS,
                CropHandle.Move                              => Cursors.SizeAll,
                _                                            => Cursors.Arrow,
            };
        }
        if (editor.DrawsMarquee || editor.DrawsLasso) return Cursors.Cross;
        if (editor.IsCutOutTool) return editor.PickingModels.IsReady ? Cursors.Cross : Cursors.SizeAll;
        if (editor.IsRetouchTool) return Cursors.Cross;
        if (editor.IsMarkupTool)
            return editor.MarkupTool switch
            {
                MarkupTool.Select => Cursors.Arrow,
                MarkupTool.Text   => Cursors.IBeam,
                _                 => Cursors.Cross,
            };
        return Cursors.SizeAll;
    }

    private static SKPoint Sk(Point p) => new((float)p.X, (float)p.Y);
    private static Point Wpf(SKPoint p) => new(p.X, p.Y);
    private static Rect Wpf(SKRect r) => new(r.Left, r.Top, r.Width, r.Height);
}
