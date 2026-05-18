using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Core.Services;

public class AIService : IAIService
{
    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Smile", "Nexaflow", "Conversations");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented              = true,
        PropertyNameCaseInsensitive = true
    };

    private List<ConversationRecord> _conversations = [];

    public ConversationRecord? ActiveConversation { get; }

    // ── Persistence ───────────────────────────────────────────────────────

    public async Task<IEnumerable<ConversationRecord>> LoadAllAsync()
    {
        var result = new List<ConversationRecord>();
        if (!Directory.Exists(BaseDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(BaseDir))
        {
            var file = Path.Combine(dir, "conversation.json");
            if (!File.Exists(file)) continue;
            try
            {
                await using var fs  = File.OpenRead(file);
                var rec = await JsonSerializer.DeserializeAsync<ConversationRecord>(fs, JsonOpts);
                if (rec is not null) result.Add(rec);
            }
            catch { /* skip corrupt files */ }
        }

        result.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        _conversations = result;
        return result;
    }

    public async Task SaveAsync(ConversationRecord activeConversation)
    {
        try
        {
            var dir  = Path.Combine(BaseDir, activeConversation.Id);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "conversation.json");
            await using var fs = File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, activeConversation, JsonOpts);
        }
        catch { /* never crash on persistence failures */ }
    }

    // ── AI routing ────────────────────────────────────────────────────────

    public async Task<IQueryHandler?> DisambiguateToolSelection(
        IPageViewModel? page, string input, IReadOnlyList<IQueryHandler> candidates)
    {
        var context  = page?.GetContext() ?? "No specific context.";
        var toolList = string.Join("\n", candidates.Select((h, i) => $"{i + 1}. {h.Description}"));

        var systemPrompt = "You are a routing assistant. Reply with only a single digit — the number of the best tool, or 0 if none apply.";
        var userPrompt   =
            $"Context: {context}\n" +
            $"User typed: \"{input}\"\n\n" +
            $"Tools:\n0. None of these apply\n{toolList}\n\n" +
            "Which tool number should handle this request?";

        var response = await LlmProviderRegistry.Basic.QueryAsync(systemPrompt, userPrompt);
        var raw      = response?.RawText?.Trim() ?? string.Empty;

        // Extract first digit from the reply
        var digit = raw.FirstOrDefault(char.IsDigit);
        if (digit == default) return null;

        var idx = digit - '0';
        return (idx >= 1 && idx <= candidates.Count) ? candidates[idx - 1] : null;
    }

    public async Task<AiResponse?> ContextChat(IPageViewModel? page, string input)
    {
        var context = page?.GetContext() ?? "No specific context.";
        var actions = page?.GetAvailableActions() ?? [];

        var actionsText = actions.Count > 0
            ? string.Join("\n", actions.Select(a =>
                $"- {a.Name}: {a.Description}" +
                (a.Parameters is { Count: > 0 }
                    ? " (params: " + string.Join(", ", a.Parameters.Select(p => $"{p.Key}: {p.Value}")) + ")"
                    : string.Empty)))
            : "No specific actions available.";

        var systemPrompt =
            $"You are an assistant embedded in a desktop application. " +
            $"The user is currently looking at: {context}.\n\n" +
            $"Available actions this page can perform:\n{actionsText}\n\n" +
            "When you respond, use exactly one of these formats:\n" +
            "1. To execute an action, reply ONLY with JSON: {\"action\":\"ActionName\",\"param\":\"value\",...}\n" +
            "2. To suggest text the user should confirm, start with: PREFILL: <suggested text>\n" +
            "3. For a conversational reply, respond normally.\n" +
            "Choose the format that best serves the user's intent.";

        var response = await LlmProviderRegistry.Conversation.QueryAsync(systemPrompt, input);
        var raw      = response?.RawText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw)) return null;

        // JSON action
        if (raw.StartsWith('{'))
        {
            try
            {
                var node = JsonNode.Parse(raw);
                var name = node?["action"]?.GetValue<string>();
                if (name is not null)
                {
                    var parameters = node!.AsObject()
                        .Where(kv => !string.Equals(kv.Key, "action", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value?.GetValue<string>() ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase);
                    return AiResponse.AsAction(new ActionDescriptor(name, string.Empty, parameters));
                }
            }
            catch { /* fall through to message */ }
        }

        // Prefill suggestion
        const string prefillPrefix = "PREFILL:";
        if (raw.StartsWith(prefillPrefix, StringComparison.OrdinalIgnoreCase))
            return AiResponse.AsPrefill(raw[prefillPrefix.Length..].TrimStart());

        // Conversational response
        return AiResponse.AsMessage(raw);
    }
}
