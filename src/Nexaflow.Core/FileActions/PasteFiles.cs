using Nexaflow.Features.Common;
using Nexaflow.Features.WinFileSystem;
using System.Collections.Generic;

namespace Nexaflow.Features.WinFileSystem.FileActions
{
    /// <summary>
    /// Pastes files from the clipboard into the current folder.
    /// The single path passed to <see cref="PerformAction(string)"/> is the
    /// destination directory (the view's current path).
    /// <see cref="CanPerformAction"/> gates visibility: the action is excluded
    /// from the strip entirely when the clipboard holds no pasteable files.
    /// </summary>
    public class PasteFiles : IFileAction
    {
        public bool   IsDestructive        => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "📂";
        public string DisplayName           => "Paste";
        public string SupportedFileTypes    => "*.*";
        public bool   AppliesToFolders      => true;
        public string SupportedFolderNames  => "*";
        public bool   AppliesToRoot         => true;   // visible even with no list selection
        public bool   AppliesToDrives       => true;   // can paste into a drive root
        public bool   RequiresRefresh       => true;   // pasting changes the directory contents

        /// <summary>
        /// Re-evaluated each time the action strip is rebuilt so the button
        /// disappears the moment the clipboard no longer contains files.
        /// </summary>
        public bool CanPerformAction => NativeMethods.ClipboardHasFiles();

        public bool PerformAction(string destinationFolder)
        {
            NativeMethods.ClipboardPasteFiles(destinationFolder);
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // When called with multiple paths, use the first one as destination.
            // In practice the registry always calls the single-path overload for Paste.
            foreach (var path in filePaths)
            {
                NativeMethods.ClipboardPasteFiles(path);
                break;
            }
            return true;
        }
    }
}
