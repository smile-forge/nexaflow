using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Nexaflow.Core.Services;

public sealed class WorkspaceManager
{
    public static WorkspaceManager Instance { get; } = new();

    /// <summary>
    /// Saved workspace configurations — what the dropdown and taskbar JumpList show.
    /// Populated by <see cref="Initialize"/>; never contains runtime-only data.
    /// </summary>
    public ObservableCollection<Workspace> Workspaces { get; } = [];

    /// <summary>
    /// Live runtime workspaces. One is created per app/IPC launch; removed when its last window
    /// closes. Not exposed publicly — callers use <see cref="CreateWorkspace"/> / <see cref="SwitchWorkspace"/>.
    /// </summary>
    private readonly List<WorkspaceRuntime> _workspaces = [];

    /// <summary>
    /// Fired after <see cref="Initialize"/> or when workspaces are added/removed, so listeners
    /// (JumpList, ShellViewModel) can refresh their derived state.
    /// </summary>
    public event EventHandler? WorkspacesRefreshed;

    /// <summary>The first live workspace, or null when no windows are open (e.g. daemon mode).</summary>
    public WorkspaceRuntime? FirstActive => _workspaces.FirstOrDefault();

    /// <summary>True when at least one window is open across all live workspaces.</summary>
    internal bool AnyWindowsOpen
        => _workspaces.Any(w => w.ShellServices is { } s && s.HasWindows);

    private WorkspacesConfig? _savedConfig;

    /// <summary>
    /// Set by <c>App</c> at startup. Given a freshly-created <see cref="WorkspaceRuntime"/>, builds (but does
    /// not show) its first window host and wires the tear-off factory — the same logic App uses on
    /// launch. Lets <see cref="RestartWorkspace"/> spin up a new workspace+window without Core.Services
    /// referencing <c>MainWindow</c>.
    /// </summary>
    internal Func<WorkspaceRuntime, IWindowHost>? WindowHostFactory { get; set; }

    private WorkspaceManager() { }

    /// <summary>
    /// Loads workspace configurations (workspaces) from <paramref name="config"/>. Does NOT create
    /// any runtime <see cref="WorkspaceRuntime"/> objects — those are created lazily when windows open.
    /// Must be called AFTER <see cref="ProviderManager"/> has loaded its assemblies.
    /// </summary>
    public void Initialize(WorkspacesConfig config)
    {
        _savedConfig = config;
        Workspaces.Clear();

        var workspaces = config.Contexts is { Count: > 0 } saved ? saved : [new Workspace()];
        foreach (var p in workspaces)
            Workspaces.Add(p);

        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The reserved sub-folder of <c>Contexts\</c> that orphaned workspace data is moved into
    /// (rather than deleted) so a mis-fire is recoverable by hand. Never itself treated as an orphan.</summary>
    public const string OrphanQuarantineDir = "_orphaned";

    /// <summary>
    /// Quarantines data folders under <c>Contexts\</c> that no loaded workspace references — leftovers from a
    /// workspace removed out-of-band, or (the reason this exists) folders orphaned when an older build reset
    /// the workspace list on a version bump before forward-migration existed. Nothing rebuilds the list from
    /// these folders, so they are permanently unreachable dead weight. Instead of deleting, they are moved
    /// into <see cref="OrphanQuarantineDir"/> so a wrong call is recoverable; the user can clear that bin.
    ///
    /// HARD-GATED on <paramref name="listIsAuthoritative"/>: the list is only trustworthy when it actually
    /// loaded (or migrated) from disk. When it defaulted — missing or unreadable <c>workcontexts.json</c> —
    /// EVERY folder looks orphaned, and moving them all would be the very data-loss this guards against, so
    /// it is skipped entirely. Call once at startup, right after <see cref="Initialize"/>. Returns the
    /// folder names quarantined (for logging / tests).
    /// </summary>
    public IReadOnlyList<string> QuarantineOrphanedDataFolders(bool listIsAuthoritative)
    {
        if (!listIsAuthoritative) return [];

        var root = Path.Combine(ConfigManager.Instance.BaseDir, "Contexts");
        if (!Directory.Exists(root)) return [];

        // Folder name == workspace Name (see WorkspaceDir); match case-insensitively like the filesystem.
        var live       = Workspaces.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quarantine = Path.Combine(root, OrphanQuarantineDir);
        var moved      = new List<string>();

        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (live.Contains(name)) continue;                                                  // a live workspace's folder
            if (string.Equals(name, OrphanQuarantineDir, StringComparison.OrdinalIgnoreCase))
                continue;                                                                       // the quarantine bin itself
            try
            {
                Directory.CreateDirectory(quarantine);
                Directory.Move(dir, UniqueQuarantinePath(quarantine, name));
                moved.Add(name);
            }
            catch (Exception ex)
            {
                // A locked file leaves the folder in place rather than crashing startup — retried next launch.
                Debug.WriteLine($"[WorkspaceManager] orphan quarantine skipped '{name}': {ex.Message}");
            }
        }

        if (moved.Count > 0)
            Debug.WriteLine($"[WorkspaceManager] quarantined {moved.Count} orphaned workspace folder(s) into '{OrphanQuarantineDir}': {string.Join(", ", moved)}");

        return moved;
    }

    /// <summary>A destination inside <paramref name="quarantine"/> for <paramref name="name"/> that doesn't
    /// collide with one already quarantined under that name (appends " (2)", " (3)", …).</summary>
    private static string UniqueQuarantinePath(string quarantine, string name)
    {
        var dest = Path.Combine(quarantine, name);
        for (int i = 2; Directory.Exists(dest); i++)
            dest = Path.Combine(quarantine, $"{name} ({i})");
        return dest;
    }

    /// <summary>
    /// Creates a brand-new runtime <see cref="WorkspaceRuntime"/> for <paramref name="workspace"/> with
    /// fully-bootstrapped per-Workspace services. Always returns a fresh instance — used on every
    /// app/IPC launch (launching the app N times ⇒ N Workspaces, possibly all on one workspace).
    /// </summary>
    public WorkspaceRuntime CreateWorkspace(Workspace workspace)
    {
        workspace.EnsureSharedServicesLoaded();
        var ws = new WorkspaceRuntime(workspace);
        BootstrapServices(ws);
        _workspaces.Add(ws);
        return ws;
    }

    /// <summary>
    /// Switches <paramref name="ws"/> to a different <paramref name="target"/> workspace in place:
    /// repoints the workspace and reconfigures the runtime (providers + AIService), closing the
    /// Workspace's tabs. Other Workspaces (incl. ones still on the old workspace) are untouched.
    /// </summary>
    public void SwitchWorkspace(WorkspaceRuntime ws, Workspace target)
    {
        if (ReferenceEquals(ws.Workspace, target)) return;
        target.EnsureSharedServicesLoaded();
        ws.RepointWorkspace(target);
        ReconfigureWorkspace(ws);
    }

    /// <summary>
    /// Rebuilds <paramref name="ws"/>'s runtime for its CURRENT workspace: closes its tabs, evicts the
    /// feature cache (so handlers drop the dead AIService), releases the old provider set and
    /// acquires a fresh one from the workspace's configs (loading newly-needed providers and unloading
    /// now-unused ones), and rebuilds the AIService. Reopens a default tab in the focused window.
    /// Called by <see cref="SwitchWorkspace"/> and by the Manage-AI panel after a provider-config change.
    /// </summary>
    public void ReconfigureWorkspace(WorkspaceRuntime ws)
    {
        var workspace = ws.Workspace;

        // 1. Close all tabs across every window (their page-VMs captured the old services).
        ws.ShellServices?.CloseAllTabs();

        // 2. Drop cached feature/handler instances that captured the old AIService/IShellServices.
        FeatureManager.Instance.EvictWorkspace(ws);

        // 3. Rebuild the AIService for the (possibly new) workspace.
        var service = new AIService(ws, workspace.ConversationsDir);
        service.LoadAbilityConfig(workspace.AiConfig);
        ws.AiService = service;

        // 4. Re-acquire providers (pool reuses unchanged ones; warms newly-assigned models, cools
        //    now-unused ones — acquire before release so unchanged instances never drop to zero refs)
        //    and register the execution instances into the new AIService.
        AcquireAndRegister(ws);

        // 5. Reopen the workspace's saved startup tabs so the windows aren't left empty.
        ws.ShellServices?.OpenDefaultTabs(workspace.DefaultTabs);
    }

    /// <summary>
    /// Called by <see cref="ShellServices.UnregisterWindow"/> when a window closes. When the
    /// Workspace has no more windows, releases its providers, drops its feature cache, and removes it.
    /// </summary>
    public void NotifyWindowClosed(WorkspaceRuntime ws)
    {
        if (ws.ShellServices is { HasWindows: true }) return;

        FeatureManager.Instance.EvictWorkspace(ws);
        ProviderManager.Instance.ReleaseProviderSet(ws.Providers);   // cools any now-unused models
        ws.Providers = null;
        ws.AiService = null;
        _workspaces.Remove(ws);
    }

    /// <summary>
    /// Rebuilds <paramref name="ws"/>'s provider set from disk (reloading the workspace's provider
    /// configs so newly discovered providers appear) and re-registers the execution instances into its
    /// AIService. Called when the AI options panel opens.
    /// </summary>
    public void RefreshProviders(WorkspaceRuntime ws)
    {
        ws.Workspace.ReloadProviderConfigs();
        AcquireAndRegister(ws);
    }

    /// <summary>
    /// Re-acquires <paramref name="ws"/>'s provider set for its current ability-grid columns (without
    /// touching tabs) and re-registers the execution instances. Called after the ability grid is edited
    /// so newly-assigned models get an execution instance (and are warmed) and dropped ones are cooled.
    /// </summary>
    public void SyncProviders(WorkspaceRuntime ws) => AcquireAndRegister(ws);

    /// <summary>Adds a new workspace with a random icon/colour to <see cref="Workspaces"/>.</summary>
    public Workspace AddWorkspace(string name)
    {
        var (icon, color) = WorkspaceStyle.Random();
        var workspace = new Workspace { Name = name, Icon = icon, Color = color };
        Workspaces.Add(workspace);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return workspace;
    }

    /// <summary>
    /// Creates a new workspace whose AI config + provider configs are copied from
    /// <paramref name="source"/>'s folder, with a randomised icon/colour.
    /// </summary>
    public Workspace CloneWorkspace(Workspace source, string name)
    {
        var (icon, color) = WorkspaceStyle.Random();
        var workspace = new Workspace { Name = name, Icon = icon, Color = color };
        var destDir = WorkspaceDir(name);

        source.EnsureSharedServicesLoaded();
        ConfigManager.Instance.SaveTo(destDir, source.AiConfig, source.AiConfig.ConfigName);
        ConfigManager.Instance.SaveTo(destDir, source.Persona, source.Persona.ConfigName);
        foreach (var cfg in source.ProviderConfigs)
            ConfigManager.Instance.SaveTo(destDir, cfg, cfg.ConfigName);
        foreach (var cfg in source.WorkspaceConfigs)
            ConfigManager.Instance.SaveTo(destDir, cfg, cfg.ConfigName);

        Workspaces.Add(workspace);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return workspace;
    }

    /// <summary>A workspace name not already in use (appends " 2", " 3", … on collision, case-insensitive).</summary>
    public string UniqueWorkspaceName(string baseName)
    {
        var taken = Workspaces.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Renames <paramref name="workspace"/> to <paramref name="newName"/>, moving its on-disk data folder
    /// so its AI / provider / conversation config follows the new name (otherwise the workspace would
    /// silently start from an empty folder). Always re-persists the workspace list, so an unchanged name
    /// with edited colour/icon is saved too. The caller must ensure the name is non-empty and not
    /// already used by another workspace (which would collide on the folder move).
    /// </summary>
    public void RenameWorkspace(Workspace workspace, string newName)
    {
        if (!string.Equals(workspace.Name, newName, StringComparison.Ordinal))
        {
            var oldDir = WorkspaceDir(workspace.Name);
            var newDir = WorkspaceDir(newName);
            // Skip the move on a case-only rename (same path on Windows) or when there's nothing to move.
            if (!string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(oldDir) && !Directory.Exists(newDir))
                Directory.Move(oldDir, newDir);
            workspace.Name = newName;   // observable → selector/ribbon update; Dir now resolves to newDir
        }
        SaveWorkspaces();
    }

    /// <summary>
    /// Removes <paramref name="workspace"/> from <see cref="Workspaces"/> and deletes its on-disk data
    /// folder (config + conversations). At least one workspace always remains. The caller (Options editor)
    /// prevents removing the active workspace and persists the list. Returns true if removed.
    /// </summary>
    public bool RemoveWorkspace(Workspace workspace)
    {
        if (Workspaces.Count <= 1) return false;
        if (!Workspaces.Remove(workspace)) return false;
        DeleteWorkspaceFolder(workspace.Name);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Permanently deletes <paramref name="workspace"/>: removes it from the list, deletes its on-disk
    /// data folder (config + every conversation), and persists the list. Refuses to delete the last
    /// remaining workspace. By default also refuses one a live workspace is using; pass
    /// <paramref name="force"/> when the caller has already (or is about to) tear down that workspace's
    /// windows — used by the Configure panel's "Delete Workspace" action, which has no separate Save step.
    /// Returns true when deleted.
    /// </summary>
    public bool DeleteWorkspace(Workspace workspace, bool force = false)
    {
        if (!force && IsWorkspaceInUse(workspace)) return false;
        if (!RemoveWorkspace(workspace)) return false;   // removes + deletes the folder
        SaveWorkspaces();
        return true;
    }

    /// <summary>Deletes a workspace's data folder, swallowing IO failures (a locked file orphans the
    /// folder rather than crashing the delete).</summary>
    private static void DeleteWorkspaceFolder(string name)
    {
        try
        {
            var dir = WorkspaceDir(name);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* leave an orphaned folder rather than fail the removal */ }
    }

    /// <summary>True if any live workspace is currently running <paramref name="workspace"/>.</summary>
    public bool IsWorkspaceInUse(Workspace workspace)
        => _workspaces.Any(w => ReferenceEquals(w.Workspace, workspace));

    /// <summary>The live workspace running <paramref name="workspace"/>, or null when none is open.</summary>
    public WorkspaceRuntime? FindLiveWorkspace(Workspace workspace)
        => _workspaces.FirstOrDefault(w => ReferenceEquals(w.Workspace, workspace));

    /// <summary>
    /// Rebuilds <paramref name="old"/> as a brand-new workspace on the same workspace and swaps its
    /// window: snapshots the focused window's tabs, creates a fresh workspace (fresh ShellServices /
    /// AIService / providers / file-system registry that pick up the just-saved per-workspace config),
    /// opens a replacement window where the old one was, reopens the tabs, then closes the old
    /// workspace's windows (its last close disposes it). Used by the Configure panel after a
    /// provider-config or workspace-scoped feature-config change — the theme-restart pattern, but
    /// swapping the whole workspace. Falls back to <see cref="ReconfigureWorkspace"/> if no window
    /// factory is wired (e.g. headless).
    /// </summary>
    internal void RestartWorkspace(WorkspaceRuntime old)
    {
        var acting = old.ShellServices?.FocusedWindow;
        if (acting is null || WindowHostFactory is null) { ReconfigureWorkspace(old); return; }

        // Snapshot the acting window's tabs grouped by pane (so a left/right split survives) and its placement.
        var snapshot  = acting.CaptureTabLayout();
        var src       = acting.Window;
        bool maximized = src.WindowState == WindowState.Maximized;
        var bounds    = maximized ? src.RestoreBounds
                                  : new Rect(src.Left, src.Top, src.Width, src.Height);

        // Fresh workspace on the same workspace — the shared Workspace already carries the saved edits.
        var fresh = CreateWorkspace(old.Workspace);

        // Replacement window, placed where the old one was, shown BEFORE the old closes so the
        // workspace is never left window-less (which would dispose it / shut the app down).
        var freshHost = WindowHostFactory(fresh);
        var dst = freshHost.Window;
        dst.WindowStartupLocation = WindowStartupLocation.Manual;
        dst.Left = bounds.Left; dst.Top = bounds.Top; dst.Width = bounds.Width; dst.Height = bounds.Height;
        dst.Show();
        if (maximized) dst.WindowState = WindowState.Maximized;

        // Tear down the old workspace's windows (last close releases its providers + evicts caches).
        old.ShellServices?.CloseAllWindows();

        // Restore the captured pane layout (split + each pane's tabs + active tab) into the fresh window.
        fresh.ShellServices!.RestoreTabLayout(snapshot);
    }

    /// <summary>Per-workspace data folder: <c>%AppData%\…\Contexts\&lt;name&gt;</c> (folder name kept for compat).</summary>
    public static string WorkspaceDir(string name)
        => Path.Combine(ConfigManager.Instance.BaseDir, "Contexts", name);

    /// <summary>Persists the workspace list (names/colours/icons only).</summary>
    public void SaveWorkspaces()
    {
        if (_savedConfig is null) return;
        _savedConfig.Contexts = [.. Workspaces];
        ConfigManager.Instance.Save(_savedConfig, _savedConfig.ConfigName);
    }

    /// <summary>Persists one workspace's AI ability config to its own folder.</summary>
    public void SaveWorkspaceAiConfig(Workspace workspace)
        => ConfigManager.Instance.SaveTo(WorkspaceDir(workspace.Name), workspace.AiConfig, workspace.AiConfig.ConfigName);

    /// <summary>Persists one workspace's assistant persona (name + system prompt) to its own folder.</summary>
    public void SaveWorkspacePersona(Workspace workspace)
        => ConfigManager.Instance.SaveTo(WorkspaceDir(workspace.Name), workspace.Persona, workspace.Persona.ConfigName);

    /// <summary>Persists one of a workspace's per-workspace feature configs to its own folder.</summary>
    public void SaveWorkspaceScopedConfig(Workspace workspace, IFeatureConfig config)
        => ConfigManager.Instance.SaveTo(WorkspaceDir(workspace.Name), config, config.ConfigName);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a fresh provider set for <paramref name="ws"/>'s current configs and assigned grid
    /// columns, swaps it in (acquire-before-release, so the pool warms newly-needed models and cools
    /// dropped ones), and re-registers the execution instances into the AIService keyed by column id.
    /// </summary>
    private static void AcquireAndRegister(WorkspaceRuntime ws)
    {
        var workspace = ws.Workspace;

        var fresh = ProviderManager.Instance.AcquireProviderSet(
            workspace.ProviderConfigs, AssignedColumns(workspace.AiConfig));
        var old   = ws.Providers;
        ws.Providers               = fresh;
        workspace.AiConfig.Providers = fresh;
        ProviderManager.Instance.ReleaseProviderSet(old);

        if (ws.AiService is { } svc)
        {
            svc.ClearProviders();
            foreach (var (columnId, provider) in fresh.Execution)
                svc.Register(columnId, provider);
        }
    }

    /// <summary>The grid columns actually assigned to an ability — the models in use (need an instance).</summary>
    private static IReadOnlyList<ProviderModelPair> AssignedColumns(AiConfig cfg)
    {
        var assigned = cfg.Assignments.Values.Where(v => !string.IsNullOrEmpty(v)).ToHashSet();
        return [.. cfg.Columns.Where(c => assigned.Contains(c.Id))];
    }

    private static void BootstrapServices(WorkspaceRuntime ws)
    {
        var workspace = ws.Workspace;

        var service = new AIService(ws, workspace.ConversationsDir);
        service.LoadAbilityConfig(workspace.AiConfig);
        ws.AiService = service;

        AcquireAndRegister(ws);

        ws.ShellServices ??= new ShellServices(ws, ProviderManager.Instance.ActivityManager);
    }
}
