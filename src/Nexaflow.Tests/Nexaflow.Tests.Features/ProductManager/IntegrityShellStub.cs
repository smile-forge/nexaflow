using System.Collections.Generic;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// <see cref="IShellServices"/> double for driving <c>ProductIntegrityViewModel</c> headlessly. The one
/// behaviour that matters: <see cref="QueueBackgroundTask"/> runs the task <b>synchronously</b> on the calling
/// thread, so a scan completes before the constructor returns and the test never has to poll. Everything the
/// VM touches is recorded; the rest throws so a stray call fails loudly.
/// </summary>
internal sealed class IntegrityShellStub : IShellServices
{
    public List<string> Notifications { get; } = [];
    public string? LastError { get; private set; }
    public List<(string Kind, Dictionary<string, string>? Params, bool RightPane)> Opened { get; } = [];

    public void QueueBackgroundTask(IBackgroundTask task, Action<bool>? onComplete = null, CancellationToken ct = default)
    {
        var ok = true;
        try { task.RunAsync(ct).GetAwaiter().GetResult(); }
        catch { ok = false; }
        onComplete?.Invoke(ok);
    }

    public IFileWatch WatchFile(string path, Action onChanged) => new NoopWatch();

    public void ShowNotification(string message) => Notifications.Add(message);
    public void ShowNotification(string message, Page tab) => Notifications.Add(message);
    public void ShowError(string message) => LastError = message;

    public void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null, IPageView? caller = null, bool inRightPane = false)
        => Opened.Add((pageKind, pageParams, inRightPane));

    private sealed class NoopWatch : IFileWatch
    {
        public bool Enabled { get; set; } = true;
        public void Dispose() { }
    }

    // ── Unused surface ──
    public IDisposable RegisterMediatedTask(MediatedTaskRegistration registration) => throw new NotSupportedException();
    public void CloseTab(Page tab) => throw new NotSupportedException();
    public IReadOnlyList<Page> GetContextItemPages() => throw new NotSupportedException();
    public IReadOnlyList<QuickOpenTarget> GetQuickOpenTargets() => throw new NotSupportedException();
    public Task RunOnUiAsync(Action action) => throw new NotSupportedException();
    public Task<T> RunOnUiAsync<T>(Func<Task<T>> action) => throw new NotSupportedException();
    public Page? FindTab(string pageKind, Dictionary<string, string>? pageParams = null) => throw new NotSupportedException();
    public void InsertChatInput(string text) => throw new NotSupportedException();
    public void SubmitAiQuery(string query) => throw new NotSupportedException();
    public void ShowPrompt(string title, string label, string initialValue, Action<string> onConfirm, Action onCancel) => throw new NotSupportedException();
    public void ShowConfirmation(string title, string message, Action onConfirm, Action onCancel) => throw new NotSupportedException();
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ConfirmAsync(string title, string message, string? confirmLabel, string? cancelLabel, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions = null, string? initialPath = null) => throw new NotSupportedException();
    public Task<string?> PickFolderAsync(string? initialPath = null) => throw new NotSupportedException();
    public Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions = null, string? initialPath = null) => throw new NotSupportedException();
    public void RequestRefresh() => throw new NotSupportedException();
    public void MoveFolderInBackground(string sourcePath, string destinationPath, string busyMessage, Action<bool>? onComplete = null) => throw new NotSupportedException();
    public event Action? FolderBusyChanged { add { } remove { } }
    public IDisposable MarkFolderBusy(string folderPath, string message) => throw new NotSupportedException();
    public string? GetFolderBusyMessage(string folderPath) => throw new NotSupportedException();
    public void SaveFeatureConfig(IFeatureConfig config) => throw new NotSupportedException();
    public void OpenOptions(string configName) => throw new NotSupportedException();
    public void OpenWorkspaceConfig(string configName) => throw new NotSupportedException();
    public void ShowOverlay(object overlayViewModel) => throw new NotSupportedException();
    public void CloseOverlay() => throw new NotSupportedException();
    public void PinToRibbon(string format, object payload) => throw new NotSupportedException();
    public bool HandleObject(object obj) => throw new NotSupportedException();
    public IEnumerable<Type> DiscoverImplementations<TInterface>() => throw new NotSupportedException();
    public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
}
