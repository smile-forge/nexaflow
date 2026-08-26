using System.Collections.Generic;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// Minimal <see cref="IShellServices"/> test double. Implements only what the Projects list VM exercises
/// (<see cref="MoveFolderInBackground"/> performs the real safe move synchronously; <see cref="ShowError"/>
/// records) — every other member throws, so a test that strays into unimplemented surface fails loudly.
/// </summary>
internal sealed class FakeShellServices : IShellServices
{
    public List<(string Source, string Dest)> Moves { get; } = [];
    public string? LastError { get; private set; }

    public void MoveFolderInBackground(string sourcePath, string destinationPath, string busyMessage, Action<bool>? onComplete = null)
    {
        Moves.Add((sourcePath, destinationPath));
        var ok = true;
        try { Nexaflow.IO.Common.DirectoryMover.MoveAsync(sourcePath, destinationPath).GetAwaiter().GetResult(); }
        catch { ok = false; }
        onComplete?.Invoke(ok);
    }

    public void ShowError(string message) => LastError = message;

    public event Action? FolderBusyChanged { add { } remove { } }

    // ── Unused surface ──
    public void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null, IPageView? caller = null, bool inRightPane = false) => throw new NotSupportedException();
    public void CloseTab(Page tab) => throw new NotSupportedException();
    public IReadOnlyList<Page> GetContextItemPages() => throw new NotSupportedException();
    public IReadOnlyList<Page> GetOpenTabs() => throw new NotSupportedException();
    public IReadOnlyList<QuickOpenTarget> GetQuickOpenTargets() => throw new NotSupportedException();
    public void QueueBackgroundTask(IBackgroundTask task, Action<bool>? onComplete = null, CancellationToken ct = default) => throw new NotSupportedException();
    public IDisposable RegisterMediatedTask(MediatedTaskRegistration registration) => throw new NotSupportedException();
    public IFileWatch WatchFile(string path, Action onChanged) => throw new NotSupportedException();
    public Task RunOnUiAsync(Action action) => throw new NotSupportedException();
    public Task<T> RunOnUiAsync<T>(Func<Task<T>> action) => throw new NotSupportedException();
    public Page? FindTab(string pageKind, Dictionary<string, string>? pageParams = null) => throw new NotSupportedException();
    public void ShowNotification(string message) => throw new NotSupportedException();
    public void ShowNotification(string message, Page tab) => throw new NotSupportedException();
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

    public Nexaflow.Features.Common.Dependencies.ExternalDependencyStatus GetDependencyStatus(string id)
        => Nexaflow.Features.Common.Dependencies.ExternalDependencyStatus.Unknown();
    public Nexaflow.Features.Common.Search.IFileTextExtractor? GetFileTextExtractor(string path) => null;
    public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
}
