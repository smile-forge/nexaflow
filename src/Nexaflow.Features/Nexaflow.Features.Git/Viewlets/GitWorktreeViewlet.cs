using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System.Windows;

namespace Nexaflow.Features.Git.Viewlets;

/// <summary>
/// The Git viewlet for a linked worktree. A worktree folder's <c>.git</c> is a <em>file</em> (a
/// <c>gitdir: …</c> pointer at the main repo), not a directory, so the normal <see cref="GitViewlet"/>
/// (which matches a <c>.git</c> directory) never fires here. Rather than overloading the shared
/// file/folder-glob contract, this is a distinct <see cref="IFolderViewlet"/> that matches the <c>.git</c>
/// file. It renders the same <see cref="GitViewletView"/>, which detects the worktree at runtime and shows
/// the worktree banner + removal control.
/// </summary>
public sealed class GitWorktreeViewlet : IFolderViewlet
{
    private readonly GitOptions     _options;
    private readonly IShellServices _shell;

    public GitWorktreeViewlet(GitOptions options, IShellServices shell)
    {
        _options = options;
        _shell   = shell;
    }

    public string DisplayName    => "Git";
    public bool   AppliesToDrives => false;

    // A linked worktree is identified by its ".git" being a file (the pointer), which only ContainsFileGlobs
    // sees. The main checkout has a ".git" directory and is served by GitViewlet — mutually exclusive.
    public string[]? ContainsFileGlobs => [".git"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new GitViewletView(_options, _shell, folderPath, controller);
}
