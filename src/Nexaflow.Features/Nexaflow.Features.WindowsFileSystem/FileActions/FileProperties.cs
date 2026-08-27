using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    internal class FileProperties : IFileAction, IFolderAction, ICacheable
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "☶";
        public string DisplayName            => "Properties";
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive       => false;
        bool   IFolderAction.SupportsMultipleFiles => false;
        string IFolderAction.Icon                => "☶";
        string IFolderAction.DisplayName         => "Properties";
        bool   IFolderAction.RequiresRefresh      => false;
        bool   IFolderAction.CanPerformAction     => true;
        public bool   AppliesToRoot              => false;
        public bool   AppliesToDrives            => true;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath)
        {
            try
            {
                // A file inside a disk image / archive is virtual — the shell needs a real path, so
                // materialise it to a temp copy first (a real file/folder passes through unchanged).
                NativeMethods.ShowFileProperties(RealPath(filePath));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string RealPath(string path)
        {
            try { return VirtualFileSystem.Instance.MaterializeFile(path); }
            catch { return path; }
        }

        // SupportsMultipleFiles is false, so the action strip never offers this on a multi-selection — but
        // the ribbon-pin path invokes a pinned action directly, and this threw NotImplementedException there.
        // Show the first file's properties, the same defensive answer RenameFile and ExecuteFile give.
        public bool PerformAction(IEnumerable<string> filePaths)
        {
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
