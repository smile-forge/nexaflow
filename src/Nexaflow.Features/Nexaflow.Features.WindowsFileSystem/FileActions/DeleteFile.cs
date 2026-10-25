using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Deletes selected files/folders.
/// <list type="bullet">
///   <item>Normal delete — shows a confirmation overlay, then moves items to the Recycle Bin.</item>
///   <item>Force delete (Shift held) — skips confirmation and permanently deletes immediately.</item>
/// </list>
/// </summary>
public class DeleteFile : IFileAction, IFolderAction, ICacheable
{
    private readonly IShellServices _shell;

    public DeleteFile(IShellServices shell) => _shell = shell;

    // ── IFileAction ───────────────────────────────────────────────────────────

    public bool   IsDestructive          => true;
    public bool   SupportsMultipleFiles  => true;
    public string Icon                   => "🗑";
    public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Delete");
    public static string? StaticExperienceId => "/";
    public string ExperienceId           => "/";
    public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
    public bool   RequiresRefresh        => false;  // refresh triggered inside callbacks
    public bool   CanPerformAction       => true;

    // ── IFolderAction ─────────────────────────────────────────────────────────

    bool   IFolderAction.IsDestructive        => true;
    bool   IFolderAction.SupportsMultipleFiles => true;
    string IFolderAction.Icon                 => "🗑";
    string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Delete");
    bool   IFolderAction.RequiresRefresh       => false;
    bool   IFolderAction.CanPerformAction      => true;
    public bool   AppliesToRoot               => false;
    public bool   AppliesToDrives             => false;

    // ── Single path ───────────────────────────────────────────────────────────

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync(filePath, force: false, ct);

    public Task<bool> PerformActionAsync(string filePath, bool force, CancellationToken ct = default) => PerformActionAsync(new[] { filePath }, force, ct);

    // ── Multiple paths ────────────────────────────────────────────────────────

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default) => PerformActionAsync(filePaths, force: false, ct);

    /// <summary>
    /// Queues the delete rather than doing it here. Emptying a large tree used to block the UI thread
    /// inside <c>SHFileOperation</c> with <c>FOF_SILENT</c> set, so the window simply stopped responding
    /// with nothing on screen to say why. Recycling still goes through shell32 — only it produces a
    /// Recycle Bin entry — but on a background thread, with a row in the operations panel.
    /// </summary>
    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, bool force, CancellationToken ct = default)
    {
        // Delete what the row points at, not a temp copy of it — see ShellPath.RealForMutation.
        var paths = new List<string>(Services.ShellPath.RealForMutation(filePaths));
        if (paths.Count == 0) return Task.FromResult(false);

        var queue = Operations.FileOperationQueue.For(_shell);

        if (force)
        {
            queue.EnqueueDelete(paths, permanent: true);
            return Task.FromResult(true);
        }

        string message = paths.Count == 1
            ? Str.Format("WindowsFileSystem.Delete.ConfirmOneFormat", Path.GetFileName(paths[0]))
            : Str.Format("WindowsFileSystem.Delete.ConfirmManyFormat", paths.Count);

        _shell.ShowConfirmation(
            title:     Str.Get("WindowsFileSystem.Delete.ConfirmTitle"),
            message:   message,
            onConfirm: () => queue.EnqueueDelete(paths, permanent: false),
            onCancel:  () => { });

        return Task.FromResult(false);   // the actual delete is deferred to the confirm callback
    }
}
