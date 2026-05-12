using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Injectable service that a <see cref="IFileAction"/> can use to open a
/// new tab in the shell.  The action receives this via constructor injection
/// so it has no direct dependency on the view-model or view layer.
/// </summary>
public interface ITabOpener
{
    /// <summary>
    /// Opens an image-viewer tab for the given ordered list of image paths.
    /// </summary>
    void OpenImageViewer(IReadOnlyList<string> imagePaths);

    /// <summary>
    /// Opens an HTML viewer tab for the given file path.
    /// Accepts .html and .url files.
    /// </summary>
    void OpenHtmlViewer(string filePath);

    /// <summary>
    /// Opens a live markdown editor/preview tab for the given .md file.
    /// </summary>
    void OpenMarkdownViewer(string filePath);
}
