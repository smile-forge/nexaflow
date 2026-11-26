using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
        /// Pastes files from the clipboard into the current folder — by path, or files that were copied as their content.
    /// The path passed to <see cref="PerformAction(string)"/> is the destination directory.
    /// <see cref="CanPerformAction"/> gates visibility: the action is excluded
    /// from the strip entirely when the clipboard holds no pasteable files.
    /// <para>
    /// The clipboard is read when the paste is asked for, and the transfer is queued — so pasting a
    /// large folder never freezes the window, and it resolves name clashes the same way a drag-drop
    /// does because both go through the same planner.
    /// </para>
    /// </summary>
    public class PasteFiles(IShellServices shell, Operations.FileOperationQueue queue) : IFolderAction, ICacheable
    {
        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles => true;
        public IconRef Icon                 => IconRef.Emoji("📂");
        public string DisplayName           => Str.Get("WindowsFileSystem.Actions.Paste");
        public bool   RequiresRefresh       => false;  // the queue refreshes when the operation finishes
        public bool   AppliesToRoot         => true;   // visible even with no list selection
        public bool   AppliesToDrives       => true;   // can paste into a drive root

        /// <summary>The kinds a paste of files is read in: files by path, and files that are only their content.</summary>
        internal static readonly string[] PastedKinds = [DragKinds.Files, DragKinds.FileContents];

        /// <summary>
        /// Re-evaluated each time the action strip is rebuilt — which the file view does when the
        /// clipboard's kinds change — so the button comes and goes with files on the clipboard.
        /// </summary>
        public bool CanPerformAction => PastedKinds.Any(shell.Clipboard.Kinds.Contains);

        public async Task<bool> PerformActionAsync(string destinationFolder, CancellationToken ct = default)
            => Paste(await shell.Clipboard.ReadAsync(PastedKinds, ct), destinationFolder, queue, shell.Clipboard);

        /// <summary>
        /// Queues what <paramref name="held"/> carries into <paramref name="destinationFolder"/>: files by path moved where they were
        /// cut — the clipboard emptied only once they have landed, so a failed move can be fixed and pasted again — and copied
        /// otherwise; files that came as content copied from where they were staged. False where there was nothing to paste.
        /// </summary>
        internal static bool Paste(IDragPayload held, string destinationFolder, Operations.FileOperationQueue queue, IShellClipboard clipboard)
        {
            var paths = held.FilesOnDisk();
            if (paths.Count == 0) return false;

            var isCut = held.Contains(DragKinds.FilesCut);
            return queue.EnqueuePaste(paths, destinationFolder, isCut, onCutSucceeded: () => _ = clipboard.ClearAsync()) is not null;
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
        {
            // When called with multiple paths, use the first one as destination.
            foreach (var path in folderPaths) return PerformActionAsync(path, ct);
            return Task.FromResult(false);
        }
    }
}
