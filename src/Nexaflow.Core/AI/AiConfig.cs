using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.AI;

[CustomControl(typeof(AiAbilityGridControl))]
public sealed class AiConfig : IFeatureConfig
{
    public string ConfigName   => "ai-abilities";
    public string FriendlyName => "AI";

    /// <summary>Ordered list of provider/model columns the user has configured.</summary>
    public List<ProviderModelPair> Columns { get; set; } = [];

    /// <summary>
    /// Maps AiAbility.ToString() → ProviderModelPair.Id.
    /// Empty/missing value means no provider assigned (None).
    /// </summary>
    public Dictionary<string, string> Assignments { get; set; } = [];
}
