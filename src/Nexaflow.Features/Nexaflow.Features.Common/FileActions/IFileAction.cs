using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Features.Common
{
    /// <summary>
    /// A context action offered for one or more <b>files</b> in the file browser (open in a viewer,
    /// delete, run an external app, …). Matched to a file by its hierarchical <see cref="ExperienceId"/>
    /// via <c>FileMapManager</c> — unlike <see cref="IFolderAction"/>, which matches structurally. Viewer-
    /// opening actions live in the owning feature; system actions (copy/rename/…) live in Core. Discovered
    /// by <c>FileSystemFeatureRegistry</c> (NOT <c>FeatureManager</c>); see <see cref="ICacheable"/> for the
    /// per-<c>WorkContext</c> caching rule and <see cref="IFileCreateAction"/> for "new file" actions.
    /// </summary>
    public interface IFileAction
    {
        bool IsDestructive { get; }
        bool SupportsMultipleFiles { get; }
        string Icon { get; }
        string DisplayName { get; }

        /// <summary>
        /// Hierarchical experience identifier, e.g. "/binary/installer" or "/image".
        /// FileMapManager uses this to match the action against file selection criteria.
        /// Child IDs automatically satisfy parent experiences (hierarchy propagates upward).
        /// Each implementation must also expose a
        /// <c>public static string? StaticExperienceId { get; }</c> so that
        /// <c>FeatureManager</c> can build the experience list via reflection
        /// without instantiation.
        /// </summary>
        string ExperienceId { get; }

        /// <summary>
        /// Human-readable description of the experience, shown in the File Type Actions
        /// Options panel when selecting which experience a mapping applies to.
        /// </summary>
        string ExperienceDescription { get; }

        bool RequiresRefresh { get; }

        bool CanPerformAction { get; }

        /// <summary>
        /// Optional WPF image to display instead of the <see cref="Icon"/> glyph.
        /// Must be frozen if it was created on a non-UI thread.
        /// </summary>
        ImageSource? IconImage => null;

        /// <summary>
        /// Optional tooltip shown on the action button, supplementing <see cref="DisplayName"/>.
        /// Defaults to <c>null</c> (tooltip falls back to DisplayName in the view).
        /// </summary>
        string? Tooltip => null;

        /// <summary>
        /// Whether this action may be pinned to the ribbon (and dragged there). Defaults to
        /// <c>true</c>; set <c>false</c> for synthetic/menu actions that can't be rehydrated
        /// from a ribbon button (e.g. the file browser's "New" button).
        /// </summary>
        bool IsRibbonPinnable => true;

        /// <summary>
        /// True when this action opens the file in an internal Nexaflow viewer tab (Text, Image,
        /// JSON, …) — as opposed to a utility action (delete/rename/properties) or an external
        /// launch (open-with, shell verb, custom app). The "Define New" wizard lists only
        /// viewer actions when mapping a file type to an internal viewer. Defaults to <c>false</c>.
        /// </summary>
        bool OpensViewer => false;

        /// <summary>
        /// Whether the action strip shows its success-tick flash after this action runs. Defaults
        /// to <c>true</c>. Chooser/gateway actions whose completion can't be detected reliably set
        /// this <c>false</c> — e.g. the "Open With" dialog, which doesn't report cancellation
        /// consistently across Windows versions, so a tick would be misleading.
        /// </summary>
        bool ShowsSuccessTick => true;

        /// <summary>
        /// True when this action hands the path to an external process that may need the file's
        /// <b>neighbours</b> — a dependency DLL, a sidecar, an installer's payload. Such an action is
        /// offered only where the whole subtree really is on disk: a plain path, or one under a
        /// pass-through mount (which resolves to a real directory). It is <b>suppressed</b> inside an
        /// archive, where materialising produces one lone temp file and the process would fail in
        /// confusing ways — better not to offer it than to offer a half-truth.
        /// <para>
        /// Implementations that set this must run against the <i>resolved real</i> path
        /// (<c>IVirtualFileSystem.TryResolveReal</c>), not the virtual one. Defaults to <c>false</c>:
        /// an action that only reads bytes through the VFS is safe everywhere.
        /// </para>
        /// </summary>
        bool RequiresFullyBackedPath => false;

        bool PerformAction(string filePath);
        bool PerformAction(IEnumerable<string> filePaths);

        /// <summary>
        /// Force-executes the action on a single path, skipping any confirmation prompts.
        /// Defaults to calling the normal overload.
        /// </summary>
        bool PerformAction(string filePath, bool force) => PerformAction(filePath);

        /// <summary>
        /// Force-executes the action on multiple paths, skipping any confirmation prompts.
        /// Defaults to calling the normal overload.
        /// </summary>
        bool PerformAction(IEnumerable<string> filePaths, bool force) => PerformAction(filePaths);

        /// <summary>
        /// Per-instance state that defines this action — for runtime-constructed types
        /// (e.g. one shell-verb action per registered verb) whose identity depends on
        /// constructor args. Returns null for singleton actions, where the type name
        /// alone is enough to look the instance up via <c>FeatureManager.FindFileAction</c>.
        ///
        /// The ribbon pinning system persists these params alongside the type name so
        /// the action can be rehydrated when its button is clicked. Action types that
        /// override this MUST also expose a
        /// <c>public static IFileAction Rehydrate(Dictionary&lt;string, string&gt; params)</c>
        /// static factory; the rehydration path uses reflection to locate it.
        /// </summary>
        Dictionary<string, string>? GetReinitParams() => null;
    }
}
