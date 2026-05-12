using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WinFileSystem.FileActions;

/// <summary>
/// Deletes selected files/folders.
/// <list type="bullet">
///   <item>Normal delete — shows a confirmation overlay, then moves items to the Recycle Bin.</item>
///   <item>Force delete (Shift held) — skips confirmation and permanently deletes immediately.</item>
/// </list>
/// </summary>
public class DeleteFile : IFileAction
{
    private readonly IInputPromptService _prompt;

    public DeleteFile(IInputPromptService prompt) => _prompt = prompt;

    public bool   IsDestructive        => true;
    public bool   SupportsMultipleFiles => true;
    public string SupportedFileTypes    => "*.*";
    public string Icon                  => "🗑";
    public string DisplayName           => "Delete";
    public bool   AppliesToFolders      => true;
    public string SupportedFolderNames  => "*";
    public bool   AppliesToRoot         => false;
    public bool   AppliesToDrives       => false;
    public bool   RequiresRefresh       => false;  // refresh triggered inside callbacks
    public bool   CanPerformAction      => true;

    // ── Single path ──────────────────────────────────────────────────────

    public bool PerformAction(string filePath)
        => PerformAction(filePath, force: false);

    public bool PerformAction(string filePath, bool force)
        => PerformAction(new[] { filePath }, force);

    // ── Multiple paths ───────────────────────────────────────────────────

    public bool PerformAction(IEnumerable<string> filePaths)
        => PerformAction(filePaths, force: false);

    public bool PerformAction(IEnumerable<string> filePaths, bool force)
    {
        var paths = new List<string>(filePaths);
        if (paths.Count == 0) return false;

        if (force)
        {
            // Shift-held: permanent delete immediately, no confirmation.
            bool ok = NativeMethods.DeleteFilesPermanently(paths);
            _prompt.RequestRefresh();
            return ok;
        }

        // Normal path: ask for confirmation, then recycle.
        string target = paths.Count == 1
            ? $"\"{Path.GetFileName(paths[0])}\""
            : $"{paths.Count} items";

        _prompt.ShowConfirmation(
            title:     "Move to Recycle Bin?",
            message:   $"Send {target} to the Recycle Bin?",
            onConfirm: () =>
            {
                NativeMethods.RecycleFiles(paths);
                _prompt.RequestRefresh();
            },
            onCancel: () => { });

        return false;   // actual delete is deferred to the confirm callback
    }
}
