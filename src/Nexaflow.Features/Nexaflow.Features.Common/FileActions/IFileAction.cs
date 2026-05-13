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
        string SupportedFileTypes { get; }
        bool AppliesToFolders { get; }

        bool RequiresRefresh { get; }

        bool CanPerformAction { get; }
        string SupportedFolderNames { get; }

        /// <summary>
        /// Optional MIME content-type selector (e.g. "text/plain").
        /// Supports a trailing wildcard: "text/*".
        /// Use <c>"*"</c> to match any content type (the default).
        /// </summary>
        string SupportedContentTypes => "*";

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
        /// When <c>false</c> this action is hidden when no item is selected
        /// (i.e. the view is showing the root / nothing is highlighted).
        /// Set <c>true</c> only for actions that operate on the current folder itself.
        /// </summary>
        bool AppliesToRoot { get; }

        /// <summary>
        /// When <c>false</c> this action will not appear for drive-root entries
        /// (e.g. C:\). Defaults to <c>true</c> for most actions.
        /// </summary>
        bool AppliesToDrives { get; }

        bool PerformAction(string filePath);
        bool PerformAction(IEnumerable<string> filePaths);

        /// <summary>
        /// Force-executes the action on a single path, skipping any confirmation
        /// prompts. Defaults to calling the normal overload.
        /// </summary>
        bool PerformAction(string filePath, bool force) => PerformAction(filePath);

        /// <summary>
        /// Force-executes the action on multiple paths, skipping any confirmation
        /// prompts. Defaults to calling the normal overload.
        /// </summary>
        bool PerformAction(IEnumerable<string> filePaths, bool force) => PerformAction(filePaths);
    }
}
