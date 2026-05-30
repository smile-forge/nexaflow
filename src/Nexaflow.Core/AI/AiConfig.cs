using Nexaflow.Core.Controls;
using Nexaflow.Providers.Common;
using System.Text.Json.Serialization;
using ProviderCustomControl = Nexaflow.Providers.Common.CustomControlAttribute;

namespace Nexaflow.Core.AI;

[ProviderCustomControl(typeof(AiAbilityGridControl))]
public sealed class AiConfig : IProviderConfig
{
    public string ConfigName   => "ai-abilities";
    public string FriendlyName => "AI";

    /// <summary>
    /// The owning context's provider set — supplies the ability grid with the provider instances
    /// and configs to list/query. Runtime-only; assigned by <see cref="Services.WorkContextManager"/>.
    /// </summary>
    [JsonIgnore]
    public ProviderSet? Providers { get; set; }

    /// <summary>Ordered list of provider/model columns the user has configured.</summary>
    public List<ProviderModelPair> Columns { get; set; } = [];

    /// <summary>
    /// Maps AiAbility.ToString() → ProviderModelPair.Id.
    /// Empty/missing value means no provider assigned (None).
    /// </summary>
    public Dictionary<string, string> Assignments { get; set; } = [];
}
