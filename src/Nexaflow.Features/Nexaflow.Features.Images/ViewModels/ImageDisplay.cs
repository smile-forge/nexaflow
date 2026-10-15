using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>How the photo is being shown — what decides what is rendered and at what size.</summary>
/// <param name="Editing">On the edit canvas: laid out to fit, rendered at the screen's density times the zoom.</param>
/// <param name="ShowOriginal">The photo as it is on disk, for comparing.</param>
/// <param name="EditFrame">What the edit canvas shows: the finished picture, or the uncropped one while cropping.</param>
/// <param name="FitToWindow">Outside the editor: fitted to the viewport, or at actual size.</param>
public readonly record struct ImageDisplayMode(bool Editing, bool ShowOriginal, RenderFrame EditFrame, bool FitToWindow);

/// <summary>
/// Puts the photo being viewed on screen: decodes it off the UI thread at the size the screen needs — sharper as a
/// zoom or a tight crop asks for more — renders its recipe for the way it is being shown, and hands each frame to the
/// view with what it is a picture of (<see cref="EditCanvas"/>).
/// </summary>
/// <remarks>
/// The viewer says which photo (<see cref="Show"/>) and how (<see cref="Render"/>); everything about sizes, densities
/// and decodes is here. Frames and decode results arrive on the UI thread, through the delegate it is given.
/// </remarks>
public sealed class ImageDisplay : IDisposable
{
    /// <summary>Decodes are sized in steps of this, so a slowly growing zoom does not decode at every step.</summary>
    private const int DecodeStep = 512;

    private readonly ImageEditingServices _editing;
    private readonly Func<Action, Task> _runOnUi;
    private readonly CancellationToken _lifetime;
    private readonly SkiaBitmapPresenter _presenter;

    private ImageEditSession? _session;
    private CancellationTokenSource? _decodeCts;
    private int _decodingEdge;

    // The restoration being decoded: its file, and the long edge asked of it.
    private CancellationTokenSource? _restoredCts;
    private string? _restoredFile;
    private int _restoredEdge;

    // The surface the photo is shown on, in device-independent pixels, its pixel density, and the edit canvas's zoom.
    private double _viewportWidth = 1600, _viewportHeight = 1000, _pixelsPerDip = 1, _editZoom = 1;

    public ImageDisplay(ImageEditingServices editing, Func<Action, Task> runOnUi, CancellationToken lifetime)
    {
        _editing  = editing;
        _runOnUi  = runOnUi;
        _lifetime = lifetime;
        _presenter = new SkiaBitmapPresenter(runOnUi);
        _presenter.BitmapChanged += () => ImageChanged?.Invoke(_presenter.Bitmap);
        _presenter.Presented     += tag => { if (tag is EditCanvas canvas) Presented?.Invoke(canvas); };
    }

    /// <summary>The bitmap frames land in is a new one, or none. On the UI thread.</summary>
    public event Action<BitmapSource?>? ImageChanged;

    /// <summary>A frame landed, a picture of this. On the UI thread.</summary>
    public event Action<EditCanvas>? Presented;

    /// <summary>The photo has pixels to render from — the first decode, or a sharper one. On the UI thread.</summary>
    public event Action<ImageEditSession>? Decoded;

    /// <summary>The photo could not be decoded. On the UI thread.</summary>
    public event Action<ImageEditSession>? DecodeFailed;

    /// <summary>The photo's restoration could not be decoded — its kept file gone, say. On the UI thread.</summary>
    public event Action<ImageEditSession>? RestoredDecodeFailed;

    /// <summary>The photo to show from now on; a different one — or the same one still without pixels — starts its decodes afresh and, until it has pixels, shows nothing.</summary>
    public void Show(ImageEditSession? session)
    {
        if (ReferenceEquals(session, _session) && session?.Proxy is not null) return;
        _decodeCts?.Cancel();
        _decodingEdge = 0;
        _restoredCts?.Cancel();
        _restoredFile = null;
        _restoredEdge = 0;
        _session = session;
        if (session?.Proxy is null) _presenter.Clear();
    }

    /// <summary>Shows nothing.</summary>
    public void Clear()
    {
        Show(null);
        _presenter.Clear();
    }

    /// <summary>The surface changed size (device-independent pixels) or density; true when it is worth a new frame.</summary>
    public bool SetViewport(double width, double height, double pixelsPerDip)
    {
        if (width <= 0 || height <= 0) return false;
        bool changed = Math.Abs(width - _viewportWidth) > 1 || Math.Abs(height - _viewportHeight) > 1 || Math.Abs(pixelsPerDip - _pixelsPerDip) > 0.01;
        (_viewportWidth, _viewportHeight, _pixelsPerDip) = (width, height, pixelsPerDip);
        return changed;
    }

    /// <summary>The edit canvas was zoomed; true when it is worth a new frame.</summary>
    public bool SetEditZoom(double zoom)
    {
        if (zoom <= 0 || Math.Abs(zoom - _editZoom) < 0.01) return false;
        _editZoom = zoom;
        return true;
    }

    /// <summary>
    /// Renders the photo for <paramref name="mode"/> — the finished picture, or the uncropped one while cropping;
    /// fitted, at 100% or at the edit canvas's zoom — and decodes it sharper first if the decode it has is too small
    /// for that. Before there are pixels, it decodes them.
    /// </summary>
    public void Render(ImageDisplayMode mode)
    {
        if (_session is not { } session) return;
        if (session.Proxy is not { } proxy)
        {
            EnsureDecoded(session, InitialDecodeEdge());
            return;
        }

        var showOriginal = mode.Editing && mode.ShowOriginal;
        var recipe = showOriginal ? ImageRecipe.Identity : session.Recipe;
        var frame = mode.Editing && !showOriginal ? mode.EditFrame : RenderFrame.Output;
        var source = proxy.SourceSize;

        CropGeometry.FrameToCanvas(recipe, source, frame, 1, out var natural);
        double layoutScale, renderScale;
        if (mode.Editing)
        {
            // Laid out to fit the canvas; rendered at the screen's density times the zoom, so zooming in sharpens.
            layoutScale = Math.Min(1, Math.Min(_viewportWidth / natural.Width, _viewportHeight / natural.Height));
            renderScale = Math.Min(1, layoutScale * _pixelsPerDip * _editZoom);
        }
        else
        {
            layoutScale = renderScale = mode.FitToWindow
                ? Math.Min(1, Math.Min(_viewportWidth * _pixelsPerDip / natural.Width, _viewportHeight * _pixelsPerDip / natural.Height))
                : 1;
        }

        var sourceToCanvas = CropGeometry.SourceToCanvas(recipe, source, frame, renderScale, out _);
        EnsureSharpEnough(session, proxy, CropGeometry.SourcePixelsPerCanvasPixel(sourceToCanvas));
        if (recipe.Restore is { } restore && session.RestoredPath is { } restoredFile) EnsureRestoredDecoded(session, restoredFile, proxy, restore.Scale);

        var straightenedToLayout = CropGeometry.FrameToCanvas(recipe, source, frame, layoutScale, out var layout);
        var canvas = new EditCanvas(recipe, frame, source, layoutScale, layout, straightenedToLayout,
                                    CropGeometry.Straighten(source, recipe.Geometry.StraightenDegrees).PostConcat(straightenedToLayout));

        var request = new RenderRequest(frame, renderScale, Adjustments: !showOriginal, Markup: !showOriginal);
        double dpi = mode.Editing ? 96 * renderScale / layoutScale : 96;
        _presenter.Present(consume => session.Render(recipe, request, consume), dpi, canvas);
    }

    /// <summary>The first decode's size: enough for the viewport, before the photo's own size is known.</summary>
    private int InitialDecodeEdge() =>
        RoundUpToStep(Math.Max(1024, Math.Max(_viewportWidth, _viewportHeight) * _pixelsPerDip));

    private void EnsureSharpEnough(ImageEditSession session, DecodedImage proxy, double sourcePerCanvasPixel)
    {
        if (proxy.IsFullResolution || proxy.Reduction <= Math.Max(1, sourcePerCanvasPixel) * 1.25) return;
        int storedEdge = Math.Max(proxy.StoredFullSize.Width, proxy.StoredFullSize.Height);
        int needed = RoundUpToStep(storedEdge / Math.Max(1, sourcePerCanvasPixel));
        EnsureDecoded(session, Math.Min(needed, storedEdge));
    }

    /// <summary>Decodes the photo off the UI thread at <paramref name="longEdge"/>, once per size asked for.</summary>
    private void EnsureDecoded(ImageEditSession session, int longEdge)
    {
        if (_decodingEdge >= longEdge) return;
        _decodingEdge = longEdge;
        _decodeCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _decodeCts = cts;

        _ = Task.Run(() =>
        {
            DecodedImage decoded;
            try
            {
                decoded = _editing.Decode(session.Path, longEdge);
            }
            catch (Exception ex) when (ex is ImageDecodeException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                if (!cts.IsCancellationRequested)
                    _ = _runOnUi(() =>
                    {
                        if (!ReferenceEquals(_session, session)) return;
                        _presenter.Clear();
                        DecodeFailed?.Invoke(session);
                    });
                return;
            }
            if (cts.IsCancellationRequested) { decoded.Dispose(); return; }

            session.Attach(decoded);
            _ = _runOnUi(() => { if (ReferenceEquals(_session, session)) Decoded?.Invoke(session); });
        }, cts.Token);
    }

    /// <summary>
    /// Decodes the restoration off the UI thread at the sharpness the photo's own decode has, <paramref name="scale"/> times
    /// larger — or whole, once the photo is decoded whole — once per file and size asked for.
    /// </summary>
    private void EnsureRestoredDecoded(ImageEditSession session, string file, DecodedImage proxy, int scale)
    {
        int edge = proxy.IsFullResolution ? int.MaxValue : Math.Max(proxy.Pixels.Width, proxy.Pixels.Height) * scale;
        if (string.Equals(file, _restoredFile, StringComparison.OrdinalIgnoreCase) && _restoredEdge >= edge) return;
        _restoredFile = file;
        _restoredEdge = edge;
        _restoredCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _restoredCts = cts;
        var sourceSize = proxy.SourceSize;

        _ = Task.Run(() =>
        {
            DecodedImage restored;
            try
            {
                restored = _editing.DecodeRestored(file, sourceSize, edge == int.MaxValue ? null : edge);
            }
            catch (Exception ex) when (ex is ImageDecodeException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                if (!cts.IsCancellationRequested)
                    _ = _runOnUi(() =>
                    {
                        if (!ReferenceEquals(_session, session)) return;
                        _restoredFile = null;
                        RestoredDecodeFailed?.Invoke(session);
                    });
                return;
            }
            if (cts.IsCancellationRequested) { restored.Dispose(); return; }

            session.AttachRestored(restored, file);
            _ = _runOnUi(() => { if (ReferenceEquals(_session, session)) Decoded?.Invoke(session); });
        }, cts.Token);
    }

    private static int RoundUpToStep(double edge) => (int)Math.Ceiling(edge / DecodeStep) * DecodeStep;

    public void Dispose()
    {
        _decodeCts?.Cancel();
        _restoredCts?.Cancel();
        _presenter.Dispose();
    }
}
