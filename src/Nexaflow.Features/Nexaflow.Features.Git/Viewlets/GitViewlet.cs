using Nexaflow.Features.Common.Viewlets;
using System.Windows;

namespace Nexaflow.Features.Git.Viewlets;

public sealed class GitViewlet : IFolderViewlet
{
    private readonly GitOptions _options;

    public GitViewlet(GitOptions options) => _options = options;

    public string DisplayName    => "Git";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFolderGlobs => [".git"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new GitViewletView(_options, folderPath, controller);
}
