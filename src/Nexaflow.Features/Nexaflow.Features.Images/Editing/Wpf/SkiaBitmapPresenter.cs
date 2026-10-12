using SkiaSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Wpf;

/// <summary>
/// Puts Skia renders on screen. Renders run off the UI thread, latest-wins — while one is in flight, further
/// requests replace each other, so a slider drag renders as often as the machine allows and never queues behind
/// itself — and each finished frame is written into one reused <see cref="WriteableBitmap"/>.
/// </summary>
/// <remarks>
/// A frame is copied out of Skia as 8-bit premultiplied sRGB — what WPF composes — into a staging buffer on the
/// render thread, then into the bitmap on the UI thread. The bitmap is replaced only when the frame's size or
/// density changes. The presenter reaches the UI thread only through the delegate it is given, so it never touches
/// a dispatcher itself.
/// </remarks>
public sealed class SkiaBitmapPresenter : IDisposable
{
    private readonly Func<Action, Task> _runOnUi;
    private readonly Lock _gate = new();
    private Frame? _pending;
    private bool _running;
    private bool _disposed;
    private SKBitmap? _staging;

    /// <param name="runOnUi">Runs an action on the UI thread.</param>
    public SkiaBitmapPresenter(Func<Action, Task> runOnUi) => _runOnUi = runOnUi;

    /// <summary>The bitmap frames land in; replaced (and <see cref="BitmapChanged"/> raised) when the size changes.</summary>
    public WriteableBitmap? Bitmap { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Bitmap"/> is a new object, or none.</summary>
    public event Action? BitmapChanged;

    /// <summary>Raised on the UI thread after each frame lands, with the tag it was asked for with.</summary>
    public event Action<object?>? Presented;

    /// <summary>
    /// Asks for a frame. <paramref name="render"/> runs off the UI thread, and hands its picture to the callback it is
    /// given while that picture is valid; it returns false when there is nothing to show yet.
    /// </summary>
    /// <param name="dpi">The bitmap's density, so a frame rendered at more pixels keeps its layout size.</param>
    /// <param name="tag">Handed back with <see cref="Presented"/> — what the frame is a picture of, for whoever draws over it.</param>
    public void Present(Func<Action<SKImage>, bool> render, double dpi = 96, object? tag = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = new Frame(render, dpi, tag);
            if (_running) return;
            _running = true;
        }
        _ = Task.Run(RenderLoopAsync);
    }

    /// <summary>Shows nothing: the bitmap is dropped (on the UI thread, by the caller).</summary>
    public void Clear()
    {
        lock (_gate) _pending = null;
        if (Bitmap is null) return;
        Bitmap = null;
        BitmapChanged?.Invoke();
    }

    private async Task RenderLoopAsync()
    {
        while (true)
        {
            Frame frame;
            lock (_gate)
            {
                if (_pending is null || _disposed)
                {
                    _running = false;
                    if (_disposed) ReleaseStaging();
                    return;
                }
                frame = _pending.Value;
                _pending = null;
            }

            bool rendered;
            try
            {
                rendered = frame.Render(CopyToStaging);
            }
            catch (InvalidOperationException)
            {
                // A frame Skia could not allocate or draw: the last one stays on screen, and the next request tries again.
                rendered = false;
            }
            if (!rendered) continue;

            try
            {
                await _runOnUi(() => Blit(frame.Dpi, frame.Tag));
            }
            catch (InvalidOperationException)
            {
                // A frame the screen could not take is skipped like one Skia could not draw: the loop must outlive
                // it, or no later frame would ever be shown.
            }
        }
    }

    private void CopyToStaging(SKImage picture)
    {
        var info = new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        if (_staging is null || _staging.Width != info.Width || _staging.Height != info.Height)
        {
            _staging?.Dispose();
            _staging = new SKBitmap(info);
        }
        if (!picture.ReadPixels(_staging.Info, _staging.GetPixels(), _staging.RowBytes, 0, 0))
            throw new InvalidOperationException("The rendered picture could not be read back.");
    }

    private void Blit(double dpi, object? tag)
    {
        if (_disposed || _staging is not { } staging) return;
        if (Bitmap is null || Bitmap.PixelWidth != staging.Width || Bitmap.PixelHeight != staging.Height || Math.Abs(Bitmap.DpiX - dpi) > 0.01)
        {
            Bitmap = new WriteableBitmap(staging.Width, staging.Height, dpi, dpi, PixelFormats.Pbgra32, null);
            BitmapChanged?.Invoke();
        }
        Bitmap.WritePixels(new Int32Rect(0, 0, staging.Width, staging.Height), staging.GetPixels(), staging.ByteCount, staging.RowBytes);
        Presented?.Invoke(tag);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = null;
            // A running loop may be mid-copy into the staging buffer; it releases the buffer itself on the way out.
            if (!_running) ReleaseStaging();
        }
    }

    private void ReleaseStaging()
    {
        _staging?.Dispose();
        _staging = null;
    }

    private readonly record struct Frame(Func<Action<SKImage>, bool> Render, double Dpi, object? Tag);
}
