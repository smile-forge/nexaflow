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

    private readonly OllamaConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;
    private readonly string                      _model;

    /// <summary>Context window (tokens) for the bound model, fetched on warm-up via /api/show.</summary>
    private int? _contextLength;

    public OllamaLlmProvider(IBackgroundActivityManager activityManager, OllamaConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
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

        return SendAsync(msgList, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(List<Message> messages, CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Ollama ({_model})…");
        try
        {
            var client  = new OllamaApiClient(new Uri(_config.Url));
            var request = new ChatRequest
            {
                Model     = _model,
                Messages  = messages,
                KeepAlive = _config.KeepAliveValue,
                // Thinking mode == --think; we never append Message.Thinking below, so it's --hidethinking too.
                Think     = _config.ThinkingMode ? true : null
            };

            var sb = new StringBuilder();
            await foreach (var chunk in client.ChatAsync(request, ct))
                sb.Append(chunk?.Message?.Content ?? "");

            activity.Complete();
            var text = sb.ToString();
            return string.IsNullOrEmpty(text) ? null : new LlmResponse(text);
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"Ollama request failed: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = new OllamaApiClient(new Uri(_config.Url));
            var models  = await client.ListLocalModelsAsync(ct);
            return models?.Select(m => m.Name ?? "")
                          .Where(n => !string.IsNullOrEmpty(n))
                          .ToList()
                   ?? [];
        }
        catch
        {
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
            var client  = new OllamaApiClient(new Uri(_config.Url));
            var request = new GenerateRequest
            {
                Model     = _model,
                Prompt    = string.Empty,
                KeepAlive = _config.KeepAliveValue
            };
            await foreach (var _ in client.GenerateAsync(request, ct)) { /* drain to completion */ }
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
            var client  = new OllamaApiClient(new Uri(_config.Url));
            var request = new GenerateRequest
            {
                Model     = _model,
                Prompt    = string.Empty,
                KeepAlive = "0"
            };
            await foreach (var _ in client.GenerateAsync(request, ct)) { /* drain to completion */ }
        }
        catch { /* best-effort */ }
    }

    /// <summary>Queries /api/show for the bound model's context length (0 on failure).</summary>
    private async Task<int> FetchContextLengthAsync(CancellationToken ct)
    {
        try
        {
            var client = new OllamaApiClient(new Uri(_config.Url));
            var show   = await client.ShowModelAsync(new ShowModelRequest { Model = _model }, ct);
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
