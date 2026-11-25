using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    public class RenameFile : IFileAction, IFolderAction, ICacheable
    {
        private readonly IShellServices _shell;
        private readonly IVirtualFileSystem _vfs;

        private readonly Services.FileCompanions _companions;

        public RenameFile(IShellServices shell, IVirtualFileSystem vfs, Services.FileCompanions companions)
        {
            _shell      = shell;
            _vfs        = vfs;
            _companions = companions;
        }

        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;   // rename only makes sense for one item
        public IconRef Icon                  => IconRef.Emoji("✏");
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Rename");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => false;   // refresh is triggered by the confirm callback
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => false;
        IconRef IFolderAction.Icon                => IconRef.Emoji("✏");
        string IFolderAction.DisplayName          => Str.Get("WindowsFileSystem.Actions.Rename");
        bool   IFolderAction.RequiresRefresh       => false;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public async Task<bool> PerformActionAsync(string path, CancellationToken ct = default)
        {
            path           = new Services.ShellPath(_vfs).RealForMutation(path);
            bool isDir     = await _vfs.IsDirectoryAsync(path, ct);
            string dir     = _vfs.GetParentPath(path) ?? Path.GetDirectoryName(path)!;
            string oldName = _vfs.GetDisplayName(path);   // as the file system names it, not the segment standing for it
            string title   = isDir ? Str.Get("WindowsFileSystem.Rename.FolderTitle") : Str.Get("WindowsFileSystem.Rename.FileTitle");

            _shell.ShowPrompt(
                title:        title,
                label:        Str.Get("WindowsFileSystem.Rename.Label"),
                initialValue: oldName,
                onConfirm: newName => _ = RenameAsync(path, dir, oldName, newName.Trim(), isDir),
                onCancel: () => { });

            return false;   // the rename waits on the prompt — refresh handled by RenameAsync
        }

        /// <summary>The rename the prompt confirmed, its companions with it. A failure is shown rather than thrown, because
        /// nothing awaits a prompt's callback.</summary>
        private async Task RenameAsync(string path, string folder, string oldName, string newName, bool isDir)
        {
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;
            if (!_vfs.CanName(folder, newName))
            {
                _shell.ShowError(Str.Format("WindowsFileSystem.Rename.FailedFormat", oldName,
                                            Str.Format("WindowsFileSystem.Rename.CannotNameFormat", newName)));
                return;
            }

            var dest = _vfs.ChildPath(folder, newName);
            try
            {
                await _vfs.MoveAsync(path, dest, replace: false);
                if (!isDir) await _companions.FollowAsync(_vfs, path, dest, move: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _shell.ShowError(Str.Format("WindowsFileSystem.Rename.FailedFormat", oldName, ex.Message));
            }
            _shell.RequestRefresh();
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            // SupportsMultipleFiles = false so this overload is never called,
            // but implement it defensively by renaming the first item.
            foreach (var p in filePaths) return PerformActionAsync(p, ct);
            return Task.FromResult(false);
        }
    }
}
