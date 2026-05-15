using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Features.Common
{
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
    }
}
