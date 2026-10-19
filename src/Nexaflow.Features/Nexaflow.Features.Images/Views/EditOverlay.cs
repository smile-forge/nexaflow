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

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// The editing chrome over the picture — never part of what is saved: the crop frame with its handles, the outside
/// dimmed and a rule-of-thirds grid; a fine grid while straightening; the level line being drawn; the markup
/// selection. It is also where the pointer is read, in the picture's layout coordinates, and handed to the editor:
/// crop drags as they are, markup mapped back to source pixels.
/// </summary>
/// <remarks>
/// It sits inside the pan/zoom surface beside the picture, so it scales with it; lines and handles are drawn at
/// <see cref="Zoom"/>'s inverse to keep one size on screen. A press it does not want (outside the frame, on nothing
/// selectable) is left unhandled, and the surface pans.
/// </remarks>
public sealed class EditOverlay : FrameworkElement
{
    private const double HandleSize = 9, GrabReach = 12, HitReach = 6, StrokeStep = 1.5, DimOpacity = 0.55, DragReach = 4, MarkerSize = 9;

    /// <summary>How strongly what the cut-out tool picks is tinted, of 255.</summary>
    private const byte TintAlpha = 110;

    private ImageViewModel? _viewer;
    private Gesture _gesture;
    private Point _levelFrom, _levelTo;
    private double _zoom = 1;
    private bool _alignmentGrid;
    private Point _cutFrom, _cutTo;
    private bool _cutDragging;
    private PictureMask? _tintFor;
    private BitmapSource? _tint;

    private enum Gesture { None, Crop, Level, Markup, CutOut, Erase }

    // Where the pointer is over the photo while the Erase tool brushes, for the brush's outline.
    private Point? _brushAt;

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
    }

    public void Detach()
    {
        if (_viewer is null) return;
        _viewer.PropertyChanged -= OnChanged;
        _viewer.Editor.PropertyChanged -= OnChanged;
        _viewer.Cutter.PropertyChanged -= OnChanged;
        _viewer.Editor.EraseMarks.PropertyChanged -= OnChanged;
        _viewer = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private ImageEditorViewModel? Editor => _viewer?.IsEditing == true ? _viewer.Editor : null;

    // ── Drawing ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Hit-testable everywhere, so a press anywhere on the picture reaches the tools.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Editor is not { ShowOriginal: false } editor) return;

        if (editor.IsCropTool && editor.ShownCrop is { } crop)
            DrawCrop(dc, Wpf(crop), editor.IsLevelTool || _alignmentGrid);
        if (_gesture == Gesture.Level)
            DrawLevelLine(dc);
        if (editor.IsMarkupTool && editor.SelectedOutline is { } outline)
            DrawSelection(dc, outline);
        if (editor.IsCutOutTool || editor.IsRetouchTool)
            DrawCutOut(dc, editor.Cutter);
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

    /// <summary>What the cut-out tool picks, tinted over the photo where it lies, its clicks marked — on it in the accent,
    /// off it in the danger colour — and the box drawn round it, or being drawn.</summary>
    private void DrawCutOut(DrawingContext dc, PictureCutter cutter)
    {
        if (_viewer?.Canvas is not { } canvas || _viewer.CurrentSession?.Proxy?.SourceSize is not { } source) return;
        var toCanvas = canvas.SourceToCanvas;
        double px = 1 / _zoom;

        if (cutter.Shown is { Bounds: not null } mask)
        {
            dc.PushTransform(new MatrixTransform(toCanvas.ScaleX, toCanvas.SkewY, toCanvas.SkewX, toCanvas.ScaleY, toCanvas.TransX, toCanvas.TransY));
            dc.DrawImage(Tint(mask), new Rect(0, 0, source.Width, source.Height));
            dc.Pop();
        }

        var outline = new Pen(Brush("OnAccentBrush"), 1.5 * px);
        if (cutter.Box is { } box)
        {
            var corners = new[] { new SKPoint((float)box.Left, (float)box.Top), new SKPoint((float)box.Right, (float)box.Top),
                                  new SKPoint((float)box.Right, (float)box.Bottom), new SKPoint((float)box.Left, (float)box.Bottom) };
            var figure = new PathFigure { StartPoint = Wpf(toCanvas.MapPoint(corners[0])), IsClosed = true };
            foreach (var corner in corners.Skip(1)) figure.Segments.Add(new LineSegment(Wpf(toCanvas.MapPoint(corner)), true));
            var geometry = new PathGeometry([figure]);
            dc.DrawGeometry(null, new Pen(Brush("OnAccentBrush"), 3 * px), geometry);
            dc.DrawGeometry(null, new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = new DashStyle([4, 3], 0) }, geometry);
        }
        if (_gesture == Gesture.CutOut && _cutDragging)
        {
            var dragged = new Rect(_cutFrom, _cutTo);
            dc.DrawRectangle(null, new Pen(Brush("OnAccentBrush"), 3 * px), dragged);
            dc.DrawRectangle(null, new Pen(Brush("AccentBrush"), 1.5 * px) { DashStyle = new DashStyle([4, 3], 0) }, dragged);
        }

        double r = MarkerSize * px / 2;
        foreach (var point in cutter.Points)
            dc.DrawEllipse(Brush(point.Include ? "AccentBrush" : "DangerBrush"), outline, Wpf(toCanvas.MapPoint(new SKPoint((float)point.X, (float)point.Y))), r, r);
    }

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

    /// <summary>A mask as a tint of the accent colour, made once for each mask shown.</summary>
    private BitmapSource Tint(PictureMask mask)
    {
        if (ReferenceEquals(mask, _tintFor) && _tint is not null) return _tint;
        var colour = (Brush("AccentBrush") as SolidColorBrush)?.Color ?? Colors.Transparent;
        byte b = (byte)(colour.B * TintAlpha / 255), g = (byte)(colour.G * TintAlpha / 255), r = (byte)(colour.R * TintAlpha / 255);
        var pixels = new byte[mask.Width * mask.Height * 4];
        for (int i = 0; i < mask.Pixels.Length; i++)
        {
            if (mask.Pixels[i] == 0) continue;
            pixels[i * 4] = b;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = r;
            pixels[i * 4 + 3] = TintAlpha;
        }
        var tint = BitmapSource.Create(mask.Width, mask.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, mask.Width * 4);
        tint.Freeze();
        (_tintFor, _tint) = (mask, tint);
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
        else if (editor.IsCutOutTool || editor.IsRetouchTool && editor.EraseMarks.IsPicking)
        {
            if (!editor.PickingModels.IsReady) return;   // nothing picks until picture selection is here: the press pans
            _cutFrom = _cutTo = at;
            _cutDragging = false;
            Begin(Gesture.CutOut, e);
        }
        else if (editor.IsRetouchTool && canvas.SourceToCanvas.TryInvert(out var brushToSource))
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
        if (_brushAt is null) return;
        _brushAt = null;
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

    /// <summary>A right-click while picking — in the cut-out tool, or the Erase tool picking — is a click off the thing; a
    /// right-drag while the Erase tool brushes takes marking back.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Editor is not { ShowOriginal: false } editor || !(editor.IsCutOutTool || editor.IsRetouchTool) || _viewer?.Canvas is not { } canvas
            || !canvas.SourceToCanvas.TryInvert(out var toSource)) return;
        if (OnPhoto(toSource.MapPoint(Sk(e.GetPosition(this)))) is not { } at) return;
        if (editor.IsRetouchTool && editor.EraseMarks.IsBrushing)
        {
            editor.EraseMarks.BeginStroke(new MarkupPoint(at.X, at.Y), marks: false);
            Begin(Gesture.Erase, e);
            return;
        }
        if (!editor.PickingModels.IsReady) return;
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
