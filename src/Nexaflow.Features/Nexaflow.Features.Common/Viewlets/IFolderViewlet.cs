using System.Windows;

namespace Nexaflow.Features.Common.Viewlets;

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
