using System.Windows.Media;

namespace Nexaflow.Features.Common;

/// <summary>
/// Represents a "new file" action that creates a blank file of a given type
/// in the current folder. Discovered by <see cref="FileActionManager"/> from
/// all registered feature assemblies.
/// </summary>
public interface IFileCreateAction
{
    string Icon { get; }
    string DisplayName { get; }

    /// <summary>
    /// Optional WPF image to display instead of the <see cref="Icon"/> glyph (e.g. a real
    /// shell icon for a registry ShellNew entry). Must be frozen if created off the UI thread.
    /// </summary>
    ImageSource? IconImage => null;

    /// <summary>
    /// Default file extension produced, e.g. ".html" or ".md".
    /// Empty for actions that create something extensionless (e.g. a folder).
    /// The host uses this to resolve the final name when the user omits an extension.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Creates the new item named <paramref name="fileName"/> inside <paramref name="folderPath"/>.
    /// <paramref name="fileName"/> is already extension-resolved by the host (the user's extension
    /// if they typed one, otherwise <see cref="FileExtension"/> appended), so implementations use it
    /// verbatim. Returns the full path of the created item, or <c>null</c> on failure.
    /// </summary>
    string? Create(string folderPath, string fileName);
}
