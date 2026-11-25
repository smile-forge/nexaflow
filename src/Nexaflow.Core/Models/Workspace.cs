using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

/// <summary>
/// A saved, shared workspace configuration — the thing listed in the context dropdown. Owns
/// identity (<see cref="Name"/>/<see cref="Color"/>/<see cref="Icon"/>) AND the shared per-workspace
/// config: the AI ability grid (<see cref="AiConfig"/>), the ribbon layout
/// (<see cref="RibbonService"/>), the provider configs (API keys etc.), and the conversations
/// directory. One Workspace can back many <see cref="WorkspaceRuntime"/> runtimes; its shared config is
/// loaded once and seen live by all of them.
/// </summary>
public sealed partial class Workspace : ObservableObject
{
    // Observable so the options editor's hex box, colour preview and swatch picker update live when
    // any one of them changes the value. Serialised by name (Name/Color/Icon) exactly as before.
    [ObservableProperty] private string _name  = "Default";
    [ObservableProperty] private string _color = "#5B8CFF";
    [ObservableProperty] private string _icon  = "⬡";

    /// <summary>
    /// The tabs (grouped by pane) opened when a fresh window starts for this workspace. Always configured:
    /// a new/never-configured workspace is seeded with the default "This PC" file view, and legacy workspaces
    /// (no value in workcontexts.json) fall back to that same seed on load. Set explicitly via the
    /// workspace's "Use Tabset as Default" action or edited in the Configure panel — an explicit empty
    /// list is honoured (start with no tabs). Serialized inline in the workspace list; the setter re-seeds a
    /// stray null (e.g. a hand-edited config) back to the default so this is never unconfigured.
    /// </summary>
    private List<DefaultTabDescriptor> _defaultTabs = [DefaultTabDescriptor.ThisPc()];
    public List<DefaultTabDescriptor> DefaultTabs
    {
        get => _defaultTabs;
        set => _defaultTabs = value ?? [DefaultTabDescriptor.ThisPc()];
    }

    /// <summary>
    /// The tabs (grouped by pane) that were open when this workspace's last window closed, recorded only
    /// when they differed from <see cref="DefaultTabs"/>. On the workspace's next open a toast offers to
    /// restore them in one click; null means the last session matched the default (nothing to restore).
    /// Serialized inline so the offer survives an app restart.
    /// </summary>
    public List<DefaultTabDescriptor>? LastSessionTabs { get; set; }

    /// <summary>
    /// True when this workspace never offers to restore its last session: no toast on open, and the
    /// tabs a closing window leaves behind are not recorded at all (so turning it on also drops any
    /// <see cref="LastSessionTabs"/> already held). Set on the Configure panel's "Default tabs" page —
    /// a workspace meant to start from a fixed tabset every time. Serialized inline in the workspace list.
    /// </summary>
    public bool SuppressSessionRestore { get; set; }

    /// <summary>
    /// Where this workspace's last window was meant to be when it closed — its monitor, normal size and position,
    /// and whether it was maximised — so the next window opens there. It is the placement the user chose, not one
    /// Windows forced while a monitor was asleep or missing (<see cref="Services.WindowPlacementTracker"/>). Kept
    /// whatever <see cref="SuppressSessionRestore"/> says: reopening where it was is not an offer. Serialized inline
    /// in the workspace list.
    /// </summary>
    public WindowPlacement? LastWindowPlacement { get; set; }

    /// <summary>UI-only, transient: set by the Workspaces editor on each row copy to mark whether the
    /// workspace is currently live (its Delete button is hidden — a running workspace can't be removed).
    /// Never serialised; false on the saved workspaces.</summary>
    [JsonIgnore] public bool IsInUse { get; set; }

    // ── Shared per-workspace state (runtime; lazily loaded, never serialised inline) ──

    /// <summary>Ability grid (Columns + Assignments). Persisted to <c>Contexts/&lt;name&gt;/ai-abilities</c>.</summary>
    [JsonIgnore] public AiConfig AiConfig { get; } = new();

    /// <summary>Assistant persona (name + system prompt). Persisted to <c>Contexts/&lt;name&gt;/ai-persona</c>.</summary>
    [JsonIgnore] public AiPersonaConfig Persona { get; } = new();

    /// <summary>Stateless disk layer for this workspace's ribbon layout.</summary>
    [JsonIgnore] public RibbonLayoutService? RibbonService { get; private set; }

    /// <summary>Provider configs (API keys etc.) — one instance per discovered config type.</summary>
    [JsonIgnore] public IReadOnlyList<IProviderConfig> ProviderConfigs { get; private set; } = [];

    /// <summary>The connections this workspace keeps, their secrets in Credential Manager; null until its shared services
    /// are loaded.</summary>
    [JsonIgnore] public WorkspaceConnections? Connections { get; private set; }

    /// <summary>
    /// Per-workspace feature configs (one instance per <see cref="WorkspaceScopedConfigAttribute"/> type)
    /// loaded from this workspace's folder — mirrors <see cref="ProviderConfigs"/>. Materialized lazily so a
    /// feature whose only footprint is a scoped config isn't force-loaded at startup; this returns whatever
    /// has been materialized so far. Injected into features via <c>FeatureManager.TryResolveArgs</c>.
    /// </summary>
    [JsonIgnore] public IReadOnlyList<IFeatureConfig> WorkspaceConfigs
    { get { lock (_scopedLock) return _scoped.Values.ToList(); } }

    private readonly Dictionary<Type, IFeatureConfig> _scoped = new();
    private readonly object _scopedLock = new();

    /// <summary>The configuration this workspace is listed in (<see cref="ListIn"/>); null while it is in no saved list.</summary>
    private ConfigManager? _configuration;

    /// <summary>
    /// Lists this workspace in <paramref name="configuration"/>: its folders sit under that configuration's, and its own settings —
    /// the ability grid, the persona, its provider and per-workspace feature configs, its connections — are read and written
    /// through it. The saved list does this for every workspace it holds (<see cref="WorkspaceManager"/>), and every workspace the
    /// session runs is in it.
    /// </summary>
    internal void ListIn(ConfigManager configuration) => _configuration = configuration;

    /// <summary>The configuration this workspace is listed in. A workspace in no saved list has no folder, so nothing of its own
    /// can be read or written.</summary>
    private ConfigManager Configuration => _configuration
        ?? throw new InvalidOperationException($"The workspace '{Name}' is in no saved list, so it has no folder.");

    /// <summary>The per-workspace feature config of the given concrete type, materializing (and loading from
    /// disk) on first request. Null when <paramref name="t"/> isn't a scoped config type, as <paramref name="features"/>
    /// knows them. The owning assembly is already loaded by the time a feature ctor asks for its scoped config, so this
    /// never forces a deferred feature to load.</summary>
    public IFeatureConfig? FindWorkspaceConfig(Type t, FeatureConfigs configs)
    {
        lock (_scopedLock)
        {
            if (_scoped.TryGetValue(t, out var existing)) return existing;
            if (!configs.IsWorkspaceScoped(t)) return null;
            try
            {
                var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
                Configuration.LoadFrom(Dir, cfg, cfg.ConfigName);
                _scoped[t] = cfg;
                return cfg;
            }
            catch { return null; }
        }
    }

    /// <summary>Materializes every scoped config <paramref name="features"/> knows, loading the owning assemblies. Used by the
    /// Configure / Manage-AI panels and on reload, which need the full set.</summary>
    internal void MaterializeAllScopedConfigs(FeatureConfigs configs)
    {
        foreach (var t in configs.WorkspaceScopedTypes)
            FindWorkspaceConfig(t, configs);
    }

    [JsonIgnore] public string Dir => WorkspaceManager.WorkspaceDir(Configuration, Name);
    [JsonIgnore] public string ConversationsDir => Path.Combine(Dir, "Conversations");

    /// <summary>What the workspace keeps on this machine alone (<see cref="WorkspaceManager.WorkspaceLocalDir"/>).</summary>
    [JsonIgnore] public string LocalDir => WorkspaceManager.WorkspaceLocalDir(Configuration, Name);

    /// <summary>What whole-document work in this workspace has worked out and keeps — gists, summaries, translations.</summary>
    [JsonIgnore] public string DocumentNotesDir => Path.Combine(LocalDir, "DocumentNotes");

    // ── Live shared-ribbon sync ──
    /// <summary>
    /// Raised after any <see cref="WorkspaceRuntime"/>'s ribbon is persisted, so every other window/Workspace
    /// bound to this workspace reloads its ribbon items live. See RibbonViewModel.
    /// </summary>
    public event EventHandler? RibbonChanged;
    public void RaiseRibbonChanged() => RibbonChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Idempotently loads this workspace's shared services from its folder, its provider configs those
    /// <paramref name="providers"/> has loaded. Called the first time a Workspace is created for the workspace
    /// (and on switch). Safe to call repeatedly.
    /// </summary>
    internal void EnsureSharedServicesLoaded(ProviderManager providers)
    {
        if (RibbonService is not null) return;
        Configuration.LoadFrom(Dir, AiConfig, AiConfig.ConfigName);
        if (AiConfig.BringUpToDate())
            SaveAiConfig();
        Configuration.LoadFrom(Dir, Persona, Persona.ConfigName);
        RibbonService    = new RibbonLayoutService(Dir);
        ProviderConfigs  = providers.LoadProviderConfigs(Dir);
        Connections      = new WorkspaceConnections(Dir, Configuration, CredentialManagerStore.Default);
        // Scoped feature configs are materialized lazily (see FindWorkspaceConfig) so a feature whose only
        // footprint here is a scoped config isn't force-loaded at startup.
    }

    /// <summary>Persists the ability grid (<see cref="AiConfig"/>) to this workspace's folder.</summary>
    internal void SaveAiConfig() => Configuration.SaveTo(Dir, AiConfig, AiConfig.ConfigName);

    /// <summary>
    /// Re-reads the provider configs from disk through <paramref name="providers"/> (picks up provider assemblies
    /// discovered after the first load, e.g. when the Manage-AI panel runs <see cref="ProviderManager.DiscoverAll"/>).
    /// </summary>
    internal void ReloadProviderConfigs(ProviderManager providers)
        => ProviderConfigs = providers.LoadProviderConfigs(Dir);

    /// <summary>
    /// Re-reads ALL shared config (ability grid, persona, provider + per-workspace feature configs)
    /// from disk into the live instances — the provider configs through <paramref name="providers"/>, the scoped ones those
    /// <paramref name="features"/> knows. Called when the Configure panel closes after edits were
    /// applied to disk, so a subsequent <see cref="WorkspaceRuntimes.Restart"/> rebuilds the
    /// workspace from the saved values (the panel deliberately leaves the live instances untouched
    /// while editing).
    /// </summary>
    internal void ReloadSharedConfigs(FeatureConfigs configs, ProviderManager providers)
    {
        Configuration.LoadFrom(Dir, AiConfig, AiConfig.ConfigName);
        Configuration.LoadFrom(Dir, Persona, Persona.ConfigName);
        ProviderConfigs = providers.LoadProviderConfigs(Dir);
        lock (_scopedLock) _scoped.Clear();
        MaterializeAllScopedConfigs(configs);
    }
}
