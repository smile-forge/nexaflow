using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Options for the AI Chat feature. <see cref="IsAnalysisEnabled"/> renders as a toggle
/// switch in the Options panel (the property grid maps bool properties to a ToggleSwitch).
/// </summary>
public sealed class AiChatConfig : IFeatureConfig
{
    public string ConfigName   => "aichat";
    public string FriendlyName => "AI Chat";

    /// <summary>True when background conversation analysis should run.</summary>
    [ConfigDisplayName("Automatic Conversation Analysis")]
    public bool IsAnalysisEnabled { get; set; } = true;
}
