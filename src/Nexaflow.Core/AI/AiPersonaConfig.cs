using Nexaflow.Core.Controls;
using Nexaflow.Providers.Common;
using ProviderCustomControl = Nexaflow.Providers.Common.CustomControlAttribute;

namespace Nexaflow.Core.AI;

/// <summary>
/// Per-workspace persona settings for the conversational AI: the display name shown on
/// the response overlay and the system prompt prepended to every conversation. Stored
/// per workspace under <c>Contexts/&lt;name&gt;/ai-persona</c> (see <see cref="Models.Workspace"/>).
/// Edited from the "AI Customisation" section of the per-workspace Configure panel.
/// </summary>
[ProviderCustomControl(typeof(AiPersonaControl))]
[Nexaflow.Providers.Common.MandatorySetup]
public sealed class AiPersonaConfig : IProviderConfig
{
    public string ConfigName   => "ai-persona";
    public string FriendlyName => "AI Customisation";

    public string Name         { get; set; } = "Aria";
    public string SystemPrompt { get; set; } = string.Empty;
}
