using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    internal class FileProperties(IVirtualFileSystem vfs) : IFileAction, IFolderAction, ICacheable
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public IconRef Icon                  => IconRef.Emoji("☶");
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Properties");
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.AllFiles");
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive       => false;
        bool   IFolderAction.SupportsMultipleFiles => false;
        IconRef IFolderAction.Icon               => IconRef.Emoji("☶");
        string IFolderAction.DisplayName         => Str.Get("WindowsFileSystem.Actions.Properties");
        bool   IFolderAction.RequiresRefresh      => false;
        bool   IFolderAction.CanPerformAction     => true;
        public bool   AppliesToRoot              => false;
        public bool   AppliesToDrives            => true;

        // ── Actions ───────────────────────────────────────────────────────────

        public async Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
        {
            try
            {
                // A file inside a disk image / archive is virtual — the shell needs a real path, so
                // materialise it to a temp copy first (a real file/folder passes through unchanged).
                NativeMethods.ShowFileProperties(await RealPathAsync(filePath, ct));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> RealPathAsync(string path, CancellationToken ct)
        {
            try { return await vfs.MaterializeFileAsync(path, ct); }
            catch { return path; }
        }

        // SupportsMultipleFiles is false, so the action strip never offers this on a multi-selection — but
        // the ribbon-pin path invokes a pinned action directly, and this threw NotImplementedException there.
        // Show the first file's properties, the same defensive answer RenameFile and ExecuteFile give.
        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            foreach (var p in filePaths) return PerformActionAsync(p, ct);
            return Task.FromResult(false);
        }
    }
}
