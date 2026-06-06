using System.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>How long conversations are kept before the on-open purge removes them.</summary>
public enum ConversationRetention
{
    [Description("1 Week")]   OneWeek,
    [Description("1 Month")]  OneMonth,
    [Description("1 Year")]   OneYear,
    [Description("5 Years")]  FiveYears,
    [Description("Forever")]  Forever,
}

/// <summary>
/// Options for the AI Chat feature. <see cref="IsAnalysisEnabled"/> renders as a toggle
/// switch in the Options panel (the property grid maps bool properties to a ToggleSwitch);
/// <see cref="Retention"/> renders as a combo (enum properties become an EnumComboBox).
/// </summary>
public sealed class AiChatConfig : IFeatureConfig
{
    public string ConfigName   => "aichat";
    public string FriendlyName => "AI Chat";

    /// <summary>True when background conversation analysis should run.</summary>
    [ConfigDisplayName("Automatic Conversation Analysis")]
    public bool IsAnalysisEnabled { get; set; } = true;

    /// <summary>How long to keep conversations; older ones are purged when the AI Chat tab opens.</summary>
    [ConfigDisplayName("Keep Conversations For")]
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
