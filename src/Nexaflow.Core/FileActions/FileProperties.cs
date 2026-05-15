using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;

namespace Nexaflow.Core.FileActions
{
    internal class FileProperties : IFileAction, IFolderAction
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "☶";
        public string DisplayName            => "Properties";
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
                NativeMethods.ShowFileProperties(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            throw new NotImplementedException();
        }
    }
}
