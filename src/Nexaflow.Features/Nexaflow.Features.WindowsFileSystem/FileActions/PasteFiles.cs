using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
    /// Pastes files from the clipboard into the current folder.
    /// The path passed to <see cref="PerformAction(string)"/> is the destination directory.
    /// <see cref="CanPerformAction"/> gates visibility: the action is excluded
    /// from the strip entirely when the clipboard holds no pasteable files.
    /// <para>
    /// The clipboard is read here, on the UI thread where it is legal to touch, and the transfer is
    /// queued — so pasting a large folder no longer freezes the window, and it resolves name clashes
    /// the same way a drag-drop does because both go through the same planner.
    /// </para>
    /// </summary>
    public class PasteFiles(IShellServices shell) : IFolderAction, ICacheable
    {
        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "📂";
        public string DisplayName           => "Paste";
        public bool   RequiresRefresh       => false;  // the queue refreshes when the operation finishes
        public bool   AppliesToRoot         => true;   // visible even with no list selection
        public bool   AppliesToDrives       => true;   // can paste into a drive root

        /// <summary>
        /// Re-evaluated each time the action strip is rebuilt so the button
        /// disappears the moment the clipboard no longer contains files.
        /// </summary>
        public bool CanPerformAction => NativeMethods.ClipboardHasFiles();

        public bool PerformAction(string destinationFolder)
        {
            var (paths, isCut) = NativeMethods.ClipboardReadDrop();
            if (paths.Count == 0) return false;

            // The clipboard is only consumed once the cut has actually landed — a failed one is left in
            // place so the cause can be fixed and the paste repeated.
            var op = Operations.FileOperationQueue.For(shell)
                .EnqueuePaste(paths, destinationFolder, isCut, onCutSucceeded: NativeMethods.ClipboardClear);

            return op is not null;
        }

        public bool PerformAction(IEnumerable<string> folderPaths)
        {
            // When called with multiple paths, use the first one as destination.
            foreach (var path in folderPaths) return PerformAction(path);
            return false;
        }
    }
}
