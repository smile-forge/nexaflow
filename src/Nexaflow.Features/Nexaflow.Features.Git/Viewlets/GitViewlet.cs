using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System.Windows;

namespace Nexaflow.Features.Git.Viewlets;

public sealed class GitViewlet : IFolderViewlet
{
    private readonly GitOptions     _options;
    private readonly IShellServices _shell;

    public GitViewlet(GitOptions options, IShellServices shell)
    {
        _options = options;
        _shell   = shell;
    }

    public string DisplayName    => "Git";
    public bool   AppliesToDrives => false;

    // A normal checkout has a ".git" *directory*. A linked worktree instead has a ".git" *file* and is
    // handled by the separate GitWorktreeViewlet — the two are mutually exclusive per folder.
    public string[]? ContainsFolderGlobs => [".git"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new GitViewletView(_options, _shell, folderPath, controller);
}
