using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using System.Windows;

namespace Nexaflow.Features.Projects.Viewlets;

public sealed class AiSummaryViewlet : IFolderViewlet
{
    private readonly ProjectsConfig _config;

    public AiSummaryViewlet(ProjectsConfig config) => _config = config;

    public string DisplayName    => "AI Summary";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFileGlobs => [".aisummary"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.DoubleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.DoubleBar, ViewletDisplayMode.Large];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new AiSummaryViewletView(new ProjectOperations(_config), folderPath, controller);
}
