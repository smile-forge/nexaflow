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

    /// <summary>File extension produced, e.g. ".html" or ".md".</summary>
    string FileExtension { get; }

    /// <summary>
    /// Creates a new file in <paramref name="folderPath"/>.
    /// Returns the full path of the created file, or <c>null</c> on cancellation/failure.
    /// </summary>
    string? Create(string folderPath);
}
