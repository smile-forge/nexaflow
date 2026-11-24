using Nexaflow.Core.Localization;

namespace Nexaflow.Core.Services;

/// <summary>What a shell window is given of the session it runs in.</summary>
/// <param name="Activities">The session's background tasks, which the window's activity ticker shows.</param>
/// <param name="Workspaces">The live workspaces, which the window moves between.</param>
/// <param name="Inbox">The session's inbox: the window shows every message in it and toasts the new ones.</param>
/// <param name="Dependencies">The third-party components the features declare, which the window's About page lists.</param>
/// <param name="Language">The UI language: the window's Options lists the installed ones, and restarts the window for a new one.</param>
/// <param name="Views">The view each open page is drawn with, which the window's panes show.</param>
public sealed record WindowServices(
    BackgroundActivityManager Activities,
    WorkspaceRuntimes Workspaces,
    MessageCenter Inbox,
    ExternalDependencyRegistry Dependencies,
    LanguageManager Language,
    PageViews Views);
