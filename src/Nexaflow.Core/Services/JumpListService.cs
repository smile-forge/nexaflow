using System;
using System.Windows;
using System.Windows.Shell;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds and applies the Windows taskbar JumpList — one task per <see cref="Models.WorkContext"/>,
/// each launching the app with <c>--context "Name"</c> so it opens straight into that context.
/// Rebuilt whenever the context list changes (e.g. the user edits contexts in Options).
/// </summary>
public static class JumpListService
{
    /// <summary>Command-line switch a JumpTask passes to request a specific WorkContext.</summary>
    public const string ContextSwitch = "--context";

    private const string CategoryName = "Work Contexts";

    /// <summary>
    /// Applies the initial JumpList and subscribes to context-list changes so it stays in sync.
    /// Call once after <see cref="WorkContextManager"/> has been initialised.
    /// </summary>
    public static void Initialize()
    {
        WorkContextManager.Instance.ContextsRefreshed += (_, _) =>
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

        foreach (var cfg in WorkContextManager.Instance.Configs)
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
