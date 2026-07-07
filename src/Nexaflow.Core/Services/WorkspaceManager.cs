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
    public ObservableCollection<Profile> Profiles { get; } = [];

    /// <summary>
    /// Live runtime workspaces. One is created per app/IPC launch; removed when its last window
    /// closes. Not exposed publicly — callers use <see cref="CreateWorkspace"/> / <see cref="SwitchProfile"/>.
    /// </summary>
    private readonly List<Workspace> _workspaces = [];

    /// <summary>
    /// Fired after <see cref="Initialize"/> or when profiles are added/removed, so listeners
    /// (JumpList, ShellViewModel) can refresh their derived state.
    /// </summary>
    public event EventHandler? ProfilesRefreshed;

    /// <summary>The first live workspace, or null when no windows are open (e.g. daemon mode).</summary>
    public Workspace? FirstActive => _workspaces.FirstOrDefault();

    /// <summary>True when at least one window is open across all live workspaces.</summary>
    internal bool AnyWindowsOpen
        => _workspaces.Any(w => w.ShellServices is { } s && s.HasWindows);

    private WorkspacesConfig? _savedConfig;

    /// <summary>
    /// Set by <c>App</c> at startup. Given a freshly-created <see cref="Workspace"/>, builds (but does
    /// not show) its first window host and wires the tear-off factory — the same logic App uses on
    /// launch. Lets <see cref="RestartWorkspace"/> spin up a new workspace+window without Core.Services
    /// referencing <c>MainWindow</c>.
    /// </summary>
    internal Func<Workspace, IWindowHost>? WindowHostFactory { get; set; }

    private WorkspaceManager() { }

    /// <summary>
    /// Loads workspace configurations (profiles) from <paramref name="config"/>. Does NOT create
    /// any runtime <see cref="Workspace"/> objects — those are created lazily when windows open.
    /// Must be called AFTER <see cref="ProviderManager"/> has loaded its assemblies.
    /// </summary>
    public void Initialize(WorkspacesConfig config)
    {
        _savedConfig = config;
        Profiles.Clear();

        var profiles = config.Contexts is { Count: > 0 } saved ? saved : [new Profile()];
        foreach (var p in profiles)
            Profiles.Add(p);

        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The reserved sub-folder of <c>Contexts\</c> that orphaned workspace data is moved into
    /// (rather than deleted) so a mis-fire is recoverable by hand. Never itself treated as an orphan.</summary>
    public const string OrphanQuarantineDir = "_orphaned";

    /// <summary>
    /// Quarantines data folders under <c>Contexts\</c> that no loaded profile references — leftovers from a
    /// profile removed out-of-band, or (the reason this exists) folders orphaned when an older build reset
    /// the profile list on a version bump before forward-migration existed. Nothing rebuilds the list from
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

        // Folder name == profile Name (see ProfileDir); match case-insensitively like the filesystem.
        var live       = Profiles.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quarantine = Path.Combine(root, OrphanQuarantineDir);
        var moved      = new List<string>();

        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (live.Contains(name)) continue;                                                  // a live profile's folder
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
    /// Creates a brand-new runtime <see cref="Workspace"/> for <paramref name="profile"/> with
    /// fully-bootstrapped per-Workspace services. Always returns a fresh instance — used on every
    /// app/IPC launch (launching the app N times ⇒ N Workspaces, possibly all on one profile).
    /// </summary>
    public Workspace CreateWorkspace(Profile profile)
    {
        profile.EnsureSharedServicesLoaded();
        var ws = new Workspace(profile);
        BootstrapServices(ws);
        _workspaces.Add(ws);
        return ws;
    }

    /// <summary>
    /// Switches <paramref name="ws"/> to a different <paramref name="target"/> profile in place:
    /// repoints the profile and reconfigures the runtime (providers + AIService), closing the
    /// Workspace's tabs. Other Workspaces (incl. ones still on the old profile) are untouched.
    /// </summary>
    public void SwitchProfile(Workspace ws, Profile target)
    {
        if (ReferenceEquals(ws.Profile, target)) return;
        target.EnsureSharedServicesLoaded();
        ws.RepointProfile(target);
        ReconfigureWorkspace(ws);
    }

    /// <summary>
    /// Rebuilds <paramref name="ws"/>'s runtime for its CURRENT profile: closes its tabs, evicts the
    /// feature cache (so handlers drop the dead AIService), releases the old provider set and
    /// acquires a fresh one from the profile's configs (loading newly-needed providers and unloading
    /// now-unused ones), and rebuilds the AIService. Reopens a default tab in the focused window.
    /// Called by <see cref="SwitchProfile"/> and by the Manage-AI panel after a provider-config change.
    /// </summary>
    public void ReconfigureWorkspace(Workspace ws)
    {
        var profile = ws.Profile;

        // 1. Close all tabs across every window (their page-VMs captured the old services).
        ws.ShellServices?.CloseAllTabs();

        // 2. Drop cached feature/handler instances that captured the old AIService/IShellServices.
        FeatureManager.Instance.EvictWorkspace(ws);

        // 3. Rebuild the AIService for the (possibly new) profile.
        var service = new AIService(ws, profile.ConversationsDir);
        service.LoadAbilityConfig(profile.AiConfig);
        ws.AiService = service;

        // 4. Re-acquire providers (pool reuses unchanged ones; warms newly-assigned models, cools
        //    now-unused ones — acquire before release so unchanged instances never drop to zero refs)
        //    and register the execution instances into the new AIService.
        AcquireAndRegister(ws);

        // 5. Reopen the profile's saved startup tabs so the windows aren't left empty.
        ws.ShellServices?.OpenDefaultTabs(profile.DefaultTabs);
    }

    /// <summary>
    /// Called by <see cref="ShellServices.UnregisterWindow"/> when a window closes. When the
    /// Workspace has no more windows, releases its providers, drops its feature cache, and removes it.
    /// </summary>
    public void NotifyWindowClosed(Workspace ws)
    {
        if (ws.ShellServices is { HasWindows: true }) return;

        FeatureManager.Instance.EvictWorkspace(ws);
        ProviderManager.Instance.ReleaseProviderSet(ws.Providers);   // cools any now-unused models
        ws.Providers = null;
        ws.AiService = null;
        _workspaces.Remove(ws);
    }

    /// <summary>
    /// Rebuilds <paramref name="ws"/>'s provider set from disk (reloading the profile's provider
    /// configs so newly discovered providers appear) and re-registers the execution instances into its
    /// AIService. Called when the AI options panel opens.
    /// </summary>
    public void RefreshProviders(Workspace ws)
    {
        ws.Profile.ReloadProviderConfigs();
        AcquireAndRegister(ws);
    }

    /// <summary>
    /// Re-acquires <paramref name="ws"/>'s provider set for its current ability-grid columns (without
    /// touching tabs) and re-registers the execution instances. Called after the ability grid is edited
    /// so newly-assigned models get an execution instance (and are warmed) and dropped ones are cooled.
    /// </summary>
    public void SyncProviders(Workspace ws) => AcquireAndRegister(ws);

    /// <summary>Adds a new profile with a random icon/colour to <see cref="Profiles"/>.</summary>
    public Profile AddProfile(string name)
    {
        var (icon, color) = ProfileStyle.Random();
        var profile = new Profile { Name = name, Icon = icon, Color = color };
        Profiles.Add(profile);
        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    /// <summary>
    /// Creates a new profile whose AI config + provider configs are copied from
    /// <paramref name="source"/>'s folder, with a randomised icon/colour.
    /// </summary>
    public Profile CloneProfile(Profile source, string name)
    {
        var (icon, color) = ProfileStyle.Random();
        var profile = new Profile { Name = name, Icon = icon, Color = color };
        var destDir = ProfileDir(name);

        source.EnsureSharedServicesLoaded();
        ConfigManager.Instance.SaveTo(destDir, source.AiConfig, source.AiConfig.ConfigName);
        ConfigManager.Instance.SaveTo(destDir, source.Persona, source.Persona.ConfigName);
        foreach (var cfg in source.ProviderConfigs)
            ConfigManager.Instance.SaveTo(destDir, cfg, cfg.ConfigName);
        foreach (var cfg in source.WorkspaceConfigs)
            ConfigManager.Instance.SaveTo(destDir, cfg, cfg.ConfigName);

        Profiles.Add(profile);
        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    /// <summary>A profile name not already in use (appends " 2", " 3", … on collision, case-insensitive).</summary>
    public string UniqueProfileName(string baseName)
    {
        var taken = Profiles.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Renames <paramref name="profile"/> to <paramref name="newName"/>, moving its on-disk data folder
    /// so its AI / provider / conversation config follows the new name (otherwise the profile would
    /// silently start from an empty folder). Always re-persists the profile list, so an unchanged name
    /// with edited colour/icon is saved too. The caller must ensure the name is non-empty and not
    /// already used by another profile (which would collide on the folder move).
    /// </summary>
    public void RenameProfile(Profile profile, string newName)
    {
        if (!string.Equals(profile.Name, newName, StringComparison.Ordinal))
        {
            var oldDir = ProfileDir(profile.Name);
            var newDir = ProfileDir(newName);
            // Skip the move on a case-only rename (same path on Windows) or when there's nothing to move.
            if (!string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(oldDir) && !Directory.Exists(newDir))
                Directory.Move(oldDir, newDir);
            profile.Name = newName;   // observable → selector/ribbon update; Dir now resolves to newDir
        }
        SaveProfiles();
    }

    /// <summary>
    /// Removes <paramref name="profile"/> from <see cref="Profiles"/> and deletes its on-disk data
    /// folder (config + conversations). At least one profile always remains. The caller (Options editor)
    /// prevents removing the active profile and persists the list. Returns true if removed.
    /// </summary>
    public bool RemoveProfile(Profile profile)
    {
        if (Profiles.Count <= 1) return false;
        if (!Profiles.Remove(profile)) return false;
        DeleteProfileFolder(profile.Name);
        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Permanently deletes <paramref name="profile"/>: removes it from the list, deletes its on-disk
    /// data folder (config + every conversation), and persists the list. Refuses to delete the last
    /// remaining profile. By default also refuses one a live workspace is using; pass
    /// <paramref name="force"/> when the caller has already (or is about to) tear down that workspace's
    /// windows — used by the Configure panel's "Delete Workspace" action, which has no separate Save step.
    /// Returns true when deleted.
    /// </summary>
    public bool DeleteProfile(Profile profile, bool force = false)
    {
        if (!force && IsProfileInUse(profile)) return false;
        if (!RemoveProfile(profile)) return false;   // removes + deletes the folder
        SaveProfiles();
        return true;
    }

    /// <summary>Deletes a profile's data folder, swallowing IO failures (a locked file orphans the
    /// folder rather than crashing the delete).</summary>
    private static void DeleteProfileFolder(string name)
    {
        try
        {
            var dir = ProfileDir(name);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* leave an orphaned folder rather than fail the removal */ }
    }

    /// <summary>True if any live workspace is currently running <paramref name="profile"/>.</summary>
    public bool IsProfileInUse(Profile profile)
        => _workspaces.Any(w => ReferenceEquals(w.Profile, profile));

    /// <summary>The live workspace running <paramref name="profile"/>, or null when none is open.</summary>
    public Workspace? FindLiveWorkspace(Profile profile)
        => _workspaces.FirstOrDefault(w => ReferenceEquals(w.Profile, profile));

    /// <summary>
    /// Rebuilds <paramref name="old"/> as a brand-new workspace on the same profile and swaps its
    /// window: snapshots the focused window's tabs, creates a fresh workspace (fresh ShellServices /
    /// AIService / providers / file-system registry that pick up the just-saved per-workspace config),
    /// opens a replacement window where the old one was, reopens the tabs, then closes the old
    /// workspace's windows (its last close disposes it). Used by the Configure panel after a
    /// provider-config or workspace-scoped feature-config change — the theme-restart pattern, but
    /// swapping the whole workspace. Falls back to <see cref="ReconfigureWorkspace"/> if no window
    /// factory is wired (e.g. headless).
    /// </summary>
    internal void RestartWorkspace(Workspace old)
    {
        var acting = old.ShellServices?.FocusedWindow;
        if (acting is null || WindowHostFactory is null) { ReconfigureWorkspace(old); return; }

        // Snapshot the acting window's tabs (kind/params/active) and its placement.
        var snapshot = acting.Tabs
            .Where(t => !string.IsNullOrEmpty(t.PageKind))
            .Select(t => (Kind: t.PageKind!, t.PageParams, t.IsActive))
            .ToList();
        var src       = acting.Window;
        bool maximized = src.WindowState == WindowState.Maximized;
        var bounds    = maximized ? src.RestoreBounds
                                  : new Rect(src.Left, src.Top, src.Width, src.Height);

        // Fresh workspace on the same profile — the shared Profile already carries the saved edits.
        var fresh = CreateWorkspace(old.Profile);

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

        // Reopen in reverse (AddTab prepends) so the original left-to-right order is preserved.
        for (int i = snapshot.Count - 1; i >= 0; i--)
            fresh.ShellServices!.OpenTab(snapshot[i].Kind, snapshot[i].PageParams);

        var active = snapshot.FirstOrDefault(s => s.IsActive);
        if (active.Kind is not null && fresh.ShellServices!.FindTab(active.Kind, active.PageParams) is { } activeTab)
            freshHost.SetActiveTab(activeTab);
    }

    /// <summary>Per-profile data folder: <c>%AppData%\…\Contexts\&lt;name&gt;</c> (folder name kept for compat).</summary>
    public static string ProfileDir(string name)
        => Path.Combine(ConfigManager.Instance.BaseDir, "Contexts", name);

    /// <summary>Persists the profile list (names/colours/icons only).</summary>
    public void SaveProfiles()
    {
        if (_savedConfig is null) return;
        _savedConfig.Contexts = [.. Profiles];
        ConfigManager.Instance.Save(_savedConfig, _savedConfig.ConfigName);
    }

    /// <summary>Persists one profile's AI ability config to its own folder.</summary>
    public void SaveProfileAiConfig(Profile profile)
        => ConfigManager.Instance.SaveTo(ProfileDir(profile.Name), profile.AiConfig, profile.AiConfig.ConfigName);

    /// <summary>Persists one profile's assistant persona (name + system prompt) to its own folder.</summary>
    public void SaveProfilePersona(Profile profile)
        => ConfigManager.Instance.SaveTo(ProfileDir(profile.Name), profile.Persona, profile.Persona.ConfigName);

    /// <summary>Persists one of a profile's per-workspace feature configs to its own folder.</summary>
    public void SaveProfileWorkspaceConfig(Profile profile, IFeatureConfig config)
        => ConfigManager.Instance.SaveTo(ProfileDir(profile.Name), config, config.ConfigName);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a fresh provider set for <paramref name="ws"/>'s current configs and assigned grid
    /// columns, swaps it in (acquire-before-release, so the pool warms newly-needed models and cools
    /// dropped ones), and re-registers the execution instances into the AIService keyed by column id.
    /// </summary>
    private static void AcquireAndRegister(Workspace ws)
    {
        var profile = ws.Profile;

        var fresh = ProviderManager.Instance.AcquireProviderSet(
            profile.ProviderConfigs, AssignedColumns(profile.AiConfig));
        var old   = ws.Providers;
        ws.Providers               = fresh;
        profile.AiConfig.Providers = fresh;
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

    private static void BootstrapServices(Workspace ws)
    {
        var profile = ws.Profile;

        var service = new AIService(ws, profile.ConversationsDir);
        service.LoadAbilityConfig(profile.AiConfig);
        ws.AiService = service;

        AcquireAndRegister(ws);

        ws.ShellServices ??= new ShellServices(ws, ProviderManager.Instance.ActivityManager);
    }
}
