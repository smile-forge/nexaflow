using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class CutFiles : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;

        public CutFiles(IShellServices shell) => _shell = shell;
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "✂️";
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Cut");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                 => "✂️";
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Cut");
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
        {
            // Companions ride along, so a paste anywhere — here or in Explorer — moves them too.
            NativeMethods.ClipboardCutFiles(Services.FileCompanions.For(_shell).WithCompanions(Services.ShellPath.Realize([filePath])));
            return Task.FromResult(true);
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            // See CopyFiles: an empty file-drop list replaces the user's clipboard, so an empty selection
            // must stop before the clipboard rather than clearing it and reporting success.
            var paths = Services.ShellPath.Realize(filePaths).ToList();
            if (paths.Count == 0) return Task.FromResult(false);

            NativeMethods.ClipboardCutFiles(Services.FileCompanions.For(_shell).WithCompanions(paths));
            return Task.FromResult(true);
        }
    }
}
