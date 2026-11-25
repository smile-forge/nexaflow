using Nexaflow.Core.Models;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// What the Workspaces page (<see cref="Controls.WorkspaceConfigControl"/>) is bound to: the saved list it edits, and the setup a
/// workspace added there is taken through.
/// </summary>
/// <param name="config">The saved workspace list the page edits.</param>
/// <param name="features">The session's feature system, which knows the workspace-scoped configs a new workspace's setup asks for.</param>
internal sealed class WorkspacesConfigViewModel(WorkspacesConfig config, FeatureManager features)
{
    /// <summary>The saved workspace list the page edits.</summary>
    public WorkspacesConfig Config => config;

    /// <summary>The setup wizard for <paramref name="workspace"/>, just added: its provider, key and model, and its mandatory
    /// configs.</summary>
    public SetupWizardViewModel SetupFor(Workspace workspace) => SetupWizardViewModel.BuildWorkspaceSetup(workspace, features);
}
