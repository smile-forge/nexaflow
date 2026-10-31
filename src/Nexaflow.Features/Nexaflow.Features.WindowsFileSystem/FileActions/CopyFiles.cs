using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class CopyFiles : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;

        public CopyFiles(IShellServices shell) => _shell = shell;
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public IconRef Icon                  => IconRef.Emoji("📋");
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Copy");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        IconRef IFolderAction.Icon                => IconRef.Emoji("📋");
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Copy");
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
        {
            // Companions ride along, so a paste anywhere — here or in Explorer — brings them too.
            NativeMethods.ClipboardCopyFiles(Services.FileCompanions.For(_shell).WithCompanions(Services.ShellPath.Realize([filePath])));
            return Task.FromResult(true);
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            // Nothing selected must not reach the clipboard: an empty file-drop list REPLACES whatever the
            // user had copied, so "copy" on an empty selection silently threw away their clipboard and
            // reported success. The ribbon path can invoke this with no selection.
            var paths = Services.ShellPath.Realize(filePaths).ToList();
            if (paths.Count == 0) return Task.FromResult(false);

            NativeMethods.ClipboardCopyFiles(Services.FileCompanions.For(_shell).WithCompanions(paths));
            return Task.FromResult(true);
        }
    }
}
