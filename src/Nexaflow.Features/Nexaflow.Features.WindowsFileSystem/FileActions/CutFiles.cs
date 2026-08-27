using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class CutFiles : IFileAction, IFolderAction, ICacheable
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "✂️";
        public string DisplayName            => "Cut";
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                 => "✂️";
        string IFolderAction.DisplayName          => "Cut";
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath)
        {
            NativeMethods.ClipboardCutFiles(Services.ShellPath.Realize([filePath]));
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // See CopyFiles: an empty file-drop list replaces the user's clipboard, so an empty selection
            // must stop before the clipboard rather than clearing it and reporting success.
            var paths = Services.ShellPath.Realize(filePaths).ToList();
            if (paths.Count == 0) return false;

            NativeMethods.ClipboardCutFiles(paths);
            return true;
        }
    }
}
