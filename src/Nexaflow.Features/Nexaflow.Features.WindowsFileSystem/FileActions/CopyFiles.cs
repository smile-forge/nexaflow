using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class CopyFiles : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;

        public CopyFiles(IShellServices shell) => _shell = shell;
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "📋";
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Copy");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                 => "📋";
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Copy");
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath)
        {
            // Companions ride along, so a paste anywhere — here or in Explorer — brings them too.
            NativeMethods.ClipboardCopyFiles(Services.FileCompanions.For(_shell).WithCompanions(Services.ShellPath.Realize([filePath])));
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // Nothing selected must not reach the clipboard: an empty file-drop list REPLACES whatever the
            // user had copied, so "copy" on an empty selection silently threw away their clipboard and
            // reported success. The ribbon path can invoke this with no selection.
            var paths = Services.ShellPath.Realize(filePaths).ToList();
            if (paths.Count == 0) return false;

            NativeMethods.ClipboardCopyFiles(Services.FileCompanions.For(_shell).WithCompanions(paths));
            return true;
        }
    }
}
