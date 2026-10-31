using Nexaflow.Icons;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common;

/// <summary>
/// One kind of new item the new-item picker offers in a folder — its icon, its name and the extension it is made with.
/// An <see cref="IFileCreateAction"/> is created from a name alone; an <see cref="IFileCreateWithOptions"/> asks for more
/// first. Both are discovered by <c>FileSystemFeatureRegistry</c> from all registered feature assemblies.
/// </summary>
public interface IFileCreateEntry
{
    /// <summary>The glyph the entry wears — and, while <see cref="IconResource"/> is still being drawn or cannot be, the one
    /// it shows instead.</summary>
    IconRef Icon { get; }
    string DisplayName { get; }

    /// <summary>The button's UI Automation id. Invariant, unlike <see cref="DisplayName"/>, which is in the reader's
    /// language; a create action built per ShellNew entry names its extension.</summary>
    string AutomationId => GetType().Name;

    /// <summary>
    /// A picture in a Windows file's resources to draw instead of the <see cref="Icon"/> glyph — a registry ShellNew type's
    /// own icon, say; named, not loaded, as <see cref="IFileAction.IconResource"/> is.
    /// </summary>
    ShellIconRef? IconResource => null;

    /// <summary>
    /// Default file extension produced, e.g. ".html" or ".md".
    /// Empty for actions that create something extensionless (e.g. a folder).
    /// The host uses this to resolve the final name when the user omits an extension.
    /// </summary>
    string FileExtension { get; }
}

/// <summary>
/// A "new file" action that creates a blank file of a given type in the current folder from its name alone.
/// </summary>
public interface IFileCreateAction : IFileCreateEntry
{
    /// <summary>
    /// Creates the new item named <paramref name="fileName"/> inside <paramref name="folderPath"/>.
    /// <paramref name="fileName"/> is already extension-resolved by the host (the user's extension
/// if they typed one, otherwise <see cref="IFileCreateEntry.FileExtension"/> appended), so implementations use it
/// verbatim. Answers the full path of the created item, which the browser selects, or <c>null</c> on failure.
/// Awaitable because the folder may be on a location, where the item exists only once the server says so.
/// </summary>
Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default);
}
