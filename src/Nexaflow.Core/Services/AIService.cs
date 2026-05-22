using Nexaflow.Core.AI;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Core.Services;

public sealed class AIService : IAIService
{
    // ── Provider registry ─────────────────────────────────────────────────

    private readonly Dictionary<string, ILlmProvider> _providers
        = new(StringComparer.OrdinalIgnoreCase);

    private AiConfig? _abilityConfig;

    /// <summary>Registers a named provider. Called by WorkContextManager during startup.</summary>
    public void Register(string name, ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[name] = provider;
    }

    /// <summary>All registered providers, keyed by name.</summary>
    public IReadOnlyDictionary<string, ILlmProvider> AllProviders => _providers;

    /// <summary>Loads the ability-to-provider mapping. Called once per context when providers are ready.</summary>
    public void LoadAbilityConfig(AiConfig config) => _abilityConfig = config;

    /// <summary>
    /// Returns the provider and model assigned to <paramref name="ability"/>, or null if none
    /// is configured or the configured provider is not registered.
    /// </summary>
    private (ILlmProvider Provider, string Model)? GetProvider(AiAbility ability)
    {
        if (_abilityConfig is null) return null;

        var key = ability.ToString();
        if (!_abilityConfig.Assignments.TryGetValue(key, out var columnId)
            || string.IsNullOrEmpty(columnId))
            return null;

        var pair = _abilityConfig.Columns.FirstOrDefault(c => c.Id == columnId);
        if (pair is null) return null;

        return _providers.TryGetValue(pair.ProviderName, out var p)
            ? (p, pair.Model)
            : null;
    }

    // ── Persistence ───────────────────────────────────────────────────────

    private readonly string _baseDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true
    };

    private List<ConversationRecord> _conversations = [];

    public ConversationRecord? ActiveConversation { get; }

    /// <param name="contextName">Used to namespace conversation storage per WorkContext.</param>
    public AIService(string contextName = "default")
    {
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smile", "Nexaflow", "Conversations", contextName);
    }

    public async Task<IEnumerable<ConversationRecord>> LoadAllAsync()
    {
        var result = new List<ConversationRecord>();
        if (!Directory.Exists(_baseDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(_baseDir))
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
            var dir  = Path.Combine(_baseDir, activeConversation.Id);
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
        var resolved = GetProvider(AiAbility.Disambiguation);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

        var context  = page?.GetContext() ?? "No specific context.";
        var toolList = string.Join("\n", candidates.Select((h, i) => $"{i + 1}. {h.Description}"));

        var systemPrompt = "You are a routing assistant. Reply with only a single digit — the number of the best tool, or 0 if none apply.";
        var userPrompt   =
            $"Context: {context}\n" +
            $"User typed: \"{input}\"\n\n" +
            $"Tools:\n0. None of these apply\n{toolList}\n\n" +
            "Which tool number should handle this request?";

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)],
            model);
        var raw      = response?.RawText?.Trim() ?? string.Empty;

        var digit = raw.FirstOrDefault(char.IsDigit);
        if (digit == default) return null;

        var idx = digit - '0';
        return (idx >= 1 && idx <= candidates.Count) ? candidates[idx - 1] : null;
    }

    public async Task<AiResponse?> ContextChat(IPageViewModel? page, string input)
    {
        var resolved = GetProvider(AiAbility.Conversation);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

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

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, input)],
            model);
        var raw      = response?.RawText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw)) return null;

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

        const string prefillPrefix = "PREFILL:";
        if (raw.StartsWith(prefillPrefix, StringComparison.OrdinalIgnoreCase))
            return AiResponse.AsPrefill(raw[prefillPrefix.Length..].TrimStart());

        return AiResponse.AsMessage(raw);
    }
}
