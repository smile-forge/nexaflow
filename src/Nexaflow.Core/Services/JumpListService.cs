using System;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds and applies the Windows taskbar JumpList — one task per <see cref="Models.Workspace"/>,
/// each launching the app with <c>--context "Name"</c> so it opens straight into that workspace.
/// Rebuilt whenever the workspace list changes (e.g. the user edits workspaces in Options).
/// </summary>
public static class JumpListService
{
    /// <summary>Command-line switch a JumpTask passes to request a specific Workspace
    /// (the "--context" literal is a frozen CLI/IPC compat string).</summary>
    public const string ContextSwitch = "--context";

    private const string CategoryName = "Workspaces";

    /// <summary>
    /// Applies the initial JumpList from <paramref name="savedWorkspaces"/> and follows its changes so it stays in sync.
    /// </summary>
    public static void Initialize(WorkspaceManager savedWorkspaces)
    {
        // Captured on the UI thread (this runs as the app opens the session); the workspace-list change can
        // arrive on any thread, so marshal the rebuild back onto it.
        var ui = Dispatcher.CurrentDispatcher;
        savedWorkspaces.WorkspacesRefreshed += (_, _) => ui.Invoke(() => Refresh(savedWorkspaces.Workspaces));
        Refresh(savedWorkspaces.Workspaces);
    }

    /// <summary>Rebuilds the JumpList from the saved <paramref name="workspaces"/>.</summary>
    public static void Refresh(IEnumerable<Models.Workspace> workspaces)
    {
        if (Application.Current is null) return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var jumpList = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

        foreach (var cfg in workspaces)
            jumpList.JumpItems.Add(new JumpTask
            {
                Title            = cfg.Name,
                Description      = Str.Format("Shell.JumpList.OpenFormat", cfg.Name),
                CustomCategory   = CategoryName,
                ApplicationPath  = exePath,
                IconResourcePath = exePath,
                Arguments        = $"{ContextSwitch} \"{cfg.Name}\"",
            });

        try
        {
            JumpList.SetJumpList(Application.Current, jumpList);
            jumpList.Apply();
        }
        catch { /* JumpList is cosmetic — never let it break startup */ }
    }
}
