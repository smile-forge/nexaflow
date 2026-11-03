using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Features.Images.Editing.Wpf;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>How the picture is being shown — what decides what is rendered and at what size.</summary>
/// <param name="Editing">On the edit canvas: laid out to fit, rendered at the screen's density times the zoom.</param>
/// <param name="ShowOriginal">The picture as it is on disk, for comparing.</param>
/// <param name="EditFrame">What the edit canvas shows: the finished picture, or the uncropped one while cropping.</param>
/// <param name="FitToWindow">Outside the editor: fitted to the viewport, or at actual size.</param>
/// <param name="MaskAlone">In a layered picture, this layer's mask shown alone instead of the layers.</param>
/// <param name="Tones">Whether each frame's tones are read, and which ends are marked where they clip.</param>
public readonly record struct ImageDisplayMode(bool Editing, bool ShowOriginal, RenderFrame EditFrame, bool FitToWindow, Guid? MaskAlone = null,
                                               ToneAsk Tones = default);

/// <summary>
/// Puts the picture being viewed on screen — a photo, or a layered picture — decoding each picture it is drawn from off the
/// UI thread at the size the screen needs, sharper as a zoom or a tight crop asks for more; rendering it for the way it is
/// being shown; and handing each frame to the view with what it is a picture of (<see cref="EditCanvas"/>).
/// </summary>
/// <remarks>
/// The viewer says what to show (<see cref="Show"/>, <see cref="ShowDocument"/>) and how (<see cref="Render"/>); everything
/// about sizes, densities and decodes is here. In a layered picture, cropping the active picture layer, or comparing it with
/// its original, shows that layer alone, as a photo is shown — its crop frame may reach past the canvas. Frames and decode
/// results arrive on the UI thread, through the delegate it is given.
/// </remarks>
public sealed class ImageDisplay : IDisposable
{
    /// <summary>Decodes are sized in steps of this, so a slowly growing zoom does not decode at every step.</summary>
    private const int DecodeStep = 512;

    private readonly ImageEditingServices _editing;
    private readonly Func<Action, Task> _runOnUi;
    private readonly CancellationToken _lifetime;
    private readonly SkiaBitmapPresenter _presenter;

    private readonly ToneReader _tones = new();

    private ImageEditSession? _session;
    private LayeredSession? _document;
    private readonly Dictionary<PictureRendering, PictureDecodes> _decodes = new(ReferenceEqualityComparer.Instance);

    // The surface the picture is shown on, in device-independent pixels, its pixel density, and the edit canvas's zoom.
    private double _viewportWidth = 1600, _viewportHeight = 1000, _pixelsPerDip = 1, _editZoom = 1;

    public ImageDisplay(ImageEditingServices editing, Func<Action, Task> runOnUi, CancellationToken lifetime)
    {
        _editing  = editing;
        _runOnUi  = runOnUi;
        _lifetime = lifetime;
        _presenter = new SkiaBitmapPresenter(runOnUi);
        _presenter.BitmapChanged += () => ImageChanged?.Invoke(_presenter.Bitmap);
        _presenter.Presented     += (tag, reading) =>
        {
            if (reading is ToneReading tones) TonesRead?.Invoke(tones);
            if (tag is EditCanvas canvas) Presented?.Invoke(canvas);
        };
    }

    /// <summary>The bitmap frames land in is a new one, or none. On the UI thread.</summary>
    public event Action<BitmapSource?>? ImageChanged;

    /// <summary>A frame landed, a picture of this. On the UI thread.</summary>
    public event Action<EditCanvas>? Presented;

    /// <summary>A frame landed whose tones were asked for, read from it as rendered. On the UI thread, while what the reading
    /// marked is still that frame's.</summary>
    public event Action<ToneReading>? TonesRead;

    /// <summary>A picture shown has pixels to render from — the first decode, or a sharper one, or what a tool made. On the UI thread.</summary>
    public event Action<PictureRendering>? Decoded;

    /// <summary>A picture shown could not be decoded. On the UI thread.</summary>
    public event Action<PictureRendering>? DecodeFailed;

    /// <summary>A picture's restoration could not be decoded — its kept file gone, say. On the UI thread.</summary>
    public event Action<PictureRendering>? RestoredDecodeFailed;

    /// <summary>A picture's cut-out mask could not be decoded — its kept file gone, say. On the UI thread.</summary>
    public event Action<PictureRendering>? CutOutDecodeFailed;

    /// <summary>A picture as erased could not be decoded — its kept file gone, say. On the UI thread.</summary>
    public event Action<PictureRendering>? ErasedDecodeFailed;

    /// <summary>A picture's scene depth could not be read — its kept file gone, say. On the UI thread.</summary>
    public event Action<PictureRendering>? SceneDepthReadFailed;

    /// <summary>The photo to show from now on; a different one — or the same one still without pixels — starts its decodes afresh and, until it has pixels, shows nothing.</summary>
    public void Show(ImageEditSession? session)
    {
        if (_document is null && ReferenceEquals(session, _session) && session?.Proxy is not null) return;
        Forget();
        _session = session;
        if (session?.Proxy is null) _presenter.Clear();
    }

    /// <summary>The layered picture to show from now on, in place of any photo.</summary>
    public void ShowDocument(LayeredSession? document)
    {
        if (ReferenceEquals(document, _document)) return;
        Forget();
        _document = document;
        _session = null;
        if (document is null) _presenter.Clear();
    }

    /// <summary>Shows nothing.</summary>
    public void Clear()
    {
        Show(null);
        _presenter.Clear();
    }

    private void Forget()
    {
        foreach (var decodes in _decodes.Values) decodes.Cancel();
        _decodes.Clear();
        _session = null;
        _document = null;
    }

    /// <summary>Whether <paramref name="rendering"/> is one this display shows: the photo, or a layer of the layered picture.</summary>
    private bool Shows(PictureRendering rendering)
        => ReferenceEquals(rendering, _session) || ReferenceEquals(rendering, _document?.SavedLook) || _document?.Renderings.Contains(rendering) == true;

    private PictureDecodes DecodesOf(PictureRendering rendering)
    {
        if (!_decodes.TryGetValue(rendering, out var decodes)) _decodes[rendering] = decodes = new PictureDecodes(this, rendering);
        return decodes;
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
    /// Renders what is shown for <paramref name="mode"/> — the finished picture, or the uncropped one while cropping; fitted,
    /// at 100% or at the edit canvas's zoom — and decodes what it is drawn from sharper first if the decodes it has are too
    /// small for that. Before there are pixels, it decodes them.
    /// </summary>
    public void Render(ImageDisplayMode mode)
    {
        if (_document is { } document)
        {
            RenderDocument(document, mode);
            return;
        }
        if (_session is { } session) RenderPicture(session, session.Recipe, mode);
    }

    /// <summary>One picture — a photo, or a picture layer shown alone — as a photo is shown.</summary>
    private void RenderPicture(PictureRendering rendering, ImageRecipe current, ImageDisplayMode mode)
    {
        var decodes = DecodesOf(rendering);
        if (rendering.Proxy is not { } proxy)
        {
            decodes.EnsureDecoded(InitialDecodeEdge());
            return;
        }

        var showOriginal = mode.Editing && mode.ShowOriginal;
        var recipe = showOriginal ? ImageRecipe.Identity : current;
        var frame = mode.Editing && !showOriginal ? mode.EditFrame : RenderFrame.Output;
        var source = proxy.SourceSize;

        CropGeometry.FrameToCanvas(recipe, source, frame, 1, out var natural);
        var (layoutScale, renderScale) = Scales(mode, natural);

        var sourceToCanvas = CropGeometry.SourceToCanvas(recipe, source, frame, renderScale, out _);
        decodes.EnsureSharpEnough(proxy, CropGeometry.SourcePixelsPerCanvasPixel(sourceToCanvas));
        decodes.EnsureMade(recipe, proxy);

        var straightenedToLayout = CropGeometry.FrameToCanvas(recipe, source, frame, layoutScale, out var layout);
        var sourceToLayout = CropGeometry.Straighten(source, recipe.Geometry.StraightenDegrees).PostConcat(straightenedToLayout);
        var canvas = new EditCanvas(recipe, frame, source, layoutScale, layout, straightenedToLayout, sourceToLayout, source, sourceToLayout);

        var request = new RenderRequest(frame, renderScale, Adjustments: !showOriginal, Markup: !showOriginal);
        double dpi = mode.Editing ? 96 * renderScale / layoutScale : 96;
        _presenter.Present(consume => rendering.Render(recipe, request, consume), dpi, canvas, ToneReadingFor(mode));
    }

    /// <summary>
    /// A layered picture: its layers composited at the canvas's size — or, while its active picture layer is cropped or
    /// compared, that layer alone — with each picture layer decoded as sharp as its place on the canvas calls for.
    /// </summary>
    private void RenderDocument(LayeredSession document, ImageDisplayMode mode)
    {
        // Opened on its saved look: the picture as the application that wrote it drew it, until a way on is chosen.
        if (document.SavedLook is { } look)
        {
            RenderPicture(look, ImageRecipe.Identity, mode with { EditFrame = RenderFrame.Output, ShowOriginal = false });
            return;
        }
        var picture = document.Picture;
        if (mode.Editing && picture.Active is PictureLayer active && document.RenderingOf(active.Id) is { } alone
            && (mode.EditFrame == RenderFrame.CropEditing || mode.ShowOriginal))
        {
            RenderPicture(alone, active.Recipe, mode);
            return;
        }

        var natural = new SKSizeI(picture.Width, picture.Height);
        var (layoutScale, renderScale) = Scales(mode, natural);
        foreach (var layer in picture.All.OfType<PictureLayer>())
        {
            if (document.RenderingOf(layer.Id) is not { } rendering) continue;
            var decodes = DecodesOf(rendering);
            if (rendering.Proxy is not { } proxy)
            {
                decodes.EnsureDecoded(InitialDecodeEdge());
                continue;
            }
            var toOutput = LayeredSession.SourceToCanvas(layer, proxy.SourceSize).PostConcat(SKMatrix.CreateScale((float)renderScale, (float)renderScale));
            decodes.EnsureSharpEnough(proxy, CropGeometry.SourcePixelsPerCanvasPixel(toOutput));
            decodes.EnsureMade(layer.Recipe, proxy);
        }

        var documentToLayout = SKMatrix.CreateScale((float)layoutScale, (float)layoutScale);
        var layout = new SKSizeI(Math.Max(1, (int)Math.Round(picture.Width * layoutScale)), Math.Max(1, (int)Math.Round(picture.Height * layoutScale)));
        var documentSize = new SKSize(picture.Width, picture.Height);
        EditCanvas canvas;
        if (picture.Active is PictureLayer layerActive && document.RenderingOf(layerActive.Id)?.Proxy is { } activeProxy)
        {
            var source = activeProxy.SourceSize;
            var placement = layerActive.Placement.ToMatrix().PostConcat(documentToLayout);
            var sourceToOutput = CropGeometry.SourceToCanvas(layerActive.Recipe, source, RenderFrame.Output, 1, out _);
            var straightened = CropGeometry.FrameToCanvas(layerActive.Recipe, source, RenderFrame.Output, 1, out _);
            canvas = new EditCanvas(layerActive.Recipe, RenderFrame.Output, source, layoutScale, layout,
                                    straightened.PostConcat(placement), sourceToOutput.PostConcat(placement), documentSize, documentToLayout);
        }
        else if (picture.Active is MarkupLayer notes)
        {
            // A markup layer's annotations lie in its own coordinates, placed on the canvas: the Markup tool works through that.
            var placement = notes.Placement.ToMatrix().PostConcat(documentToLayout);
            canvas = new EditCanvas(ImageRecipe.Identity, RenderFrame.Output, documentSize, layoutScale, layout, placement, placement,
                                    documentSize, documentToLayout);
        }
        else
        {
            canvas = new EditCanvas(ImageRecipe.Identity, RenderFrame.Output, documentSize, layoutScale, layout, documentToLayout, documentToLayout,
                                    documentSize, documentToLayout);
        }

        var options = new CompositeOptions(MaskAlone: mode.Editing ? mode.MaskAlone : null);
        double dpi = mode.Editing ? 96 * renderScale / layoutScale : 96;
        _presenter.Present(consume => document.Render(picture, renderScale, options, consume), dpi, canvas, ToneReadingFor(mode));
    }

    /// <summary>The scale a picture of <paramref name="natural"/> size is laid out at, and the one it is rendered at.</summary>
    private (double Layout, double Render) Scales(ImageDisplayMode mode, SKSizeI natural)
    {
        if (mode.Editing)
        {
            // Laid out to fit the canvas; rendered at the screen's density times the zoom, so zooming in sharpens.
            double layout = Math.Min(1, Math.Min(_viewportWidth / natural.Width, _viewportHeight / natural.Height));
            return (layout, Math.Min(1, layout * _pixelsPerDip * _editZoom));
        }
        double fitted = mode.FitToWindow
            ? Math.Min(1, Math.Min(_viewportWidth * _pixelsPerDip / natural.Width, _viewportHeight * _pixelsPerDip / natural.Height))
            : 1;
        return (fitted, fitted);
    }

    /// <summary>What reads each frame's tones for <paramref name="mode"/>; none where it asks for none.</summary>
    private Func<SKImage, object?>? ToneReadingFor(ImageDisplayMode mode)
    {
        if (!mode.Tones.Read) return null;
        var marked = mode.Tones.Marked;
        return picture => _tones.Read(picture, marked);
    }

    /// <summary>The first decode's size: enough for the viewport, before the picture's own size is known.</summary>
    private int InitialDecodeEdge() =>
        RoundUpToStep(Math.Max(1024, Math.Max(_viewportWidth, _viewportHeight) * _pixelsPerDip));

    private static int RoundUpToStep(double edge) => (int)Math.Ceiling(edge / DecodeStep) * DecodeStep;

    public void Dispose()
    {
        foreach (var decodes in _decodes.Values) decodes.Cancel();
        _presenter.Dispose();
    }

    /// <summary>
    /// What one picture is drawn from, decoded or being decoded: the picture itself at the size asked for, and what the tools
    /// made of it — its restoration, the picture as erased, its cut-out mask — beside it.
    /// </summary>
    private sealed class PictureDecodes
    {
        private readonly ImageDisplay _display;
        private readonly PictureRendering _rendering;
        private readonly MadeDecode _restored;
        private readonly MadeDecode _erased;
        private CancellationTokenSource? _decodeCts;
        private int _decodingEdge;
        private CancellationTokenSource? _cutOutCts;
        private string? _cutOutFile;
        private CancellationTokenSource? _sceneDepthCts;
        private string? _sceneDepthFile;

        public PictureDecodes(ImageDisplay display, PictureRendering rendering)
        {
            _display = display;
            _rendering = rendering;
            _restored = new MadeDecode(this, r => r.RestoredProxy, (r, picture, file) => r.AttachRestored(picture, file), r => display.RestoredDecodeFailed?.Invoke(r));
            _erased   = new MadeDecode(this, r => r.ErasedProxy, (r, picture, file) => r.AttachErased(picture, file), r => display.ErasedDecodeFailed?.Invoke(r));
        }

        public void Cancel()
        {
            _decodeCts?.Cancel();
            _restored.Cancel();
            _erased.Cancel();
            _cutOutCts?.Cancel();
            _sceneDepthCts?.Cancel();
        }

        /// <summary>What the tools made for <paramref name="recipe"/>, decoded beside the picture as it is made.</summary>
        public void EnsureMade(ImageRecipe recipe, DecodedImage proxy)
        {
            if (!recipe.Erasures.IsEmpty && _rendering.ErasedPath is { } erasedFile) _erased.Ensure(erasedFile, proxy, 1);
            if (recipe.Restore is { } restore && _rendering.RestoredPath is { } restoredFile) _restored.Ensure(restoredFile, proxy, restore.Scale);
            if (recipe.CutOut is not null && _rendering.CutOutPath is { } cutOutFile) EnsureCutOutDecoded(cutOutFile);
            if (recipe.AsksSceneDepth && _rendering.SceneDepthPath is { } depthFile) EnsureSceneDepthRead(depthFile);
        }

        public void EnsureSharpEnough(DecodedImage proxy, double sourcePerCanvasPixel)
        {
            if (proxy.IsFullResolution || proxy.Reduction <= Math.Max(1, sourcePerCanvasPixel) * 1.25) return;
            int storedEdge = Math.Max(proxy.StoredFullSize.Width, proxy.StoredFullSize.Height);
            int needed = RoundUpToStep(storedEdge / Math.Max(1, sourcePerCanvasPixel));
            EnsureDecoded(Math.Min(needed, storedEdge));
        }

        /// <summary>Decodes the picture off the UI thread at <paramref name="longEdge"/>, once per size asked for.</summary>
        public void EnsureDecoded(int longEdge)
        {
            if (_decodingEdge >= longEdge) return;
            _decodingEdge = longEdge;
            _decodeCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_display._lifetime);
            _decodeCts = cts;
            var rendering = _rendering;

            _ = Task.Run(async () =>
            {
                DecodedImage decoded;
                try
                {
                    decoded = await _display._editing.DecodeAsync(rendering.Path, longEdge);
                }
                catch (Exception ex) when (ex is ImageDecodeException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                {
                    if (!cts.IsCancellationRequested)
                        _ = _display._runOnUi(() =>
                        {
                            if (!_display.Shows(rendering)) return;
                            if (ReferenceEquals(rendering, _display._session)) _display._presenter.Clear();
                            _display.DecodeFailed?.Invoke(rendering);
                        });
                    return;
                }
                if (cts.IsCancellationRequested) { decoded.Dispose(); return; }

                rendering.Attach(decoded);
                _ = _display._runOnUi(() => { if (_display.Shows(rendering)) _display.Decoded?.Invoke(rendering); });
            }, cts.Token);
        }

        /// <summary>Decodes the cut-out mask off the UI thread, once per file the picture is handed.</summary>
        private void EnsureCutOutDecoded(string file)
        {
            if (_rendering.CutOutMask is not null || string.Equals(file, _cutOutFile, StringComparison.OrdinalIgnoreCase)) return;
            _cutOutFile = file;
            _cutOutCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_display._lifetime);
            _cutOutCts = cts;
            var rendering = _rendering;

            _ = Task.Run(() =>
            {
                SKImage mask;
                try
                {
                    mask = CutOutMasks.Decode(file);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    if (!cts.IsCancellationRequested)
                        _ = _display._runOnUi(() =>
                        {
                            if (ReferenceEquals(_cutOutCts, cts)) _cutOutFile = null;
                            if (_display.Shows(rendering)) _display.CutOutDecodeFailed?.Invoke(rendering);
                        });
                    return;
                }
                if (cts.IsCancellationRequested) { mask.Dispose(); return; }

                rendering.AttachCutOut(mask, file);
                _ = _display._runOnUi(() =>
                {
                    if (ReferenceEquals(_cutOutCts, cts)) _cutOutFile = null;
                    if (_display.Shows(rendering)) _display.Decoded?.Invoke(rendering);
                });
            }, cts.Token);
        }

        /// <summary>Reads the scene depth off the UI thread, once per file the picture is handed — a plane under a megabyte, read
        /// whole whatever size the picture is shown at.</summary>
        private void EnsureSceneDepthRead(string file)
        {
            if (_rendering.SceneDepth is not null || string.Equals(file, _sceneDepthFile, StringComparison.OrdinalIgnoreCase)) return;
            _sceneDepthFile = file;
            _sceneDepthCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_display._lifetime);
            _sceneDepthCts = cts;
            var rendering = _rendering;

            _ = Task.Run(() =>
            {
                SceneDepthPlane plane;
                try
                {
                    plane = SceneDepthMaps.Read(file);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    if (!cts.IsCancellationRequested)
                        _ = _display._runOnUi(() =>
                        {
                            if (ReferenceEquals(_sceneDepthCts, cts)) _sceneDepthFile = null;
                            if (_display.Shows(rendering)) _display.SceneDepthReadFailed?.Invoke(rendering);
                        });
                    return;
                }
                if (cts.IsCancellationRequested) return;

                rendering.AttachSceneDepth(plane, file);
                _ = _display._runOnUi(() =>
                {
                    if (ReferenceEquals(_sceneDepthCts, cts)) _sceneDepthFile = null;
                    if (_display.Shows(rendering)) _display.Decoded?.Invoke(rendering);
                });
            }, cts.Token);
        }

        /// <summary>
        /// A picture the tools made of the picture — its restoration, or the picture as erased — decoded beside it, as sharp as
        /// the picture's own decode calls for: <c>scale</c> times its long edge, the full picture once it is decoded in full.
        /// </summary>
        private sealed class MadeDecode(PictureDecodes owner, Func<PictureRendering, DecodedImage?> decoded,
                                        Action<PictureRendering, DecodedImage, string> attach, Action<PictureRendering> failed)
        {
            // Its file, the long edge asked of it, and whether it is still on its way.
            private CancellationTokenSource? _cts;
            private string? _file;
            private int _edge;
            private bool _underway;

            public void Cancel() => _cts?.Cancel();

            public void Ensure(string file, DecodedImage proxy, int scale)
            {
                var rendering = owner._rendering;
                var display = owner._display;
                int edge = proxy.IsFullResolution ? int.MaxValue : Math.Max(proxy.Pixels.Width, proxy.Pixels.Height) * scale;
                // Decoded at this size already, or on its way: the rendering let go of a decode only when it was handed another file.
                if (string.Equals(file, _file, StringComparison.OrdinalIgnoreCase) && _edge >= edge && (_underway || decoded(rendering) is not null)) return;
                _file = file;
                _edge = edge;
                _underway = true;
                _cts?.Cancel();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(display._lifetime);
                _cts = cts;
                var sourceSize = proxy.SourceSize;

                _ = Task.Run(() =>
                {
                    DecodedImage picture;
                    try
                    {
                        picture = display._editing.DecodeRestored(file, sourceSize, edge == int.MaxValue ? null : edge);
                    }
                    catch (Exception ex) when (ex is ImageDecodeException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                    {
                        if (!cts.IsCancellationRequested)
                            _ = display._runOnUi(() =>
                            {
                                if (!display.Shows(rendering)) return;
                                _file = null;
                                failed(rendering);
                            });
                        return;
                    }
                    if (cts.IsCancellationRequested) { picture.Dispose(); return; }

                    attach(rendering, picture, file);
                    _ = display._runOnUi(() =>
                    {
                        if (ReferenceEquals(_cts, cts)) _underway = false;
                        if (display.Shows(rendering)) display.Decoded?.Invoke(rendering);
                    });
                }, cts.Token);
            }
        }
    }
}
