using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
    /// Pastes files from the clipboard into the current folder.
    /// The path passed to <see cref="PerformAction(string)"/> is the destination directory.
    /// <see cref="CanPerformAction"/> gates visibility: the action is excluded
    /// from the strip entirely when the clipboard holds no pasteable files.
    /// </summary>
    public class PasteFiles : IFolderAction, ICacheable
    {
        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "📂";
        public string DisplayName           => "Paste";
        public bool   RequiresRefresh       => true;   // pasting changes the directory contents
        public bool   AppliesToRoot         => true;   // visible even with no list selection
        public bool   AppliesToDrives       => true;   // can paste into a drive root

        /// <summary>
        /// Re-evaluated each time the action strip is rebuilt so the button
        /// disappears the moment the clipboard no longer contains files.
        /// </summary>
        public bool CanPerformAction => NativeMethods.ClipboardHasFiles();

        public bool PerformAction(string destinationFolder)
        {
            NativeMethods.ClipboardPasteFiles(Services.ShellPath.RealForMutation(destinationFolder));
            return true;
        }

        public bool PerformAction(IEnumerable<string> folderPaths)
        {
            // When called with multiple paths, use the first one as destination.
            foreach (var path in folderPaths)
            {
                NativeMethods.ClipboardPasteFiles(Services.ShellPath.RealForMutation(path));
                break;
            }
            return true;
        }
    }
}
