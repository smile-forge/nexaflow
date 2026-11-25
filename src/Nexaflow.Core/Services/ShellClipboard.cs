using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's <see cref="IShellClipboard"/> on WPF: the Windows clipboard, read through <see cref="WindowsDragPayload"/> and
/// written through <see cref="WindowsDataObject"/>, on the UI thread its clipboard calls must run on.
/// <para>
/// What it holds is read again whenever Windows says it changed — a clipboard-format listener on a message-only window — and
/// when the app is activated, so <see cref="Kinds"/> never opens it. A clipboard another application holds open is tried again
/// a few times, a moment apart, before a read gives nothing or a write says it failed.
/// </para>
/// </summary>
internal sealed class ShellClipboard : IShellClipboard, IDisposable
{
    private const int ClipboardUpdate = 0x031D;   // WM_CLIPBOARDUPDATE

    /// <summary>How long to wait before each further try at a clipboard another application holds open.</summary>
    private static readonly TimeSpan[] Retries =
        [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(300)];

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);
    private static readonly string[] Announced = [.. DragKinds.Shared, DragKinds.FilesCut];

    private readonly Application _app;
    private readonly HwndSource _listener;
    private IReadOnlySet<string> _kinds = Nothing;

    /// <summary>Listens for the clipboard's changes on <paramref name="app"/>'s UI thread; made there.</summary>
    public ShellClipboard(Application app)
    {
        _app = app;
        _listener = new HwndSource(new HwndSourceParameters("Nexaflow clipboard") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _listener.AddHook(OnMessage);
        AddClipboardFormatListener(_listener.Handle);
        app.Activated += OnActivated;
        _ = RefreshAsync();
    }

    public IReadOnlySet<string> Kinds => _kinds;

    public event EventHandler? KindsChanged;

    public Task<IDragPayload> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct = default)
        => OnClipboardAsync<IDragPayload>(
            () => Clipboard.GetDataObject() is { } data ? new WindowsDragPayload(data).Take(kinds, DropStaging.Default) : ComposedPayload.Empty,
            ComposedPayload.Empty, ct);

    public Task<bool> WriteAsync(IDragPayload payload, CancellationToken ct = default)
        => OnClipboardAsync(() =>
        {
            Clipboard.SetDataObject(WindowsDataObject.From(payload), copy: true);
            Hold(KindsOf(payload));
            return true;
        }, false, ct);

    public Task<bool> ClearAsync(CancellationToken ct = default)
        => OnClipboardAsync(() =>
        {
            Clipboard.Clear();
            Hold(Nothing);
            return true;
        }, false, ct);

    public void Dispose()
    {
        _app.Activated -= OnActivated;
        RemoveClipboardFormatListener(_listener.Handle);
        _listener.RemoveHook(OnMessage);
        _listener.Dispose();
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClipboardUpdate) _ = RefreshAsync();
        return IntPtr.Zero;
    }

    private void OnActivated(object? sender, EventArgs e) => _ = RefreshAsync();

    /// <summary>Reads what the clipboard holds now; a clipboard that stays busy keeps what was last known.</summary>
    private Task<bool> RefreshAsync()
        => OnClipboardAsync(() =>
        {
            Hold(Clipboard.GetDataObject() is { } data ? KindsOf(new WindowsDragPayload(data)) : Nothing);
            return true;
        }, false, CancellationToken.None);

    private static IReadOnlySet<string> KindsOf(IDragPayload payload)
        => Announced.Where(payload.Contains).ToHashSet(StringComparer.Ordinal);

    /// <summary>On the UI thread: what it holds is <paramref name="kinds"/>, told if that is news.</summary>
    private void Hold(IReadOnlySet<string> kinds)
    {
        if (_kinds.SetEquals(kinds)) return;
        _kinds = kinds;
        KindsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs <paramref name="use"/> on the UI thread, again a moment later while another application holds the clipboard open,
    /// and <paramref name="busy"/> once it has held it open throughout.
    /// </summary>
    private async Task<T> OnClipboardAsync<T>(Func<T> use, T busy, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (done, value) = await _app.Dispatcher.InvokeAsync(() =>
            {
                try { return (true, use()); }
                catch (ExternalException) { return (false, busy); }
            }, DispatcherPriority.Normal, ct);

            if (done) return value;
            if (attempt == Retries.Length) return busy;
            await Task.Delay(Retries[attempt], ct);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr window);
}
