using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>Where a transform's frame is taken hold of: its body to move it, a side or corner to scale it, or its handle to turn it.</summary>
public enum ArrangeHandle
{
    None,
    Body,
    Rotate,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>A transform's frame on the canvas: its corners from the top left clockwise, its centre, and the way out of its top side.</summary>
public sealed record ArrangeFrame(SKPoint[] Corners, SKPoint Centre, SKPoint Up);

/// <summary>
/// The Move tool: the active layer moved by dragging — or, on a pixel layer with a selection, what is selected, lifted into a
/// floating layer of its own and dropped back into the layer once done — and Transform (Ctrl+T): a frame round the layer or what
/// is lifted, scaled by its sides and corners, turned by its handle, flipped and turned a quarter from the panel, applied as one
/// step or cancelled. A picture or markup layer takes the transform into its placement, so it is drawn afresh from its source
/// or its annotations and never softens; pixels are resampled once from what they were when the transform began.
/// </summary>
public sealed partial class LayerArranging : ObservableObject
{
    /// <summary>Turns snap to this many degrees while Shift is held.</summary>
    private const double SnapDegrees = 15;

    private readonly ArtistViewModel _viewer;
    private readonly ImageEditingServices _editing;

    // What is lifted from a pixel layer and floats above it, until it is dropped back.
    private (Guid Floating, Guid Source)? _floating;

    // The open move: the picture it began from, the layer moved, where the pointer began.
    private (LayeredPicture Start, Guid Layer, bool Lifted, SKPoint From)? _move;

    // The open transform: the picture it began from, the layer transformed, its frame then, and whether it lifted what it transforms.
    private (LayeredPicture Start, Guid Layer, SKRect Box, bool Lifted)? _transform;
    private double _scaleX = 1, _scaleY = 1, _degrees;
    private SKPoint _offset;

    // The open handle drag: the handle, where the pointer began, and the transform's terms then.
    private (ArrangeHandle Handle, SKPoint From, double ScaleX, double ScaleY, double Degrees, SKPoint Offset)? _drag;

    internal LayerArranging(ArtistViewModel viewer, ImageEditingServices editing)
    {
        _viewer  = viewer;
        _editing = editing;
    }

    /// <summary>A transform is open: the frame shows, and the panel offers to flip, turn, apply or cancel it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TransformCommand), nameof(ApplyCommand), nameof(CancelCommand), nameof(FlipHorizontalCommand),
                                nameof(FlipVerticalCommand), nameof(RotateLeftCommand), nameof(RotateRightCommand), nameof(DropCommand))]
    private bool _isTransforming;

    /// <summary>What was selected floats above its layer, lifted: Drop puts it back.</summary>
    public bool HasFloating => Floating() is not null;

    /// <summary>The photo is no layered picture yet: Add layer is offered, to move and transform layers.</summary>
    public bool OffersLayer => _viewer.CurrentDocument is null;

    /// <summary>Makes the photo a layered picture, where it is not one.</summary>
    public IRelayCommand AddLayerCommand => _viewer.AddLayerCommand;

    /// <summary>The picture or its active layer changed: what floats, and whether there is anything to move, are read again.</summary>
    internal void Refresh()
    {
        if (_floating is not null && Floating() is null) _floating = null;
        OnPropertyChanged(nameof(HasFloating));
        OnPropertyChanged(nameof(OffersLayer));
        TransformCommand.NotifyCanExecuteChanged();
        DropCommand.NotifyCanExecuteChanged();
    }

    private (LayeredSession Document, PixelLayer Floating, PixelLayer Source)? Floating()
        => _floating is { } lifted && _viewer.CurrentDocument is { } document
           && document.Picture.Find(lifted.Floating) is PixelLayer floating && document.Picture.Find(lifted.Source) is PixelLayer source
            ? (document, floating, source) : null;

    // The layer moving and transforming reach: the active one, not locked, and one there is something of to move.
    private static Layer? Movable(LayeredPicture picture) => picture.Active is { Locked: false } layer and not UndrawableLayer ? layer : null;

    /// <summary>What is selected on the active pixel layer lifted into a floating layer above it — the layer left without it —
    /// where there is a selection and nothing floats yet.</summary>
    private bool LiftIfSelected(LayeredSession document)
    {
        var picture = document.Picture;
        if (_floating is not null || picture.Selection.IsEmpty || picture.Active is not PixelLayer { Locked: false } source) return false;
        var floating = new PixelLayer { Name = Str.Get("Images.Arrange.FloatingName"), Pixels = source.Pixels.KeptWhere(picture.Selection) };
        document.Commit(p => p.WithLayer(source.Id, l => l is PixelLayer pixels ? pixels with { Pixels = pixels.Pixels.Cleared(p.Selection) } : l)
                              .Inserted(floating, source.Id));
        _floating = (floating.Id, source.Id);
        OnPropertyChanged(nameof(HasFloating));
        return true;
    }

    /// <summary>Drop: what floats is laid back into the layer it was lifted from, where it now is — one step.</summary>
    [RelayCommand(CanExecute = nameof(CanDrop))]
    public void Drop()
    {
        if (Floating() is not { } held)
        {
            _floating = null;
            return;
        }
        var (document, floating, source) = held;
        document.Commit(p => p.WithLayer(source.Id, l => l is PixelLayer pixels ? pixels with { Pixels = pixels.Pixels.MergedWith(floating.Pixels) } : l)
                              .Without(floating.Id) with { ActiveId = source.Id, EditsMask = false });
        _floating = null;
        OnPropertyChanged(nameof(HasFloating));
    }

    private bool CanDrop() => !IsTransforming && HasFloating;

    // ── Move ─────────────────────────────────────────────────────────────────

    /// <summary>A drag begins at <paramref name="at"/> (canvas pixels): the active layer moves with it, or what is selected,
    /// lifted — one step with the lift. Gives whether there was anything to move.</summary>
    public bool BeginMove(SKPoint at)
    {
        if (IsTransforming || _viewer.CurrentDocument is not { } document || Movable(document.Picture) is null) return false;
        document.History.BeginGesture();
        bool lifted = LiftIfSelected(document);
        var start = document.Picture;
        _move = (start, start.ActiveId!.Value, lifted || _floating is { } held && held.Floating == start.ActiveId, at);
        return true;
    }

    /// <summary>The drag reached <paramref name="at"/>: the layer stands moved by whole pixels from where it began.</summary>
    public void ContinueMove(SKPoint at)
    {
        if (_move is not { } move || _viewer.CurrentDocument is not { } document) return;
        var (start, id, lifted, from) = move;
        int dx = (int)Math.Round(at.X - from.X), dy = (int)Math.Round(at.Y - from.Y);
        document.Commit(_ =>
        {
            var moved = start.WithLayer(id, l => LayerEdits.MovedBy(l, start.Width, start.Height, dx, dy));
            return lifted ? moved with { Selection = start.Selection.Shifted(dx, dy, start.Width, start.Height) } : moved;
        });
    }

    /// <summary>The drag ended: the move is one step.</summary>
    public void EndMove()
    {
        if (_move is null) return;
        _move = null;
        _viewer.CurrentDocument?.History.EndGesture();
    }

    // ── Transform ────────────────────────────────────────────────────────────

    /// <summary>Transform: a frame round the active layer — or round what is selected, lifted — to scale, turn and flip.</summary>
    [RelayCommand(CanExecute = nameof(CanTransform))]
    public void Transform()
    {
        if (IsTransforming || _viewer.CurrentDocument is not { } document || Movable(document.Picture) is null) return;
        document.History.BeginGesture();
        bool lifted = LiftIfSelected(document);
        var start = document.Picture;
        var layer = start.Active!;
        if (ContentBounds(document, layer) is not { IsEmpty: false } box)
        {
            document.History.CancelGesture();
            if (lifted) _floating = null;
            return;
        }
        _transform = (start, layer.Id, box, lifted);
        (_scaleX, _scaleY, _degrees, _offset) = (1, 1, 0, SKPoint.Empty);
        IsTransforming = true;
        OnPropertyChanged(nameof(Frame));
    }

    private bool CanTransform() => !IsTransforming && _viewer.CurrentDocument is { } document && Movable(document.Picture) is not null;

    /// <summary>Apply: the transform is one step; what was lifted for it is dropped back.</summary>
    [RelayCommand(CanExecute = nameof(IsTransforming))]
    public void Apply()
    {
        if (_transform is null) return;
        _transform = null;
        _drag = null;
        IsTransforming = false;
        _viewer.CurrentDocument?.History.EndGesture();
        if (HasFloating) Drop();
        OnPropertyChanged(nameof(Frame));
    }

    /// <summary>Cancel: the layer as it was before the transform, what was lifted for it back in its layer.</summary>
    [RelayCommand(CanExecute = nameof(IsTransforming))]
    public void Cancel()
    {
        if (_transform is not { } open) return;
        bool lifted = open.Lifted;
        _transform = null;
        _drag = null;
        IsTransforming = false;
        _viewer.CurrentDocument?.History.CancelGesture();
        if (lifted) _floating = null;
        OnPropertyChanged(nameof(HasFloating));
        OnPropertyChanged(nameof(Frame));
    }

    [RelayCommand(CanExecute = nameof(IsTransforming))]
    private void FlipHorizontal() => Change(() => _scaleX = -_scaleX);

    [RelayCommand(CanExecute = nameof(IsTransforming))]
    private void FlipVertical() => Change(() => _scaleY = -_scaleY);

    [RelayCommand(CanExecute = nameof(IsTransforming))]
    private void RotateLeft() => Change(() => _degrees -= 90);

    [RelayCommand(CanExecute = nameof(IsTransforming))]
    private void RotateRight() => Change(() => _degrees += 90);

    private void Change(Action change)
    {
        change();
        Show();
    }

    /// <summary>The transform as it stands: the frame's centre to the origin, scaled, turned, and back where it is placed.</summary>
    internal SKMatrix Matrix
    {
        get
        {
            if (_transform is not { } open) return SKMatrix.Identity;
            var box = open.Box;
            var centre = new SKPoint(box.MidX, box.MidY);
            return SKMatrix.CreateTranslation(-centre.X, -centre.Y)
                           .PostConcat(SKMatrix.CreateScale((float)_scaleX, (float)_scaleY))
                           .PostConcat(SKMatrix.CreateRotationDegrees((float)_degrees))
                           .PostConcat(SKMatrix.CreateTranslation(centre.X + _offset.X, centre.Y + _offset.Y));
        }
    }

    /// <summary>The frame on the canvas, in canvas pixels; null while no transform is open.</summary>
    public ArrangeFrame? Frame
    {
        get
        {
            if (_transform is not { } open) return null;
            var box = open.Box;
            var m = Matrix;
            var corners = m.MapPoints([new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom)]);
            var centre = m.MapPoint(new SKPoint(box.MidX, box.MidY));
            var top = new SKPoint((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
            var up = top - centre;
            float length = up.Length;
            return new ArrangeFrame(corners, centre, length > 0 ? new SKPoint(up.X / length, up.Y / length) : new SKPoint(0, -1));
        }
    }

    // The picture drawn with the transform as it stands, from what it began.
    private void Show()
    {
        if (_transform is not { } open || _viewer.CurrentDocument is not { } document) return;
        var (start, id, _, lifted) = open;
        var m = Matrix;
        document.Commit(_ =>
        {
            var transformed = start.WithLayer(id, l => LayerEdits.Transformed(l, m, start.Width, start.Height));
            return lifted ? transformed with { Selection = LayerEdits.MappedCoverage(start.Selection, m) } : transformed;
        });
        OnPropertyChanged(nameof(Frame));
    }

    /// <summary>A handle of the frame is taken hold of at <paramref name="at"/> (canvas pixels).</summary>
    public void BeginHandle(ArrangeHandle handle, SKPoint at)
    {
        if (!IsTransforming || handle == ArrangeHandle.None) return;
        _drag = (handle, at, _scaleX, _scaleY, _degrees, _offset);
    }

    /// <summary>
    /// The handle is dragged to <paramref name="at"/>: the body moves the frame, the turning handle turns it about its centre —
    /// by steps of 15° with <paramref name="constrain"/> — and a side or corner scales it, the opposite side or corner staying
    /// put, a corner keeping its proportions with <paramref name="constrain"/>.
    /// </summary>
    public void DragHandle(SKPoint at, bool constrain)
    {
        if (_drag is not { } drag || _transform is not { } open) return;
        var (handle, from, scaleX, scaleY, degrees, offset) = drag;
        var box = open.Box;
        (_scaleX, _scaleY, _degrees, _offset) = (scaleX, scaleY, degrees, offset);
        var centre = new SKPoint(box.MidX + offset.X, box.MidY + offset.Y);
        switch (handle)
        {
            case ArrangeHandle.Body:
                _offset = new SKPoint(offset.X + at.X - from.X, offset.Y + at.Y - from.Y);
                break;
            case ArrangeHandle.Rotate:
            {
                double turned = degrees + (Math.Atan2(at.Y - centre.Y, at.X - centre.X) - Math.Atan2(from.Y - centre.Y, from.X - centre.X)) * 180 / Math.PI;
                _degrees = constrain ? Math.Round(turned / SnapDegrees) * SnapDegrees : turned;
                break;
            }
            default:
                Scale(handle, at, box, centre, scaleX, scaleY, degrees, constrain);
                break;
        }
        Show();
    }

    // A side or corner dragged to `at`: scaled so the opposite side or corner stays where it was.
    private void Scale(ArrangeHandle handle, SKPoint at, SKRect box, SKPoint centre, double scaleX, double scaleY, double degrees, bool constrain)
    {
        var (hx, hy) = Direction(handle);
        double halfW = box.Width / 2, halfH = box.Height / 2;
        double radians = degrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        // The fixed point: the opposite side's or corner's place, in the frame's turned axes.
        double fx = -hx * halfW * scaleX, fy = -hy * halfH * scaleY;
        var fixedAt = new SKPoint((float)(centre.X + fx * cos - fy * sin), (float)(centre.Y + fx * sin + fy * cos));
        // The pointer from there, in the frame's own axes.
        double vx = (at.X - fixedAt.X) * cos + (at.Y - fixedAt.Y) * sin, vy = -(at.X - fixedAt.X) * sin + (at.Y - fixedAt.Y) * cos;
        double sx = hx != 0 && halfW > 0 ? vx / (2 * hx * halfW) : scaleX;
        double sy = hy != 0 && halfH > 0 ? vy / (2 * hy * halfH) : scaleY;
        if (constrain && hx != 0 && hy != 0)
        {
            double kx = sx / scaleX, ky = sy / scaleY, k = Math.Abs(kx) > Math.Abs(ky) ? kx : ky;
            (sx, sy) = (scaleX * k, scaleY * k);
        }
        if (Math.Abs(sx) * box.Width < 1) sx = Math.CopySign(1 / Math.Max(1, box.Width), sx == 0 ? 1 : sx);
        if (Math.Abs(sy) * box.Height < 1) sy = Math.CopySign(1 / Math.Max(1, box.Height), sy == 0 ? 1 : sy);
        // The centre where the fixed point stays put.
        double cx = hx * halfW * sx, cy = hy * halfH * sy;
        var newCentre = new SKPoint((float)(fixedAt.X + cx * cos - cy * sin), (float)(fixedAt.Y + cx * sin + cy * cos));
        (_scaleX, _scaleY) = (sx, sy);
        _offset = new SKPoint(newCentre.X - box.MidX, newCentre.Y - box.MidY);
    }

    private static (int X, int Y) Direction(ArrangeHandle handle) => handle switch
    {
        ArrangeHandle.TopLeft     => (-1, -1),
        ArrangeHandle.Top         => (0, -1),
        ArrangeHandle.TopRight    => (1, -1),
        ArrangeHandle.Right       => (1, 0),
        ArrangeHandle.BottomRight => (1, 1),
        ArrangeHandle.Bottom      => (0, 1),
        ArrangeHandle.BottomLeft  => (-1, 1),
        ArrangeHandle.Left        => (-1, 0),
        _                         => (0, 0),
    };

    /// <summary>The handle is let go.</summary>
    public void EndHandle() => _drag = null;

    /// <summary>What a layer shows, on the canvas: its pixels' bounds, its picture's or annotations' placed, a group's together;
    /// null where it shows nothing.</summary>
    private SKRect? ContentBounds(LayeredSession document, Layer layer)
    {
        switch (layer)
        {
            case PixelLayer pixels when !pixels.Pixels.IsEmpty:
                var b = pixels.Pixels.Bounds;
                return SKRect.Create(b.Left, b.Top, b.Width, b.Height);
            case PictureLayer picture when document.RenderingOf(picture.Id) is { Proxy: { } proxy } rendering:
                return picture.Placement.ToMatrix().MapRect(rendering.OutputSize(picture.Recipe) is { } output
                    ? SKRect.Create(output.Width, output.Height)
                    : SKRect.Create(proxy.SourceSize.Width, proxy.SourceSize.Height));
            case MarkupLayer markup when !markup.Elements.IsEmpty:
                return markup.Placement.ToMatrix().MapRect(markup.Elements.Select(_editing.Markup.Bounds).Aggregate(SKRect.Union));
            case GroupLayer group:
                var parts = group.Children.Select(c => ContentBounds(document, c)).OfType<SKRect>().ToList();
                return parts.Count == 0 ? null : parts.Aggregate(SKRect.Union);
            default:
                return null;
        }
    }
}
