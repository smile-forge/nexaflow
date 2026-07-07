using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

/// <summary>
/// A saved, shared workspace configuration — the thing listed in the context dropdown. Owns
/// identity (<see cref="Name"/>/<see cref="Color"/>/<see cref="Icon"/>) AND the shared per-profile
/// config: the AI ability grid (<see cref="AiConfig"/>), the ribbon layout
/// (<see cref="RibbonService"/>), the provider configs (API keys etc.), and the conversations
/// directory. One Profile can back many <see cref="Workspace"/> runtimes; its shared config is
/// loaded once and seen live by all of them.
/// </summary>
public sealed partial class Profile : ObservableObject
{
    // Observable so the options editor's hex box, colour preview and swatch picker update live when
    // any one of them changes the value. Serialised by name (Name/Color/Icon) exactly as before.
    [ObservableProperty] private string _name  = "Default";
    [ObservableProperty] private string _color = "#5B8CFF";
    [ObservableProperty] private string _icon  = "⬡";

    /// <summary>
    /// The tabs (grouped by pane) opened when a fresh window starts for this profile. Always configured:
    /// a new/never-configured profile is seeded with the default "This PC" file view, and legacy profiles
    /// (no value in workcontexts.json) fall back to that same seed on load. Set explicitly via the
    /// workspace's "Use Tabset as Default" action or edited in the Configure panel — an explicit empty
    /// list is honoured (start with no tabs). Serialized inline in the profile list; the setter re-seeds a
    /// stray null (e.g. a hand-edited config) back to the default so this is never unconfigured.
    /// </summary>
    private List<DefaultTabDescriptor> _defaultTabs = [DefaultTabDescriptor.ThisPc()];
    public List<DefaultTabDescriptor> DefaultTabs
    {
        get => _defaultTabs;
        set => _defaultTabs = value ?? [DefaultTabDescriptor.ThisPc()];
    }

    /// <summary>UI-only, transient: set by the Workspaces editor on each row copy to mark whether the
    /// workspace is currently live (its Delete button is hidden — a running workspace can't be removed).
    /// Never serialised; false on the saved profiles.</summary>
    [JsonIgnore] public bool IsInUse { get; set; }

    // ── Shared per-profile state (runtime; lazily loaded, never serialised inline) ──

    /// <summary>Ability grid (Columns + Assignments). Persisted to <c>Contexts/&lt;name&gt;/ai-abilities</c>.</summary>
    [JsonIgnore] public AiConfig AiConfig { get; } = new();

    /// <summary>Assistant persona (name + system prompt). Persisted to <c>Contexts/&lt;name&gt;/ai-persona</c>.</summary>
    [JsonIgnore] public AiPersonaConfig Persona { get; } = new();

    /// <summary>Stateless disk layer for this profile's ribbon layout.</summary>
    [JsonIgnore] public RibbonLayoutService? RibbonService { get; private set; }

    /// <summary>Provider configs (API keys etc.) — one instance per discovered config type.</summary>
    [JsonIgnore] public IReadOnlyList<IProviderConfig> ProviderConfigs { get; private set; } = [];

    /// <summary>
    /// Per-workspace feature configs (one instance per <see cref="WorkspaceScopedConfigAttribute"/> type)
    /// loaded from this profile's folder — mirrors <see cref="ProviderConfigs"/>. Materialized lazily so a
    /// feature whose only footprint is a scoped config isn't force-loaded at startup; this returns whatever
    /// has been materialized so far. Injected into features via <c>FeatureManager.TryResolveArgs</c>.
    /// </summary>
    [JsonIgnore] public IReadOnlyList<IFeatureConfig> WorkspaceConfigs
    { get { lock (_scopedLock) return _scoped.Values.ToList(); } }

    private readonly Dictionary<Type, IFeatureConfig> _scoped = new();
    private readonly object _scopedLock = new();

    /// <summary>The per-workspace feature config of the given concrete type, materializing (and loading from
    /// disk) on first request. Null when <paramref name="t"/> isn't a scoped config type. The owning
    /// assembly is already loaded by the time a feature ctor asks for its scoped config, so this never
    /// forces a deferred feature to load.</summary>
    public IFeatureConfig? FindWorkspaceConfig(Type t)
    {
        lock (_scopedLock)
        {
            if (_scoped.TryGetValue(t, out var existing)) return existing;
            if (!FeatureManager.Instance.IsWorkspaceScopedConfig(t)) return null;
            try
            {
                var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
                ConfigManager.Instance.LoadFrom(Dir, cfg, cfg.ConfigName);
                _scoped[t] = cfg;
                return cfg;
            }
            catch { return null; }
        }
    }

    /// <summary>Materializes scoped configs for already-loaded feature assemblies (no new loads). Called
    /// before a feature's merged config view is built, so a loaded feature's scoped config is included.</summary>
    internal void MaterializeLoadedScopedConfigs()
    {
        foreach (var t in FeatureManager.Instance.LoadedWorkspaceScopedConfigTypes)
            FindWorkspaceConfig(t);
    }

    /// <summary>Materializes every scoped config, loading the owning assemblies. Used by the Configure /
    /// Manage-AI panels and on reload, which need the full set.</summary>
    internal void MaterializeAllScopedConfigs()
    {
        foreach (var t in FeatureManager.Instance.WorkspaceScopedConfigTypes)
            FindWorkspaceConfig(t);
    }

    [JsonIgnore] public string Dir => WorkspaceManager.ProfileDir(Name);
    [JsonIgnore] public string ConversationsDir => Path.Combine(Dir, "Conversations");

    // ── Live shared-ribbon sync ──
    /// <summary>
    /// Raised after any <see cref="Workspace"/>'s ribbon is persisted, so every other window/Workspace
    /// bound to this profile reloads its ribbon items live. See RibbonViewModel.
    /// </summary>
    public event EventHandler? RibbonChanged;
    public void RaiseRibbonChanged() => RibbonChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Idempotently loads this profile's shared services from its folder. Called the first time a
    /// Workspace is created for the profile (and on switch). Safe to call repeatedly.
    /// </summary>
    internal void EnsureSharedServicesLoaded()
    {
        if (RibbonService is not null) return;
        ConfigManager.Instance.LoadFrom(Dir, AiConfig, AiConfig.ConfigName);
        ConfigManager.Instance.LoadFrom(Dir, Persona, Persona.ConfigName);
        RibbonService    = new RibbonLayoutService(Dir);
        ProviderConfigs  = ProviderManager.Instance.LoadProviderConfigs(Dir);
        // Scoped feature configs are materialized lazily (see FindWorkspaceConfig) so a feature whose only
        // footprint here is a scoped config isn't force-loaded at startup.
    }

    /// <summary>
    /// Re-reads the provider configs from disk (picks up provider assemblies discovered after the
    /// first load, e.g. when the Manage-AI panel runs <see cref="ProviderManager.DiscoverAll"/>).
    /// </summary>
    internal void ReloadProviderConfigs()
        => ProviderConfigs = ProviderManager.Instance.LoadProviderConfigs(Dir);

    /// <summary>Re-reads the per-workspace feature configs from this profile's folder.</summary>
    internal void ReloadWorkspaceConfigs()
    {
        lock (_scopedLock) _scoped.Clear();
        MaterializeAllScopedConfigs();
    }

    /// <summary>
    /// Re-reads ALL shared config (ability grid, persona, provider + per-workspace feature configs)
    /// from disk into the live instances. Called when the Configure panel closes after edits were
    /// applied to disk, so a subsequent <see cref="WorkspaceManager.RestartWorkspace"/> rebuilds the
    /// workspace from the saved values (the panel deliberately leaves the live instances untouched
    /// while editing). The runtime-only <see cref="AiConfig.Providers"/> is preserved (JsonIgnore).
    /// </summary>
    internal void ReloadSharedConfigs()
    {
        ConfigManager.Instance.LoadFrom(Dir, AiConfig, AiConfig.ConfigName);
        ConfigManager.Instance.LoadFrom(Dir, Persona, Persona.ConfigName);
        ProviderConfigs = ProviderManager.Instance.LoadProviderConfigs(Dir);
        lock (_scopedLock) _scoped.Clear();
        MaterializeAllScopedConfigs();
    }
}
