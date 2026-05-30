using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Options for the AI Chat feature. The Options panel has no checkbox editor, so the
/// automatic-analysis toggle is a list-sourced "On"/"Off" string (see <see cref="ScratchpadConfig"/>
/// precedent), surfaced as <see cref="IsAnalysisEnabled"/>.
/// </summary>
public sealed class AiChatConfig : IFeatureConfig
{
    public string ConfigName   => "aichat";
    public string FriendlyName => "AI Chat";

    [ConfigDisplayName("Automatic Conversation Analysis")]
    [ListSource(typeof(AiChatConfig), nameof(GetToggleOptions))]
    public string AutomaticAnalysis { get; set; } = "On";

    public static IEnumerable<string> GetToggleOptions() => ["On", "Off"];

    /// <summary>True when background conversation analysis should run.</summary>
    public bool IsAnalysisEnabled =>
        AutomaticAnalysis.Equals("On", StringComparison.OrdinalIgnoreCase);
}
