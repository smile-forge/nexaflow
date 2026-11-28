using Nexaflow.Features.Common;

namespace Nexaflow.Session.Services;

/// <summary>
/// The shell's own pickers for a file or a folder, which the UI toolkit draws. Each is modal over <c>owner</c> — the window
/// asking, or none — browses <c>workspace</c>'s file system, and answers null or nothing when the person cancels. Asked on the
/// UI thread; a toolkit whose pickers are modal answers at once, one whose pickers are asynchronous when the person has chosen.
/// </summary>
public interface IFilePickers
{
    /// <summary>One file to open, of <paramref name="extensions"/> when given.</summary>
    Task<string?> PickOpenFileAsync(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath);

    /// <summary>Any number of files to open, of <paramref name="extensions"/> when given.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath);

    /// <summary>A folder.</summary>
    Task<string?> PickFolderAsync(IWindowHost? owner, WorkspaceRuntime workspace, string? initialPath);

    /// <summary>Where to save <paramref name="bytes"/> bytes, named <paramref name="defaultFileName"/> unless the person says otherwise,
    /// as one of <paramref name="types"/>.</summary>
    Task<string?> PickSaveFileAsync(IWindowHost? owner, WorkspaceRuntime workspace, string defaultFileName, IReadOnlyList<SaveFileType> types,
                                    string? initialPath, long bytes);
}
