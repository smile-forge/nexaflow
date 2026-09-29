using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.AIChat;

/// <summary>How long conversations are kept before the on-open purge removes them.</summary>
public enum ConversationRetention
{
    [ConfigDisplayName("AIChat.Config.AiChat.Retention.OneWeek")]   OneWeek,
    [ConfigDisplayName("AIChat.Config.AiChat.Retention.OneMonth")]  OneMonth,
    [ConfigDisplayName("AIChat.Config.AiChat.Retention.OneYear")]   OneYear,
    [ConfigDisplayName("AIChat.Config.AiChat.Retention.FiveYears")] FiveYears,
    [ConfigDisplayName("AIChat.Config.AiChat.Retention.Forever")]   Forever,
}

/// <summary>
/// Options for the AI Chat feature. <see cref="IsAnalysisEnabled"/> renders as a toggle
/// switch in the Options panel (the property grid maps bool properties to a ToggleSwitch);
/// <see cref="Retention"/> renders as a combo (enum properties become an EnumComboBox).
/// </summary>
public sealed class AiChatConfig : IFeatureConfig
{
    public string ConfigName   => "aichat";
    public string FriendlyName => Str.Get("AIChat.Config.AiChat");

    /// <summary>True when background conversation analysis should run.</summary>
    [ConfigDisplayName("AIChat.Config.AiChat.IsAnalysisEnabled")]
    public bool IsAnalysisEnabled { get; set; } = true;

    /// <summary>How long to keep conversations; older ones are purged when the AI Chat tab opens.</summary>
    [ConfigDisplayName("AIChat.Config.AiChat.Retention")]
    public ConversationRetention Retention { get; set; } = ConversationRetention.OneYear;

    /// <summary>The cutoff date for retention, or null when keeping forever.</summary>
    public DateTime? RetentionCutoff() => Retention switch
    {
        ConversationRetention.OneWeek   => DateTime.Now.AddDays(-7),
        ConversationRetention.OneMonth  => DateTime.Now.AddMonths(-1),
        ConversationRetention.OneYear   => DateTime.Now.AddYears(-1),
        ConversationRetention.FiveYears => DateTime.Now.AddYears(-5),
        _                               => null,
    };
}
