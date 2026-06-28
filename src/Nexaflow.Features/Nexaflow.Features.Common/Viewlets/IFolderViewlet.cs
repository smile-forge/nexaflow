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

    ViewletDisplayMode DefaultDisplayMode => ViewletDisplayMode.DoubleBar;

    ViewletDisplayMode[] SupportedModes
        => [ViewletDisplayMode.SingleBar, ViewletDisplayMode.DoubleBar, ViewletDisplayMode.Large, ViewletDisplayMode.Full];

    FrameworkElement CreateView(string folderPath, IViewletController controller);
}
