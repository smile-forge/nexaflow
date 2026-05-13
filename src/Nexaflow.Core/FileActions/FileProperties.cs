using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.WinFileSystem.FileActions
{
    internal class FileProperties : IFileAction
    {
        public bool IsDestructive => false;

        public bool SupportsMultipleFiles => false;

        public string Icon => "☶";

        public string DisplayName => "Properties";

        public string SupportedFileTypes => "*.*";

        public bool AppliesToFolders => false;

        public string SupportedFolderNames => "";

        public bool AppliesToRoot => false;

        public bool AppliesToDrives => true;

        public bool RequiresRefresh => false;

        public bool RequireRefresh => false;

        public bool CanPerformAction => true;

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
