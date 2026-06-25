using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Features.Common
{
    /// <summary>
    /// An action that operates on one or more directories.
    /// Folder actions are matched structurally (folder name, contents) rather than
    /// via the experience/criteria mapping used by <see cref="IFileAction"/>.
    /// </summary>
    public interface IFolderAction
    {
        bool IsDestructive { get; }
        bool SupportsMultipleFiles { get; }
        string Icon { get; }
        string DisplayName { get; }

        bool RequiresRefresh { get; }
        bool CanPerformAction { get; }

        /// <summary>
        /// When <c>true</c>, this action appears even when no item is selected
        /// (i.e. operating on the current open folder).
        /// </summary>
        bool AppliesToRoot { get; }

        /// <summary>
        /// When <c>true</c>, this action appears for drive-root entries (C:\, D:\, etc.).
        /// </summary>
        bool AppliesToDrives { get; }

        /// <summary>
        /// Glob pattern matched against the folder name. Use <c>"*"</c> to match any folder.
        /// </summary>
        string FolderNameGlob => "*";

        /// <summary>
        /// If non-null, the folder must contain at least one file matching any of these globs.
        /// </summary>
        string[]? ContainsFileGlobs => null;

        /// <summary>
        /// When <see cref="ContainsFileGlobs"/> is set, the minimum percentage (0–100) of the folder's
        /// top-level files that must match one of those globs for the action to apply. The default of
        /// <c>0</c> keeps the plain "at least one match" behaviour; a positive value requires that share
        /// of the directory to match. The matcher enumerates the folder once and bails as soon as the
        /// threshold is reached or has become unreachable.
        /// </summary>
        int MinimumFileGlobMatchPercentage => 0;

        /// <summary>
        /// If non-null, the folder must contain at least one sub-folder matching any of these globs.
        /// </summary>
        string[]? ContainsFolderGlobs => null;

        ImageSource? IconImage => null;
        string? Tooltip => null;

        /// <summary>
        /// Whether this action may be pinned to the ribbon (and dragged there). Defaults to
        /// <c>true</c>; set <c>false</c> for synthetic/menu folder actions that can't be
        /// rehydrated from a ribbon button (e.g. the file browser's "New" button).
        /// </summary>
        bool IsRibbonPinnable => true;

        /// <summary>Whether this action is offered when the current folder is <b>inside an archive</b>
        /// (a virtual path). Defaults to <c>true</c>; set <c>false</c> for actions that need a real
        /// on-disk folder — e.g. "Zip It", which has nothing to compress from a virtual location.</summary>
        bool AppliesInsideArchive => true;

        bool PerformAction(string folderPath);
        bool PerformAction(IEnumerable<string> folderPaths);

        bool PerformAction(string folderPath, bool force) => PerformAction(folderPath);
        bool PerformAction(IEnumerable<string> folderPaths, bool force) => PerformAction(folderPaths);
    }
}
