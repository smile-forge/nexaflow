using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class CutFiles : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;
        private readonly Services.ShellPath _paths;

        public CutFiles(IShellServices shell, IVirtualFileSystem files)
        {
            _shell = shell;
            _paths = new Services.ShellPath(files);
        }
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public IconRef Icon                  => IconRef.Emoji("✂️");
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Cut");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        IconRef IFolderAction.Icon                => IconRef.Emoji("✂️");
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Cut");
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

        public async Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            // See CopyFiles: an empty file-drop list replaces the user's clipboard, so an empty selection
            // must stop before the clipboard rather than clearing it and reporting success.
            var paths = await _paths.RealizeAsync(filePaths, ct);
            if (paths.Length == 0) return false;

            // Companions ride along, so a paste anywhere — here or in Explorer — moves them too.
            return _shell.Clipboard.Write(ComposedPayload.OfFiles(Services.FileCompanions.For(_shell).WithCompanions(paths), cut: true));
        }
    }
}
