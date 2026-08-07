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

    public bool PerformAction(IEnumerable<string> filePaths, bool force)
    {
        // Delete what the row points at, not a temp copy of it — see ShellPath.RealForMutation.
        var paths = new List<string>(Services.ShellPath.RealForMutation(filePaths));
        if (paths.Count == 0) return false;

        if (force)
        {
            bool ok = NativeMethods.DeleteFilesPermanently(paths);
            _shell.RequestRefresh();
            return ok;
        }

        string target = paths.Count == 1
            ? $"\"{Path.GetFileName(paths[0])}\""
            : $"{paths.Count} items";

        _shell.ShowConfirmation(
            title:     "Move to Recycle Bin?",
            message:   $"Send {target} to the Recycle Bin?",
            onConfirm: () =>
            {
                NativeMethods.RecycleFiles(paths);
                _shell.RequestRefresh();
            },
            onCancel: () => { });

        return false;   // actual delete is deferred to the confirm callback
    }
}
