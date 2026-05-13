using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.WinFileSystem.FileActions
{
    public class CopyFiles : IFileAction
    {
        public bool   IsDestructive        => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "📋";
        public string DisplayName           => "Copy";
        public string SupportedFileTypes    => "*.*";
        public bool   AppliesToFolders      => true;
        public string SupportedFolderNames  => "*";
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => false;
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;

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
