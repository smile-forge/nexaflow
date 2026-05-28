using Nexaflow.Core.Controls;
using Nexaflow.Providers.Common;
using ProviderCustomControl = Nexaflow.Providers.Common.CustomControlAttribute;

namespace Nexaflow.Core.AI;

/// <summary>
/// Global persona settings for the conversational AI: the display name shown on
/// the response overlay and the system prompt prepended to every conversation.
/// Edited from the "AI Customisation" tab of the Manage AI popup.
/// </summary>
[ProviderCustomControl(typeof(AiPersonaControl))]
public sealed class AiPersonaConfig : IProviderConfig
{
    public string ConfigName   => "ai-persona";
    public string FriendlyName => "AI Customisation";

    public string Name         { get; set; } = "Aria";
    public string SystemPrompt { get; set; } = string.Empty;
}
