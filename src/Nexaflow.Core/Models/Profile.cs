using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
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

    // ── Shared per-profile state (runtime; lazily loaded, never serialised inline) ──

    /// <summary>Ability grid (Columns + Assignments). Persisted to <c>Contexts/&lt;name&gt;/ai-abilities</c>.</summary>
    [JsonIgnore] public AiConfig AiConfig { get; } = new();

    /// <summary>Assistant persona (name + system prompt). Persisted to <c>Contexts/&lt;name&gt;/ai-persona</c>.</summary>
    [JsonIgnore] public AiPersonaConfig Persona { get; } = new();

    /// <summary>Stateless disk layer for this profile's ribbon layout.</summary>
    [JsonIgnore] public RibbonLayoutService? RibbonService { get; private set; }

    /// <summary>Provider configs (API keys etc.) — one instance per discovered config type.</summary>
    [JsonIgnore] public IReadOnlyList<IProviderConfig> ProviderConfigs { get; private set; } = [];

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
        RibbonService   = new RibbonLayoutService(Dir);
        ProviderConfigs = ProviderManager.Instance.LoadProviderConfigs(Dir);
    }

    /// <summary>
    /// Re-reads the provider configs from disk (picks up provider assemblies discovered after the
    /// first load, e.g. when the Manage-AI panel runs <see cref="ProviderManager.DiscoverAll"/>).
    /// </summary>
    internal void ReloadProviderConfigs()
        => ProviderConfigs = ProviderManager.Instance.LoadProviderConfigs(Dir);
}
