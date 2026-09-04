using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

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
    public string DisplayName            => "Delete";
    public static string? StaticExperienceId => "/";
    public string ExperienceId           => "/";
    public string ExperienceDescription  => "All files";
    public bool   RequiresRefresh        => false;  // refresh triggered inside callbacks
    public bool   CanPerformAction       => true;

    // ── IFolderAction ─────────────────────────────────────────────────────────

    bool   IFolderAction.IsDestructive        => true;
    bool   IFolderAction.SupportsMultipleFiles => true;
    string IFolderAction.Icon                 => "🗑";
    string IFolderAction.DisplayName          => "Delete";
    bool   IFolderAction.RequiresRefresh       => false;
    bool   IFolderAction.CanPerformAction      => true;
    public bool   AppliesToRoot               => false;
    public bool   AppliesToDrives             => false;

    // ── Single path ───────────────────────────────────────────────────────────

    public bool PerformAction(string filePath)
        => PerformAction(filePath, force: false);

    public bool PerformAction(string filePath, bool force)
        => PerformAction(new[] { filePath }, force);

    // ── Multiple paths ────────────────────────────────────────────────────────

    public bool PerformAction(IEnumerable<string> filePaths)
        => PerformAction(filePaths, force: false);

    /// <summary>
    /// Queues the delete rather than doing it here. Emptying a large tree used to block the UI thread
    /// inside <c>SHFileOperation</c> with <c>FOF_SILENT</c> set, so the window simply stopped responding
    /// with nothing on screen to say why. Recycling still goes through shell32 — only it produces a
    /// Recycle Bin entry — but on a background thread, with a row in the operations panel.
    /// </summary>
    public bool PerformAction(IEnumerable<string> filePaths, bool force)
    {
        // Delete what the row points at, not a temp copy of it — see ShellPath.RealForMutation.
        var paths = new List<string>(Services.ShellPath.RealForMutation(filePaths));
        if (paths.Count == 0) return false;

        var queue = Operations.FileOperationQueue.For(_shell);

        if (force)
        {
            queue.EnqueueDelete(paths, permanent: true);
            return true;
        }

        string target = paths.Count == 1
            ? $"\"{Path.GetFileName(paths[0])}\""
            : $"{paths.Count} items";

        _shell.ShowConfirmation(
            title:     "Move to Recycle Bin?",
            message:   $"Send {target} to the Recycle Bin?",
            onConfirm: () => queue.EnqueueDelete(paths, permanent: false),
            onCancel:  () => { });

        return false;   // the actual delete is deferred to the confirm callback
    }
}
