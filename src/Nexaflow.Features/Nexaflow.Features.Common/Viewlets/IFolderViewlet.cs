using System.Windows;

namespace Nexaflow.Features.Common.Viewlets;

/// <summary>
/// A folder-display extension: when the open folder matches this viewlet's structural criteria (name /
/// contained-files / contained-folders globs), the file browser renders the inline view from
/// <see cref="CreateView"/> above its file list — e.g. the Git viewlet (repo status) or the .NET viewlet
/// (projects + build). Discovered by reflection like <see cref="IPageRegistration"/>, instantiated per
/// workspace. The host hands the view an <see cref="IViewletController"/> for display-mode changes; a
/// view may also implement <see cref="IViewletAiSurface"/> to feed the AI folder-specific context/tools.
/// </summary>
public interface IFolderViewlet
{
    string DisplayName { get; }

    bool AppliesToDrives { get; }

    string FolderNameGlob => "*";

    string[]? ContainsFileGlobs => null;

    string[]? ContainsFolderGlobs => null;

    /// <summary>
    /// True when this viewlet needs the folder to genuinely exist on disk — because it runs real tooling
    /// against it (a process with a working directory, a repository handle) rather than reading entries
    /// through the virtual file system. Such a viewlet is offered for a real path or a pass-through
    /// mount, and <b>never inside an archive</b>, where the entries are bytes in a container and no
    /// directory exists for a tool to work in.
    /// <para>
    /// Defaults to <c>true</c>, unlike the equivalent on <see cref="IFileAction"/>: a file action often
    /// just reads bytes, whereas a viewlet describes a folder <i>as a project</i> and almost always shells
    /// out. A viewlet that genuinely reads through the VFS opts out explicitly — the safe direction, since
    /// forgetting to opt in would only ever produce a surface that cannot work.
    /// </para>
    /// </summary>
    bool RequiresFullyBackedPath => true;

    /// <summary>
    /// True when this viewlet changes the folder's files with Windows' own tools and has no other way to change them — a
    /// summary saved beside it. Only a folder of this PC may be changed so: a location's folder — a WSL distro's, though
    /// Windows shows it as a folder — changes only through the location, which keeps its exact names and its permissions, so
    /// the viewlet is not offered there. A viewlet whose tools can run on the location's own host declares
    /// <see cref="RunsOnLocationHost"/> instead. Defaults to <c>false</c>: a viewlet that only reads works on a location too.
    /// </summary>
    bool RequiresLocalFolder => false;

    /// <summary>
    /// True when this viewlet's tools — <c>git</c>, <c>dotnet</c> — change the folder, and on a location run on the location's
    /// own host (<c>IVirtualFileSystem.GetProgramHost</c>), so what they change keeps the names and permissions the host gives
    /// it. Such a viewlet is offered on a location whose host runs programs — one with a shell, named by the location's path or
    /// by a Windows folder showing it — and on no other; on this PC it runs its tools here. Defaults to <c>false</c>: a viewlet
    /// that only reads needs nothing of a location's host.
    /// </summary>
    bool RunsOnLocationHost => false;

    ViewletDisplayMode DefaultDisplayMode => ViewletDisplayMode.DoubleBar;

    ViewletDisplayMode[] SupportedModes
        => [ViewletDisplayMode.SingleBar, ViewletDisplayMode.DoubleBar, ViewletDisplayMode.Large, ViewletDisplayMode.Full];

    FrameworkElement CreateView(string folderPath, IViewletController controller);
}
