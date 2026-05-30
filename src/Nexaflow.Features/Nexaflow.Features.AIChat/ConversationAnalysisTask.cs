using System.Text;
using System.Text.Json;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Background task that analyzes a conversation's transcript via the Analysis-ability model and
/// stores the resulting <see cref="ConversationAnalysis"/> as the conversation's "analysis" artifact.
/// Queued by <see cref="ViewModels.ConversationViewModel"/> when its tab is closed after content was added.
/// </summary>
public sealed class ConversationAnalysisTask : IBackgroundTask
{
    public const string ArtifactName = "analysis";

    private readonly IAIService _ai;
    private readonly string     _conversationId;
    private readonly string     _title;
    private readonly List<ConversationMessage> _messages;
    private readonly List<string> _attachments;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
    };

    public ConversationAnalysisTask(IAIService ai, ConversationRecord conversation)
    {
        _ai             = ai;
        _conversationId = conversation.Id;
        _title          = conversation.Title;
        _messages       = [.. conversation.Messages];
        _attachments    = [.. conversation.Attachments];
    }

    public string Description => $"Analyzing conversation: {_title}";

    public async Task RunAsync(CancellationToken ct)
    {
        if (_messages.Count == 0) return;

        var raw = await _ai.RunAnalysisAsync(BuildSystemPrompt(), BuildTranscript(), ct);
        if (string.IsNullOrWhiteSpace(raw)) return;

        var analysis = Parse(raw);
        if (analysis is null) return;

        analysis.AnalyzedMessageCount = _messages.Count;
        analysis.AnalyzedAt           = DateTime.Now;

        var json = JsonSerializer.Serialize(analysis, JsonOpts);
        await _ai.SaveConversationArtifactAsync(_conversationId, ArtifactName, json);
    }

    private static string BuildSystemPrompt() =>
        "You analyze a conversation between a user and an AI assistant. Respond with ONLY a JSON " +
        "object (no prose, no code fences) using exactly these keys:\n" +
        "  \"topic\": string — what the conversation was about,\n" +
        "  \"tone\": string — overall tone,\n" +
        "  \"toneEvolution\": string — how the tone changed over time,\n" +
        "  \"understandingOfUser\": string — what was learned about the user,\n" +
        "  \"understandingOfAgent\": string — what was learned about the assistant,\n" +
        "  \"keyDecisionPoints\": string[] — notable decisions reached (empty array if none),\n" +
        "  \"importantFacts\": string[] — important or unique facts discussed,\n" +
        "  \"valuableAttachments\": string[] — attachments that carried value,\n" +
        "  \"summary\": string — a concise 1-3 sentence summary of the whole analysis.";

    private string BuildTranscript()
    {
        var sb = new StringBuilder();
        sb.Append("Title: ").AppendLine(_title);
        if (_attachments.Count > 0)
        {
            sb.AppendLine("Attachments:");
            foreach (var a in _attachments) sb.Append("  - ").AppendLine(a);
        }
        sb.AppendLine("Transcript:");
        foreach (var m in _messages)
            sb.AppendLine((m.IsUser ? "User: " : "Assistant: ") + m.Text);
        return sb.ToString();
    }

    /// <summary>Parses the model's JSON reply, tolerating ```json fences and surrounding text.</summary>
    private static ConversationAnalysis? Parse(string raw)
    {
        try
        {
            var json = ExtractJson(raw);
            return json is null ? null : JsonSerializer.Deserialize<ConversationAnalysis>(json, JsonOpts);
        }
        catch { return null; }
    }

    private static string? ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end   = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }
}
