using Nexaflow.Core.Localization;
using Nexaflow.Core.Services.Logging;

namespace Nexaflow.Core.Services;

/// <summary>What a shell window is given of the session it runs in.</summary>
/// <param name="Config">The session's configuration: what the window's Options and Configure panels list and save, and the setup
/// a workspace it adds is asked for.</param>
/// <param name="Shell">The shell's settings: the theme and language a window restarts for when Options changes them.</param>
/// <param name="Activities">The session's background tasks, which the window's activity ticker shows.</param>
/// <param name="Workspaces">The live workspaces, which the window moves between.</param>
/// <param name="Inbox">The session's inbox: the window shows every message in it and toasts the new ones.</param>
/// <param name="Dependencies">The third-party components the features declare, which the window's About page lists.</param>
/// <param name="Language">The UI language: the window's Options lists the installed ones, and restarts the window for a new one.</param>

/// <param name="Features">The feature system: the pages the window opens, the handlers its chat bar and ribbon offer, and what
/// its Options and Configure panels list.</param>
/// <param name="Providers">The provider pool: the AI host the window's AI line watches, and the providers and models on this
/// machine its Configure panel and a new workspace's setup offer.</param>
/// <param name="SavedWorkspaces">The saved workspaces: the window's selector lists them, and its Options and Configure panels
/// add, rename and delete them.</param>
/// <param name="Updates">Where this build stands against its newest release, which the window's About page shows.</param>
/// <param name="Logging">The process's logs: what the window logs to, and the session's log the window's Options turns on and off.</param>
public sealed record WindowServices(
    ConfigManager Config,
    ShellConfig Shell,
    BackgroundActivityManager Activities,
    WorkspaceRuntimes Workspaces,
    MessageCenter Inbox,
    ExternalDependencyRegistry Dependencies,
    LanguageManager Language,

    FeatureManager Features,
    ProviderManager Providers,
    WorkspaceManager SavedWorkspaces,
    AppUpdates Updates,
    ShellLogging Logging);
