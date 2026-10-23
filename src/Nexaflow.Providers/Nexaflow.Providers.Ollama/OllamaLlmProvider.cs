using Nexaflow.Providers.Common;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.Text;
using System.Text.Json;
using ModelInfo = Nexaflow.Providers.Common.ModelInfo;
using System.Text.Json.Nodes;

namespace Nexaflow.Providers.Ollama;

public sealed class OllamaLlmProvider : ILlmProvider, ITracksModelUse
{
    public const  string ProviderName = "Ollama";
    public string Name => ProviderName;

    /// <summary>Vision is model-bound (llava/moondream/…-vision variants accept images).</summary>
    public bool SupportsImages => ModelSupportsVision(_model);

    /// <summary>On this machine when the Ollama server is; a server elsewhere takes requests off it.</summary>
    public DataResidency Residency => EndpointResidency.Of(_config.Url, DataResidency.StaysOnMachine);

    private readonly OllamaConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;
    private readonly string                      _model;
    private OllamaApiClient?                     _client;

    /// <summary>Context window (tokens) for the bound model, fetched on warm-up via /api/show.</summary>
    private int? _contextLength;

    /// <summary>Whether the bound model calls tools, once its details are read.</summary>
    private bool? _toolCalls;

    /// <summary>The read of the bound model's details, begun when the instance is made.</summary>
    private readonly Task _details = Task.CompletedTask;

    public string? LastModelListError { get; private set; }

    public OllamaLlmProvider(IBackgroundActivityManager activityManager, OllamaConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
        // The model's details — its context, whether it calls tools — are read now, off the caller's thread; loading the
        // model itself waits for its first request, or for the app to load it ahead.
        if (!string.IsNullOrWhiteSpace(_model)) _details = Task.Run(() => ReadModelAsync(CancellationToken.None));
    }

    // The pooled instance is long-lived with a fixed config payload — build the SDK client once.
    private OllamaApiClient Client => _client ??= new OllamaApiClient(new Uri(_config.Url));

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
        => Stream(messages, [], ct);

    /// <summary>Whether the bound model's template calls tools — Ollama names it among the model's capabilities, read with
    /// its details when the instance is made. Until they are read, its tools are described in the conversation instead.</summary>
    public bool SupportsToolCalls => _toolCalls == true;

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool>    tools,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
        => Stream(messages, tools, ct);

    private IAsyncEnumerable<LlmStreamEvent> Stream(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Model     = _model,
            Messages  = BuildMessages(messages),
            KeepAlive = _config.KeepAliveValue,
            // Thinking mode == --think; we never yield Message.Thinking, so it's --hidethinking too.
            Think     = _config.ThinkingMode ? true : null,
            Tools     = tools.Count > 0 ? [.. tools.Select(Declare)] : null,
        };

        return ModelUse.During(LlmStreamRunner.RunAsync(_activityManager, $"Ollama ({_model})…", ProviderName,
            ct => Events(Client.ChatAsync(request, ct)), ct), ct);
    }

    /// <summary>A tool as Ollama declares it: a function, its parameters' JSON Schema as given.</summary>
    internal static JsonObject Declare(LlmTool tool) => new()
    {
        ["type"]     = "function",
        ["function"] = new JsonObject
        {
            ["name"]        = tool.Name,
            ["description"] = tool.Description,
            ["parameters"]  = tool.Parameters.DeepClone(),
        },
    };

    /// <summary>The reply as it streams: its text, and each call whole in the chunk that carries it. A call Ollama gives no
    /// id is numbered.</summary>
    internal static async IAsyncEnumerable<LlmStreamEvent> Events(IAsyncEnumerable<ChatResponseStream?> chunks)
    {
        int made = 0;
        await foreach (var chunk in chunks)
        {
            if (chunk?.Message?.Content is { Length: > 0 } text)
                yield return new LlmTextDelta(text);
            foreach (var call in chunk?.Message?.ToolCalls ?? [])
            {
                var arguments = call.Function?.Arguments is { } args ? JsonSerializer.Serialize(args) : null;
                yield return new LlmToolCallRequested(LlmToolCall.Read(call.Id ?? $"call_{made}", call.Function?.Name ?? "", arguments));
                made++;
            }
        }
    }

    /// <summary>Maps the neutral message list to Ollama chat messages (system inline; attachments
    /// listed as paths — image bytes go via vision-capable models only when the host allows; a reply's tool calls on its
    /// message, and what came of each as a tool message naming the tool). Internal + static so the mapping is unit-testable.</summary>
    internal static List<Message> BuildMessages(IReadOnlyList<LlmMessage> messages)
    {
        // Ollama's API accepts system messages inline in the messages array
        var msgList = new List<Message>();
        foreach (var msg in messages)
        {
            msgList.Add(msg.Role switch
            {
                LlmRole.System    => new Message { Role = ChatRole.System, Content = msg.Text },
                LlmRole.Assistant => new Message
                {
                    Role      = ChatRole.Assistant,
                    Content   = msg.Text,
                    ToolCalls = msg.ToolCalls is { Count: > 0 } calls ? [.. calls.Select(Called)] : null,
                },
                LlmRole.Tool      => new Message { Role = ChatRole.Tool, Content = msg.Text, ToolName = msg.Answers?.Name },
                _                 => new Message { Role = ChatRole.User, Content = BuildUserContent(msg.Text, msg.Attachments) },
            });
        }
        return msgList;
    }

    private static Message.ToolCall Called(LlmToolCall call) => new()
    {
        Id       = call.Id,
        Function = new Message.Function
        {
            Name      = call.Name,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(call.Arguments.ToJsonString()),
        },
    };

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
        await _details.WaitAsync(ct);
        if (_contextLength is null) await ReadModelAsync(ct);
        return _contextLength > 0 ? new ModelInfo(_contextLength.Value, _model) : null;
    }

    /// <summary>
    /// Warm-up: read the bound model's details (via /api/show) and trigger Ollama to load it
    /// into memory by issuing an empty generate with the configured keep_alive. Best-effort; never throws.
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return;

        await _details;

        try
        {
            var request = new GenerateRequest
            {
                Model     = _model,
                Prompt    = string.Empty,
                KeepAlive = _config.KeepAliveValue
            };
            await foreach (var _ in Client.GenerateAsync(request, ct)) { /* drain to completion */ }
            ModelUse.Loaded();
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

    /// <summary>Each chat streamed is one use, counted toward loading the model ahead.</summary>
    public ModelUseTracker ModelUse { get; } = new();

    /// <summary>Ollama unloads the model itself once its keep-alive runs out.</summary>
    public bool ReleasesItself => true;

    /// <summary>Reads the bound model's details from /api/show: its context length (0 on failure) and whether it calls
    /// tools.</summary>
    private async Task ReadModelAsync(CancellationToken ct)
    {
        try
        {
            var show = await Client.ShowModelAsync(new ShowModelRequest { Model = _model }, ct);
            _contextLength = ExtractContextLength(show);
            _toolCalls     = show?.Capabilities?.Contains("tools", StringComparer.OrdinalIgnoreCase) ?? false;
        }
        catch { _contextLength = 0; }
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
