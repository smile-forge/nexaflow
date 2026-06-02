using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.IO;

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

        // 3. Re-acquire providers (pool reuses unchanged ones, unloads now-unused). Acquire before
        //    release so providers whose config is unchanged never drop to zero refs.
        var fresh = ProviderManager.Instance.AcquireProviderSet(profile.ProviderConfigs);
        var old   = ws.Providers;
        ws.Providers           = fresh;
        profile.AiConfig.Providers = fresh;
        ProviderManager.Instance.ReleaseProviderSet(old);

        // 4. Rebuild the AIService for the (possibly new) profile.
        var service = new AIService(ws, profile.ConversationsDir);
        foreach (var (name, provider) in fresh.Providers)
            service.Register(name, provider);
        service.LoadAbilityConfig(profile.AiConfig);
        ws.AiService = service;

        // 5. Reopen the default tab so the windows aren't left empty.
        ws.ShellServices?.OpenTab("FileSystem", new() { ["mode"] = "thispc" });
    }

    /// <summary>
    /// Called by <see cref="ShellServices.UnregisterWindow"/> when a window closes. When the
    /// Workspace has no more windows, releases its providers, drops its feature cache, and removes it.
    /// </summary>
    public void NotifyWindowClosed(Workspace ws)
    {
        if (ws.ShellServices is { HasWindows: true }) return;

        FeatureManager.Instance.EvictWorkspace(ws);
        ProviderManager.Instance.ReleaseProviderSet(ws.Providers);
        ws.Providers = null;
        ws.AiService = null;
        _workspaces.Remove(ws);
    }

    /// <summary>
    /// Rebuilds <paramref name="ws"/>'s provider set from disk (reloading the profile's provider
    /// configs so newly discovered providers appear) and re-registers them into its AIService.
    /// Called when the AI options panel opens.
    /// </summary>
    public void RefreshProviders(Workspace ws)
    {
        ws.Profile.ReloadProviderConfigs();

        var fresh = ProviderManager.Instance.AcquireProviderSet(ws.Profile.ProviderConfigs);
        var old   = ws.Providers;
        ws.Providers               = fresh;
        ws.Profile.AiConfig.Providers = fresh;
        ProviderManager.Instance.ReleaseProviderSet(old);

        if (ws.AiService is { } svc)
            foreach (var (name, provider) in fresh.Providers)
                svc.Register(name, provider);
    }

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

        Profiles.Add(profile);
        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    /// <summary>
    /// Removes <paramref name="profile"/> from <see cref="Profiles"/>. At least one profile always
    /// remains. The caller (Options editor) prevents removing the active profile. Returns true if removed.
    /// </summary>
    public bool RemoveProfile(Profile profile)
    {
        if (Profiles.Count <= 1) return false;
        var removed = Profiles.Remove(profile);
        if (removed) ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>True if any live workspace is currently running <paramref name="profile"/>.</summary>
    public bool IsProfileInUse(Profile profile)
        => _workspaces.Any(w => ReferenceEquals(w.Profile, profile));

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

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void BootstrapServices(Workspace ws)
    {
        var profile = ws.Profile;

        ws.Providers               = ProviderManager.Instance.AcquireProviderSet(profile.ProviderConfigs);
        profile.AiConfig.Providers = ws.Providers;

        var service = new AIService(ws, profile.ConversationsDir);
        foreach (var (name, provider) in ws.Providers.Providers)
            service.Register(name, provider);
        service.LoadAbilityConfig(profile.AiConfig);
        ws.AiService = service;

        ws.ShellServices ??= new ShellServices(ws, ProviderManager.Instance.ActivityManager);
    }
}
