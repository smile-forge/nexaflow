namespace Nexaflow.Features.AIChat;

/// <summary>
/// Structured analysis of a conversation, produced in the background when a ConversationView tab
/// is closed (see <see cref="ConversationAnalysisTask"/>) and stored as the "analysis" artifact
/// beside the transcript. Shown in the AI Chat browser and used as resume context.
/// </summary>
public sealed class ConversationAnalysis
{
    /// <summary>What the conversation was about.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Overall tone of the conversation.</summary>
    public string Tone { get; set; } = string.Empty;

    /// <summary>How the tone evolved over the course of the conversation.</summary>
    public string ToneEvolution { get; set; } = string.Empty;

    /// <summary>What the conversation revealed about the user.</summary>
    public string UnderstandingOfUser { get; set; } = string.Empty;

    /// <summary>What the conversation revealed about the agent / assistant.</summary>
    public string UnderstandingOfAgent { get; set; } = string.Empty;

    /// <summary>Key decision points reached, if any.</summary>
    public List<string> KeyDecisionPoints { get; set; } = [];

    /// <summary>Important or unique facts discussed.</summary>
    public List<string> ImportantFacts { get; set; } = [];

    /// <summary>Attachments that carried value to the conversation.</summary>
    public List<string> ValuableAttachments { get; set; } = [];

    /// <summary>Concise summary of the analysis, shown as the headline in the browser.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>When the analysis was produced.</summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Number of transcript messages covered by this analysis. On resume, only messages beyond
    /// this index are replayed as history (the rest are summarized by the analysis).
    /// </summary>
    public int AnalyzedMessageCount { get; set; }
}
