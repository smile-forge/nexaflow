using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Localization;
using System.Windows;

namespace Nexaflow.Features.Projects.Viewlets;

public sealed class AiSummaryViewlet : IFolderViewlet
{
    private readonly ProjectsConfig _config;

    public AiSummaryViewlet(ProjectsConfig config) => _config = config;

    public string DisplayName    => Str.Get("Projects.Viewlet.AiSummary");
    public bool   AppliesToDrives => false;

    public string[]? ContainsFileGlobs => [".aisummary"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.DoubleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.DoubleBar, ViewletDisplayMode.Large];

    public object CreateViewModel(string folderPath, IViewletController controller)
        => new ViewModels.AiSummaryViewletViewModel(new ProjectOperations(_config), folderPath, controller);

    /// <summary>The summary is saved beside the folder's files with Windows' own file calls, which a location's folder may not be changed by.</summary>
    public bool RequiresLocalFolder => true;
}
