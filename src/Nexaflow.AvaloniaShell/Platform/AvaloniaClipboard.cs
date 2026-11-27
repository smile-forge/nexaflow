using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Nexaflow.Interaction;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// The session's clipboard on Avalonia: text and files, the kinds Avalonia's clipboard names on every platform. What it holds is
/// read again when Windows says it changed (a clipboard-format listener on a <see cref="MessageWindow"/>, as Nexaflow.Core listens).
/// <para>
/// Avalonia reaches the clipboard through a window (<see cref="TopLevel.Clipboard"/>), where the session's clipboard is the
/// process's, made before any window: until one is open it holds nothing and writes nothing. The kinds Nexaflow.Core also reads —
/// <see cref="DragKinds.FilesCut"/>, <see cref="DragKinds.FileContents"/>, <see cref="DragKinds.Html"/>, <see cref="DragKinds.Image"/> —
/// are Windows formats its WPF payload (<c>WindowsDragPayload</c>, in Visuals.Common) decodes; they are not read here.
/// </para>
/// </summary>
internal sealed class AvaloniaClipboard : IShellClipboard, IDisposable
{
    private const int ClipboardUpdate = 0x031D;   // WM_CLIPBOARDUPDATE

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private readonly IUiThread _ui;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MessageWindow _listener;
    private IReadOnlySet<string> _kinds = Nothing;

    public AvaloniaClipboard(IUiThread ui, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _ui = ui;
        _desktop = desktop;
        _listener = new MessageWindow("Nexaflow clipboard", OnMessage);
        AddClipboardFormatListener(_listener.Handle);
    }

    public IReadOnlySet<string> Kinds => _kinds;

    public event EventHandler? KindsChanged;

    public Task<IDragPayload> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct = default)
        => _ui.RunAsync<IDragPayload>(async () =>
        {
            if (Clipboard() is not { } clipboard) return ComposedPayload.Empty;

            var payload = ComposedPayload.Empty;
            if (kinds.Contains(DragKinds.Files) && await clipboard.TryGetFilesAsync() is { Length: > 0 } items)
                payload = ComposedPayload.OfFiles([.. items.Select(item => item.TryGetLocalPath()).OfType<string>()], cut: false);
            if (kinds.Contains(DragKinds.Text) && await clipboard.TryGetTextAsync() is { } text)
                payload = payload.With(DragKinds.Text, text);
            return payload;
        });

    public Task<bool> WriteAsync(IDragPayload payload, CancellationToken ct = default)
        => _ui.RunAsync(async () =>
        {
            if (Clipboard() is not { } clipboard || TopLevelOpen() is not { } top) return false;

            var data = new DataTransfer();
            if (payload.Read(DragKinds.Files) is IEnumerable<string> paths)
                foreach (var path in paths)
                    if (await top.StorageProvider.TryGetFileFromPathAsync(path) is { } file)
                        data.Add(DataTransferItem.Create(DataFormat.File, file));
            if (payload.Read(DragKinds.Text) is string text)
                data.Add(DataTransferItem.Create(DataFormat.Text, text));

            await clipboard.SetDataAsync(data);
            Hold(KindsOf(payload));
            return true;
        });

    public Task<bool> ClearAsync(CancellationToken ct = default)
        => _ui.RunAsync(async () =>
        {
            if (Clipboard() is not { } clipboard) return false;
            await clipboard.ClearAsync();
            Hold(Nothing);
            return true;
        });

    public void Dispose()
    {
        RemoveClipboardFormatListener(_listener.Handle);
        _listener.Dispose();
    }

    private TopLevel? TopLevelOpen() => _desktop.Windows.FirstOrDefault();

    private IClipboard? Clipboard() => TopLevelOpen()?.Clipboard;

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != ClipboardUpdate) return false;
        _ = RefreshAsync();
        return true;
    }

    /// <summary>Reads which kinds the clipboard holds now, through a window while one is open.</summary>
    private async Task RefreshAsync()
    {
        if (Clipboard() is not { } clipboard) return;
        var formats = await clipboard.GetDataFormatsAsync();
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        if (formats.Contains(DataFormat.Text)) kinds.Add(DragKinds.Text);
        if (formats.Contains(DataFormat.File)) kinds.Add(DragKinds.Files);
        Hold(kinds);
    }

    private static IReadOnlySet<string> KindsOf(IDragPayload payload)
        => new[] { DragKinds.Files, DragKinds.Text }.Where(payload.Contains).ToHashSet(StringComparer.Ordinal);

    /// <summary>On the UI thread: what it holds is <paramref name="kinds"/>, told if that is news.</summary>
    private void Hold(IReadOnlySet<string> kinds)
    {
        if (_kinds.SetEquals(kinds)) return;
        _kinds = kinds;
        KindsChanged?.Invoke(this, EventArgs.Empty);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr window);
}
