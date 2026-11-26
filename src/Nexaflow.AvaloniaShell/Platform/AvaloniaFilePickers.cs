using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nexaflow.AvaloniaShell.Shell;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// The shell's pickers on Avalonia: the window's <see cref="IStorageProvider"/>.
/// <para>
/// <see cref="IFilePickers"/> answers synchronously, as WPF's modal dialogs do, where Avalonia's pickers are asynchronous only. Each
/// pick is waited out in a nested dispatcher frame, so the UI thread keeps pumping while it waits — the session's
/// <c>ShellServices.Pick…Async</c> already hands the call to the UI thread, so the port could be asynchronous and this frame go.
/// </para>
/// </summary>
internal sealed class AvaloniaFilePickers(IClassicDesktopStyleApplicationLifetime desktop) : IFilePickers
{
    public string? PickOpenFile(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => PickOpen(owner, extensions, initialPath, many: false).FirstOrDefault();

    public IReadOnlyList<string> PickOpenFiles(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath)
        => PickOpen(owner, extensions, initialPath, many: true);

    public string? PickFolder(IWindowHost? owner, WorkspaceRuntime workspace, string? initialPath)
    {
        if (StorageOf(owner) is not { } storage) return null;
        var picked = Wait(async () => await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            SuggestedStartLocation = await FolderAsync(storage, initialPath),
        }));
        return picked.Select(folder => folder.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }

    public string? PickSaveFile(IWindowHost? owner, WorkspaceRuntime workspace, string defaultFileName, IReadOnlyList<SaveFileType> types,
                                string? initialPath, long bytes)
    {
        if (StorageOf(owner) is not { } storage) return null;
        var picked = Wait(async () => await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName      = defaultFileName,
            SuggestedStartLocation = await FolderAsync(storage, initialPath),
            FileTypeChoices        = [.. types.Select(type => new FilePickerFileType(type.Name) { Patterns = [$"*{type.Extension}"] })],
        }));
        return picked?.TryGetLocalPath();
    }

    private IReadOnlyList<string> PickOpen(IWindowHost? owner, IReadOnlyList<string>? extensions, string? initialPath, bool many)
    {
        if (StorageOf(owner) is not { } storage) return [];
        var picked = Wait(async () => await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple          = many,
            SuggestedStartLocation = await FolderAsync(storage, initialPath),
            FileTypeFilter         = extensions is { Count: > 0 }
                ? [new FilePickerFileType(string.Join(", ", extensions)) { Patterns = [.. extensions.Select(ext => $"*{ext}")] }]
                : null,
        }));
        return [.. picked.Select(file => file.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>The storage of the window asking, or of the first open one.</summary>
    private IStorageProvider? StorageOf(IWindowHost? owner)
        => ((owner as ShellWindowModel)?.Window ?? (TopLevel?)desktop.Windows.FirstOrDefault())?.StorageProvider;

    private static async Task<IStorageFolder?> FolderAsync(IStorageProvider storage, string? path)
        => string.IsNullOrEmpty(path) ? null : await storage.TryGetFolderFromPathAsync(path);

    /// <summary>Waits for <paramref name="pick"/> on the UI thread, pumping it meanwhile.</summary>
    private static T Wait<T>(Func<Task<T>> pick)
    {
        var task = pick();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
}
