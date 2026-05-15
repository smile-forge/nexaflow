using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Core.FileActions
{
    public class CutFiles : IFileAction, IFolderAction
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "✂️";
        public string DisplayName            => "Cut";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive        => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                 => "✂️";
        string IFolderAction.DisplayName          => "Cut";
        bool   IFolderAction.RequiresRefresh       => false;
        bool   IFolderAction.CanPerformAction      => true;
        public bool   AppliesToRoot               => false;
        public bool   AppliesToDrives             => false;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath)
        {
            NativeMethods.ClipboardCutFiles([filePath]);
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            NativeMethods.ClipboardCutFiles([.. filePaths]);
            return true;
        }
    }
}
