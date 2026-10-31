using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Icons;

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
        /// <summary>The glyph the action's button wears — and, while <see cref="IconResource"/> is still being drawn or cannot
        /// be, the one it shows instead.</summary>
        IconRef Icon { get; }
        string DisplayName { get; }

        /// <summary>The button's UI Automation id. Invariant, unlike <see cref="DisplayName"/>, which is in the reader's
        /// language.</summary>
        string AutomationId => GetType().Name;

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

        /// <summary>A picture in a Windows file's resources to draw instead of the <see cref="Icon"/> glyph; named, not loaded,
        /// as <see cref="IFileAction.IconResource"/> is.</summary>
        ShellIconRef? IconResource => null;
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

        /// <summary>Per-path applicability, evaluated against each selected folder/drive after the structural
        /// (name / contents glob) checks pass. Defaults to <c>true</c>. Lets an action hide itself for a
        /// specific path that structural globs can't express — e.g. "Unmount" only on a drive that is backed
        /// by a currently mounted disk image. Keep it cheap: it runs on every selection change.</summary>
        bool AppliesToFolder(string folderPath) => true;

        /// <summary>
        /// Runs the action on one folder, answering whether it did something now (the success tick). Awaitable because the
        /// folder may be on a location, where a paste or a delete knows how it went only after a round trip.
        /// </summary>
        Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default);

        /// <summary>As <see cref="PerformActionAsync(string, CancellationToken)"/>, on several folders.</summary>
        Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default);

        /// <summary>Force-executes the action, skipping any confirmation prompts. Defaults to the normal overload.</summary>
        Task<bool> PerformActionAsync(string folderPath, bool force, CancellationToken ct = default) => PerformActionAsync(folderPath, ct);

        /// <summary>Force-executes the action on several folders, skipping any confirmation prompts. Defaults to the normal overload.</summary>
        Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, bool force, CancellationToken ct = default) => PerformActionAsync(folderPaths, ct);
    }
}
