using Nexaflow.Core.Controls;
using Nexaflow.Providers.Common;
using System.Text.Json.Serialization;
using ProviderCustomControl = Nexaflow.Providers.Common.CustomControlAttribute;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.AI;

[ProviderCustomControl(typeof(AiAbilityGridControl))]
public sealed class AiConfig : IProviderConfig
{
    public string ConfigName   => "ai-abilities";
    public string FriendlyName => Str.Get("Shell.Config.Ai");

    /// <summary>
    /// The editing workspace's provider set — supplies the ability grid with the provider instances
    /// and configs to list/query. Runtime-only; assigned by <see cref="Services.WorkspaceManager"/>.
    /// </summary>
    [JsonIgnore]
    public ProviderSet? Providers { get; set; }

    /// <summary>Ordered list of provider/model columns the user has configured.</summary>
    public List<ProviderModelPair> Columns { get; set; } = [];

    /// <summary>
    /// Maps AiAbility.ToString() → the chain of columns that serve it. A missing or empty chain means no model.
    /// </summary>
    public Dictionary<string, AbilityChain> Assignments { get; set; } = [];

    /// <summary>Every column some ability's chain names — the ones a workspace needs providers for.</summary>
    public IReadOnlySet<string> AssignedColumnIds()
        => Assignments.Values.SelectMany(c => c.ColumnIds).ToHashSet();
}
