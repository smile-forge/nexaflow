using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System.Windows;

namespace Nexaflow.Features.Projects.Viewlets;

/// <summary>
/// Folder viewlet shown when a folder contains a <c>.project</c> file: a per-status breakdown of the
/// backlog (one coloured chip per configured status with its count) plus a shortcut into the project's
/// detail view. Distinct from the <c>.aisummary</c> viewlet. Auto-discovered by reflection.
/// </summary>
public sealed class BacklogStatusViewlet : IFolderViewlet
{
    private readonly ProjectsConfig _config;
    private readonly IShellServices _shell;

    public BacklogStatusViewlet(ProjectsConfig config, IShellServices shell)
    {
        _config = config;
        _shell  = shell;
    }

    public string DisplayName    => "Backlog";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFileGlobs => [".project"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new BacklogStatusViewletView(_config, _shell, folderPath, controller);
}
