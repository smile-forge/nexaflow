using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
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
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Rename");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => false;   // refresh is triggered by the confirm callback
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => false;
        string IFolderAction.Icon                 => "✏";
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Rename");
        bool   IFolderAction.RequiresRefresh       => false;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string path)
        {
            path           = Services.ShellPath.RealForMutation(path);
            bool isDir     = Directory.Exists(path);
            string dir     = Path.GetDirectoryName(path)!;
            string oldName = Path.GetFileName(path);
            string title   = isDir ? Str.Get("WindowsFileSystem.Rename.FolderTitle") : Str.Get("WindowsFileSystem.Rename.FileTitle");

            _shell.ShowPrompt(
                title:        title,
                label:        Str.Get("WindowsFileSystem.Rename.Label"),
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
