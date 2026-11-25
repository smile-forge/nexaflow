using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.ViewModels;

/// <summary>What the About page (<see cref="Controls.AboutControl"/>) is bound to: the session's components it lists and re-checks,
/// the update it offers, and the window that asks before a reset.</summary>
/// <param name="dependencies">The third-party components the features declare, and what the last probe found of each.</param>
/// <param name="updates">Where this build stands against its newest release — the startup check's, shared with its toast.</param>
/// <param name="runtimes">The live workspaces, a window of which asks before a reset.</param>
internal sealed class AboutViewModel(ExternalDependencyRegistry dependencies, AppUpdates updates, WorkspaceRuntimes runtimes)
{
    /// <summary>The third-party components the features declare, and what the last probe found of each.</summary>
    public ExternalDependencyRegistry Dependencies => dependencies;

    /// <summary>Where this build stands against its newest release: the startup check, a toast's Update or the page's own buttons
    /// move it on.</summary>
    public AppUpdates Updates => updates;

    /// <summary>The shell of a window open on the session, which asks before a reset; null while none is open.</summary>
    public IShellServices? Shell => runtimes.FirstWithWindows?.ShellServices;
}
