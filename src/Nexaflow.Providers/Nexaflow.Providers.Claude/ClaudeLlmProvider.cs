using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Nexaflow.Providers.Common;
using AnthropicModelList = Anthropic.Models.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Claude";
    public string Name => ProviderName;

    /// <summary>Every Claude model in the current catalogue (Claude 3 and later) accepts image input.</summary>
    public bool SupportsImages => true;

    /// <summary>Anthropic's API, unless the base URL points at a server on this machine.</summary>
    public DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    private readonly ClaudeConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private AnthropicClient?                    _client;

    public ClaudeLlmProvider(IBackgroundActivityManager activityManager, ClaudeConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // The pooled instance is long-lived and its config payload is fixed (the pool key includes it),
    // so the SDK client — and its HttpClient — is built once and reused across calls.
    private AnthropicClient Client => _client ??= new AnthropicClient(new ClientOptions
    {
        ApiKey  = _config.ApiKey,
        BaseUrl = _config.BaseUrl
    });

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
        => Stream(Request(messages, []), ct);

    /// <summary>Claude calls the tools it is offered in its own protocol: each declared with its JSON Schema, each call
    /// streamed back as a <c>tool_use</c> block.</summary>
    public bool SupportsToolCalls => true;

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool>    tools,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
        => Stream(Request(messages, tools), ct);

    private IAsyncEnumerable<LlmStreamEvent> Stream(MessageCreateParams request, CancellationToken ct)
        => LlmStreamRunner.RunAsync(_activityManager, $"Claude ({_model})…", ProviderName,
            ct => Events(Client.Messages.CreateStreaming(request, ct)), ct);

    private MessageCreateParams Request(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools)
    {
        var (systemPrompt, msgParams) = BuildRequest(messages);

        var request = new MessageCreateParams
        {
            Model     = _model,
            MaxTokens = _config.MaxOutputTokens > 0 ? _config.MaxOutputTokens : DefaultMaxOutputTokens(_model),
            Messages  = msgParams
        };
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            request = request with { System = new MessageCreateParamsSystem(systemPrompt, null) };
        if (tools.Count > 0)
            request = request with { Tools = [.. tools.Select(Declare)] };
        return request;
    }

    /// <summary>The reply as it streams: its text, and each tool call once its block closes — the arguments arrive as
    /// pieces of JSON between the block's start and its stop.</summary>
    internal static async IAsyncEnumerable<LlmStreamEvent> Events(IAsyncEnumerable<RawMessageStreamEvent> stream)
    {
        var calls = new Dictionary<long, (string Id, string Name, StringBuilder Arguments)>();
        await foreach (var evt in stream)
        {
            if (evt.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickToolUse(out var use))
                calls[start.Index] = (use.ID, use.Name, new StringBuilder());
            else if (evt.TryPickContentBlockDelta(out var deltaEvt))
            {
                if (deltaEvt.Delta.TryPickText(out var textDelta) && !string.IsNullOrEmpty(textDelta.Text))
                    yield return new LlmTextDelta(textDelta.Text);
                else if (deltaEvt.Delta.TryPickInputJson(out var json) && calls.TryGetValue(deltaEvt.Index, out var writing))
                    writing.Arguments.Append(json.PartialJson);
            }
            else if (evt.TryPickContentBlockStop(out var stop) && calls.Remove(stop.Index, out var made))
                yield return new LlmToolCallRequested(LlmToolCall.Read(made.Id, made.Name, made.Arguments.ToString()));
        }
    }

    /// <summary>A tool as Claude declares it: its parameters' JSON Schema is the tool's input schema.</summary>
    internal static ToolUnion Declare(LlmTool tool) => new Tool
    {
        Name        = tool.Name,
        Description = tool.Description,
        InputSchema = InputSchema.FromRawUnchecked(Raw(tool.Parameters)),
    };

    private static Dictionary<string, JsonElement> Raw(JsonObject json)
        => json.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

    /// <summary>
    /// Maps the neutral message list to Claude's wire shape (system prompt split out; image
    /// attachments as base64 vision blocks; a reply's tool calls as <c>tool_use</c> blocks). What came of a step's calls
    /// is the user turn after it — every <c>tool_result</c>, then anything the user says besides. Internal + static so the
    /// mapping is unit-testable without a network call.
    /// </summary>
    internal static (string? SystemPrompt, List<MessageParam> Messages) BuildRequest(
        IReadOnlyList<LlmMessage> messages)
    {
        // Claude's API separates the system prompt from the messages array
        var (systemPrompt, turns) = PromptComposer.SplitSystemPrompt(messages);

        var msgParams = new List<MessageParam>();
        List<ContentBlockParam>? results = null;   // a step's tool results, awaiting the user turn they open
        foreach (var msg in turns)
        {
            if (msg.Role == LlmRole.Tool)
            {
                (results ??= []).Add(new ToolResultBlockParam(msg.Answers?.Id ?? "") { Content = msg.Text });
                continue;
            }
            if (results is not null)
            {
                if (msg.Role == LlmRole.User) results.AddRange(UserBlocks(msg.Text, msg.Attachments));
                msgParams.Add(new UserMessageParam(results));
                results = null;
                if (msg.Role == LlmRole.User) continue;
            }

            msgParams.Add(msg.Role == LlmRole.User
                ? new UserMessageParam(BuildUserContent(msg.Text, msg.Attachments))
                : new AssistantMessageParam(AssistantContent(msg)));
        }
        if (results is not null)
            msgParams.Add(new UserMessageParam(results));
        return (systemPrompt, msgParams);
    }

    /// <summary>A reply: its text, then each tool it called.</summary>
    private static MessageContentParam AssistantContent(LlmMessage msg)
    {
        if (msg.ToolCalls is not { Count: > 0 } calls) return new MessageContentParam(msg.Text);

        var blocks = new List<ContentBlockParam>();
        if (!string.IsNullOrWhiteSpace(msg.Text)) blocks.Add(new TextBlockParam(msg.Text));
        foreach (var call in calls)
            blocks.Add(new ToolUseBlockParam { ID = call.Id, Name = call.Name, Input = Raw(call.Arguments) });
        return blocks;
    }

    /// <summary>Per-model output ceiling, used when the config doesn't override it. Values are
    /// deliberately below each family's documented maximum so a catalogue drift can't 400.</summary>
    internal static int DefaultMaxOutputTokens(string model) => model switch
    {
        _ when model.Contains("claude-3-opus", StringComparison.OrdinalIgnoreCase) => 4096,
        _ when model.Contains("claude-3",      StringComparison.OrdinalIgnoreCase) => 8192,
        _ => 32_000,   // Claude 4 / 5 families accept ≥32k output
    };

    /// <summary>Per-model context window. All currently shipping models are 200k; this is the seam
    /// a larger-context variant extends (keyed on the bound model id).</summary>
    internal static int ContextWindowTokens(string model) => 200_000;

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // Prefer the live models endpoint (new models appear without a code change); fall back to
        // the static catalogue when unreachable so the options UI is never empty for Claude.
        try
        {
            var names = new List<string>();
            var page  = await Client.Models.List(new AnthropicModelList.ModelListParams(), ct);
            while (true)
            {
                foreach (var m in page.Items) names.Add(m.ID);
                if (!page.HasNext()) break;
                page = await page.Next(ct);
            }
            if (names.Count > 0) return names;
        }
        catch { /* fall through to the static catalogue */ }

        return FallbackModels;
    }

    internal static readonly IReadOnlyList<string> FallbackModels =
    [
        "claude-fable-5",
        "claude-opus-4-8",
        "claude-sonnet-5",
        "claude-haiku-4-5-20251001",
        "claude-opus-4-7",
        "claude-sonnet-4-6",
    ];

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        ModelInfo? info = string.IsNullOrWhiteSpace(_model)
            ? null
            : new ModelInfo(ContextWindowTokens(_model), DisplayName: _model);
        return Task.FromResult(info);
    }

    /// <summary>
    /// Builds the final user turn. Image attachments become native vision blocks (base64); non-image
    /// attachments keep the path-as-text behaviour. With no images, returns a plain string content.
    /// </summary>
    internal static MessageContentParam BuildUserContent(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        var (images, files) = PromptComposer.PartitionAttachments(attachments);
        return images.Count == 0
            ? new MessageContentParam(PromptComposer.AppendFileList(prompt, files))
            : UserBlocks(prompt, attachments);   // implicit List<ContentBlockParam> -> MessageContentParam
    }

    /// <summary>What the user says as blocks: the text, unless there is none, then each image as a vision block.</summary>
    private static List<ContentBlockParam> UserBlocks(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        var (images, files) = PromptComposer.PartitionAttachments(attachments);
        var text = PromptComposer.AppendFileList(prompt, files);

        var blocks = new List<ContentBlockParam>();
        if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new TextBlockParam(text));
        foreach (var img in images)
            blocks.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data      = Convert.ToBase64String(img.ReadBytes()),
                    MediaType = ToMediaType(img.ResolvedMimeType),
                },
            });
        return blocks;
    }

    private static MediaType ToMediaType(string mime) => mime switch
    {
        "image/jpeg" => MediaType.ImageJpeg,
        "image/gif"  => MediaType.ImageGif,
        "image/webp" => MediaType.ImageWebP,
        _            => MediaType.ImagePng,
    };
}
