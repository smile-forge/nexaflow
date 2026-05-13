using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Injectable service that opens tabs in the shell without a direct dependency
/// on the view-model or view layer. Received via constructor injection.
/// </summary>
public interface ITabOpener
{
    /// <summary>
    /// Requests the shell to open any registered page kind, optionally passing
    /// page-specific parameters (e.g. folder path, file path).
    /// </summary>
    void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null);

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
