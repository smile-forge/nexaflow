namespace Nexaflow.Core.Services;

/// <summary>What a shell window is given of the session it runs in.</summary>
/// <param name="Activities">The session's background tasks, which the window's activity ticker shows.</param>
/// <param name="Workspaces">The live workspaces, which the window moves between.</param>
/// <param name="Inbox">The session's inbox: the window shows every message in it and toasts the new ones.</param>
public sealed record WindowServices(BackgroundActivityManager Activities, WorkspaceRuntimes Workspaces, MessageCenter Inbox);
