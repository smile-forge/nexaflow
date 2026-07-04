using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp;
using VlcMediaPlayer = LibVLCSharp.MediaPlayer;

namespace Nexaflow.Features.Video.Rendering;

/// <summary>
/// Renders libvlc video frames into a WPF <see cref="WriteableBitmap"/> via the smem video callbacks,
/// instead of embedding a native window. This removes the WPF "airspace" problem entirely: the video is a
/// plain WPF image, so overlay controls, click handling, fullscreen and multiple simultaneous views all
/// behave like ordinary WPF.
/// <para>
/// libvlc decodes into one of two native buffers (double-buffered, swapped on display) off the UI thread;
/// <see cref="Blit"/> copies the latest completed frame into the bitmap's back buffer and is driven on the
/// UI thread by the view's per-frame render tick. The bitmap itself is only ever touched on the UI thread.
/// </para>
/// </summary>
internal sealed class VlcVideoSink : IDisposable
{
    private readonly Func<Action, Task> _runOnUi;
    private readonly Action<WriteableBitmap> _onBitmapReady;

    // Delegates are held in fields so the GC can't collect them while libvlc holds the native pointers.
    private readonly VlcMediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly VlcMediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    private readonly VlcMediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly VlcMediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly VlcMediaPlayer.LibVLCVideoDisplayCb _displayCb;

    private readonly object _sync = new();
    private IntPtr _bufWrite, _bufRead;
    private int _width, _height, _stride, _size;
    private bool _frameReady;
    private bool _disposed;
    private bool _hasOutput;
    private WriteableBitmap? _bitmap;

    // Signalled when libvlc has torn down the video output (Cleanup ran) — the point after which no decode
    // thread touches the smem buffers again. Runs continuations off the signalling (native) thread.
    private readonly TaskCompletionSource _outputStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True once libvlc has allocated output buffers (a video output exists) and not yet torn it
    /// down. The owner uses this to decide whether teardown must wait for <see cref="OutputStopped"/>.</summary>
    public bool HasOutput { get { lock (_sync) return _hasOutput; } }

    /// <summary>Completes when libvlc's video output has been destroyed (its <see cref="Cleanup"/> callback
    /// ran) — i.e. no decode thread will write into the smem buffers again — or on <see cref="Dispose"/> when
    /// no output ever existed. The owner MUST await this before releasing the player and this sink; libvlc 4's
    /// <c>Stop()</c> is asynchronous, so freeing the buffers/delegates without this barrier races the decode
    /// thread and corrupts the heap.</summary>
    public Task OutputStopped => _outputStopped.Task;

    public VlcVideoSink(VlcMediaPlayer mp, Func<Action, Task> runOnUi, Action<WriteableBitmap> onBitmapReady)
    {
        _runOnUi       = runOnUi;
        _onBitmapReady = onBitmapReady;

        _formatCb  = Format;
        _cleanupCb = Cleanup;
        _lockCb    = Lock;
        _unlockCb  = Unlock;
        _displayCb = Display;

        mp.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        mp.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    // ── libvlc callbacks (on decode threads) ────────────────────────────────

    private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
                        ref uint pitches, ref uint lines)
    {
        int w = (int)width, h = (int)height;
        int stride = w * 4;

        // Force BGRA (matches WPF PixelFormats.Bgra32 byte order).
        Marshal.Copy([(byte)'B', (byte)'G', (byte)'R', (byte)'A'], 0, chroma, 4);

        lock (_sync)
        {
            FreeBuffers();
            if (_disposed) return 0;
            _width  = w;
            _height = h;
            _stride = stride;
            _size   = stride * h;
            _bufWrite  = Marshal.AllocHGlobal(_size);
            _bufRead   = Marshal.AllocHGlobal(_size);
            _frameReady = false;
            _hasOutput  = true;
        }

        pitches = (uint)stride;
        lines   = (uint)h;

        // The bitmap must be created on the UI thread; the view binds to it once ready.
        _ = _runOnUi(() =>
        {
            if (_disposed) return;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            lock (_sync) { _bitmap = bmp; }
            _onBitmapReady(bmp);
        });

        return 1;   // one plane
    }

    private void Cleanup(ref IntPtr opaque)
    {
        lock (_sync) { FreeBuffers(); _hasOutput = false; }
        _outputStopped.TrySetResult();   // vout gone → owner may now safely release the player and this sink
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        lock (_sync)
        {
            Marshal.WriteIntPtr(planes, 0, _bufWrite);
            return _bufWrite;
        }
    }

    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) { }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        lock (_sync)
        {
            (_bufWrite, _bufRead) = (_bufRead, _bufWrite);   // the just-filled buffer becomes readable
            _frameReady = true;
        }
    }

    // ── UI thread: copy the latest frame into the bitmap ────────────────────

    /// <summary>Copies the most recent completed frame into the bitmap. Call on the UI thread once per
    /// render tick; cheap and a no-op when no new frame has arrived.</summary>
    public unsafe void Blit()
    {
        WriteableBitmap? bmp;
        lock (_sync)
        {
            if (!_frameReady || _bitmap is null || _bufRead == IntPtr.Zero) return;
            bmp = _bitmap;
            if (bmp.PixelWidth != _width || bmp.PixelHeight != _height) return;   // size changed mid-flight

            try
            {
                bmp.Lock();
                int dstStride = bmp.BackBufferStride;
                byte* src = (byte*)_bufRead;
                byte* dst = (byte*)bmp.BackBuffer;
                if (dstStride == _stride)
                {
                    Buffer.MemoryCopy(src, dst, (long)dstStride * _height, (long)_size);
                }
                else
                {
                    int row = Math.Min(_stride, dstStride);
                    for (int y = 0; y < _height; y++)
                        Buffer.MemoryCopy(src + y * _stride, dst + y * dstStride, dstStride, row);
                }
                bmp.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            }
            finally { bmp.Unlock(); }

            _frameReady = false;
        }
    }

    private void FreeBuffers()
    {
        if (_bufWrite != IntPtr.Zero) Marshal.FreeHGlobal(_bufWrite);
        if (_bufRead  != IntPtr.Zero) Marshal.FreeHGlobal(_bufRead);
        _bufWrite = _bufRead = IntPtr.Zero;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            FreeBuffers();   // safe: the owner disposes this only after OutputStopped, so Cleanup already freed
            _bitmap = null;
            _hasOutput = false;
        }
        _outputStopped.TrySetResult();   // unblock a waiter even if no output was ever created
    }
}
