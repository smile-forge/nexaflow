using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// What the Workspaces page (<see cref="Controls.WorkspaceConfigControl"/>) is bound to: the saved list it edits, which of them a
/// window shows, and the setup a workspace added there is taken through.
/// </summary>
/// <param name="config">The saved workspace list the page edits.</param>
/// <param name="session">What the window has of the session: the saved workspaces and live runtimes the page edits and guards,
/// and the feature system and provider pool a new workspace's setup asks with.</param>
internal sealed class WorkspacesConfigViewModel(WorkspacesConfig config, WindowServices session)
{
    /// <summary>The saved workspace list the page edits.</summary>
    public WorkspacesConfig Config => config;

    /// <summary>The saved workspaces the page adds to, clones, reorders and removes from.</summary>
    public WorkspaceManager Saved => session.SavedWorkspaces;

    /// <summary>The live workspaces: one a window shows is not removed, and one removed while dormant ends with it.</summary>
    public WorkspaceRuntimes Runtimes => session.Workspaces;

    /// <summary>The setup wizard for <paramref name="workspace"/>, just added: its provider, key and model, and its mandatory
    /// configs.</summary>
    public SetupWizardViewModel SetupFor(Workspace workspace)
        => SetupWizardViewModel.BuildWorkspaceSetup(workspace, session.Features, session.Providers, session.SavedWorkspaces);
}
