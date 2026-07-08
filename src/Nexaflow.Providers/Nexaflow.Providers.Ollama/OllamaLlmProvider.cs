using Nexaflow.Providers.Common;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.Text;
using System.Text.Json;
using ModelInfo = Nexaflow.Providers.Common.ModelInfo;

namespace Nexaflow.Providers.Ollama;

public sealed class OllamaLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Ollama";
    public string Name => ProviderName;

    /// <summary>Vision is model-bound (llava/moondream/…-vision variants accept images).</summary>
    public bool SupportsImages => ModelSupportsVision(_model);

    private readonly OllamaConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;
    private readonly string                      _model;
    private OllamaApiClient?                     _client;

    /// <summary>Context window (tokens) for the bound model, fetched on warm-up via /api/show.</summary>
    private int? _contextLength;

    public string? LastModelListError { get; private set; }

    public OllamaLlmProvider(IBackgroundActivityManager activityManager, OllamaConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // The pooled instance is long-lived with a fixed config payload — build the SDK client once.
    private OllamaApiClient Client => _client ??= new OllamaApiClient(new Uri(_config.Url));

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
    {
        var msgList = BuildMessages(messages);
        var request = new ChatRequest
        {
            Model     = _model,
            Messages  = msgList,
            KeepAlive = _config.KeepAliveValue,
            // Thinking mode == --think; we never append Message.Thinking, so it's --hidethinking too.
            Think     = _config.ThinkingMode ? true : null
        };

        return LlmStreamRunner.RunAsync(_activityManager, $"Ollama ({_model})…", ProviderName,
            ct => Deltas(request, ct), ct);
    }

    private async IAsyncEnumerable<string?> Deltas(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in Client.ChatAsync(request, ct))
            yield return chunk?.Message?.Content;
    }

    /// <summary>Maps the neutral message list to Ollama chat messages (system inline; attachments
    /// listed as paths — image bytes go via vision-capable models only when the host allows).
    /// Internal + static so the mapping is unit-testable.</summary>
    internal static List<Message> BuildMessages(IReadOnlyList<LlmMessage> messages)
    {
        // Ollama's API accepts system messages inline in the messages array
        var msgList = new List<Message>();
        foreach (var msg in messages)
        {
            ChatRole role = msg.Role switch
            {
                LlmRole.System    => ChatRole.System,
                LlmRole.Assistant => ChatRole.Assistant,
                _                 => ChatRole.User
            };
            var content = msg.Role == LlmRole.User ? BuildUserContent(msg.Text, msg.Attachments) : msg.Text;

            msgList.Add(new Message { Role = role, Content = content });
        }
        return msgList;
    }

    /// <summary>Name heuristic for locally-hosted vision models.</summary>
    internal static bool ModelSupportsVision(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        string[] vision = ["llava", "bakllava", "moondream", "minicpm-v", "vision", "qwen2-vl", "qwen2.5vl"];
        foreach (var v in vision)
            if (model.Contains(v, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var models = await Client.ListLocalModelsAsync(ct);
            LastModelListError = null;
            return models?.Select(m => m.Name ?? "")
                          .Where(n => !string.IsNullOrEmpty(n))
                          .ToList()
                   ?? [];
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    public async Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return null;
        var len = _contextLength ??= await FetchContextLengthAsync(ct);
        return len > 0 ? new ModelInfo(len, _model) : null;
    }

    /// <summary>
    /// Warm-up: cache the bound model's context window (via /api/show) and trigger Ollama to load it
    /// into memory by issuing an empty generate with the configured keep_alive. Best-effort; never throws.
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return;

        _contextLength = await FetchContextLengthAsync(ct);

        try
        {
            var request = new GenerateRequest
            {
                Model     = _model,
                Prompt    = string.Empty,
                KeepAlive = _config.KeepAliveValue
            };
            await foreach (var _ in Client.GenerateAsync(request, ct)) { /* drain to completion */ }
        }
        catch { /* Ollama not running / model unavailable — best-effort */ }
    }

    /// <summary>
    /// Cool-down: ask Ollama to unload the bound model immediately (empty generate with keep_alive=0).
    /// Best-effort; never throws.
    /// </summary>
    public async Task CooldownAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return;

        try
        {
            var request = new GenerateRequest
            {
                Model     = _model,
                Prompt    = string.Empty,
                KeepAlive = "0"
            };
            await foreach (var _ in Client.GenerateAsync(request, ct)) { /* drain to completion */ }
        }
        catch { /* best-effort */ }
    }

    /// <summary>Queries /api/show for the bound model's context length (0 on failure).</summary>
    private async Task<int> FetchContextLengthAsync(CancellationToken ct)
    {
        try
        {
            var show   = await Client.ShowModelAsync(new ShowModelRequest { Model = _model }, ct);
            return ExtractContextLength(show);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Pulls the context length out of the model_info block — the architecture-specific
    /// <c>{arch}.context_length</c> key (e.g. <c>llama.context_length</c>).
    /// </summary>
    private static int ExtractContextLength(ShowModelResponse? show)
    {
        var info = show?.Info?.ExtraInfo;
        if (info is null) return 0;

        foreach (var (key, value) in info)
            if (key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase)
                && TryToInt(value, out var n))
                return n;

        return 0;
    }

    private static bool TryToInt(object? value, out int result)
    {
        result = 0;
        switch (value)
        {
            case null:                                                  return false;
            case int i:                              result = i;        return true;
            case long l:                             result = (int)l;   return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jl):
                                                     result = (int)jl;  return true;
            default:                                 return int.TryParse(value.ToString(), out result);
        }
    }

    // Ollama is text-only: every attachment (image or not) is listed by path.
    private static string BuildUserContent(string prompt, IReadOnlyList<LlmAttachment>? attachments)
        => PromptComposer.AppendFileList(prompt, attachments ?? []);
}
