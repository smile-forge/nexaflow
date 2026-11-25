using Nexaflow.Core.Models;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's own pickers for a file or a folder, which the UI toolkit draws. Each is modal over <c>owner</c> — the window
/// asking, or none — browses <c>workspace</c>'s file system, and answers null or nothing when the person cancels.
/// </summary>
public interface IFilePickers
{
    /// <summary>One file to open, of <paramref name="extensions"/> when given.</summary>
    string? PickOpenFile(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath);

    /// <summary>Any number of files to open, of <paramref name="extensions"/> when given.</summary>
    IReadOnlyList<string> PickOpenFiles(IWindowHost? owner, WorkspaceRuntime workspace, IReadOnlyList<string>? extensions, string? initialPath);

    /// <summary>A folder.</summary>
    string? PickFolder(IWindowHost? owner, WorkspaceRuntime workspace, string? initialPath);

    /// <summary>Where to save <paramref name="bytes"/> bytes, named <paramref name="defaultFileName"/> unless the person says otherwise,
    /// as one of <paramref name="types"/>.</summary>
    string? PickSaveFile(IWindowHost? owner, WorkspaceRuntime workspace, string defaultFileName, IReadOnlyList<SaveFileType> types,
                         string? initialPath, long bytes);
}
