using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Core.FileActions
{
    public class CopyFiles : IFileAction, IFolderAction
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "📋";
        public string DisplayName            => "Copy";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => true;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                 => "📋";
        string IFolderAction.DisplayName          => "Copy";
        bool   IFolderAction.RequiresRefresh       => true;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath)
        {
            NativeMethods.ClipboardCopyFiles([filePath]);
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            NativeMethods.ClipboardCopyFiles([.. filePaths]);
            return true;
        }
    }
}
