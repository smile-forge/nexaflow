using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class RenameFile : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;

        public RenameFile(IShellServices shell) => _shell = shell;

        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;   // rename only makes sense for one item
        public string Icon                   => "✏";
        public string DisplayName            => "Rename";
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => false;   // refresh is triggered by the confirm callback
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => false;
        string IFolderAction.Icon                 => "✏";
        string IFolderAction.DisplayName          => "Rename";
        bool   IFolderAction.RequiresRefresh       => false;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string path)
        {
            bool isDir     = Directory.Exists(path);
            string dir     = Path.GetDirectoryName(path)!;
            string oldName = Path.GetFileName(path);
            string title   = isDir ? "Rename Folder" : "Rename File";

            _shell.ShowPrompt(
                title:        title,
                label:        "New name:",
                initialValue: oldName,
                onConfirm: newName =>
                {
                    newName = newName.Trim();
                    if (string.IsNullOrEmpty(newName) || newName == oldName) return;

                    string dest = Path.Combine(dir, newName);
                    if (isDir)
                        Directory.Move(path, dest);
                    else
                        File.Move(path, dest);

                    _shell.RequestRefresh();
                },
                onCancel: () => { });

            return false;   // action is async — refresh handled by confirm callback
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // SupportsMultipleFiles = false so this overload is never called,
            // but implement it defensively by renaming the first item.
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
