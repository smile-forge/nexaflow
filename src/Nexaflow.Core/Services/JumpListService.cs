using System;
using System.Windows;
using System.Windows.Shell;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds and applies the Windows taskbar JumpList — one task per <see cref="Models.Profile"/>,
/// each launching the app with <c>--context "Name"</c> so it opens straight into that profile.
/// Rebuilt whenever the profile list changes (e.g. the user edits profiles in Options).
/// </summary>
public static class JumpListService
{
    /// <summary>Command-line switch a JumpTask passes to request a specific WorkContext.</summary>
    public const string ContextSwitch = "--context";

    private const string CategoryName = "Workspaces";

    /// <summary>
    /// Applies the initial JumpList and subscribes to context-list changes so it stays in sync.
    /// Call once after <see cref="WorkspaceManager"/> has been initialised.
    /// </summary>
    public static void Initialize()
    {
        WorkspaceManager.Instance.ProfilesRefreshed += (_, _) =>
            Application.Current?.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    /// <summary>Rebuilds the JumpList from the current set of WorkContexts.</summary>
    public static void Refresh()
    {
        if (Application.Current is null) return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var jumpList = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

        foreach (var cfg in WorkspaceManager.Instance.Profiles)
            jumpList.JumpItems.Add(new JumpTask
            {
                Title            = cfg.Name,
                Description      = $"Open the “{cfg.Name}” work context",
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
