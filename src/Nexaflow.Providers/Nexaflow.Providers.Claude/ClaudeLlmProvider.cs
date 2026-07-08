using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Nexaflow.Providers.Common;
using AnthropicModelList = Anthropic.Models.Models;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Claude";
    public string Name => ProviderName;

    /// <summary>Every Claude model in the current catalogue (Claude 3 and later) accepts image input.</summary>
    public bool SupportsImages => true;

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

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
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

        return LlmStreamRunner.RunAsync(_activityManager, $"Claude ({_model})…", ProviderName,
            ct => Deltas(request, ct), ct);
    }

    private async IAsyncEnumerable<string?> Deltas(
        MessageCreateParams request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in Client.Messages.CreateStreaming(request, ct))
        {
            if (evt.TryPickContentBlockDelta(out var deltaEvt) &&
                deltaEvt.Delta.TryPickText(out var textDelta))
                yield return textDelta.Text;
        }
    }

    /// <summary>
    /// Maps the neutral message list to Claude's wire shape (system prompt split out; image
    /// attachments as base64 vision blocks). Internal + static so the mapping is unit-testable
    /// without a network call.
    /// </summary>
    internal static (string? SystemPrompt, List<MessageParam> Messages) BuildRequest(
        IReadOnlyList<LlmMessage> messages)
    {
        // Claude's API separates the system prompt from the messages array
        var (systemPrompt, turns) = PromptComposer.SplitSystemPrompt(messages);

        var msgParams = new List<MessageParam>();
        foreach (var msg in turns)
        {
            var content = msg.Role == LlmRole.User
                ? BuildUserContent(msg.Text, msg.Attachments)
                : new MessageParamContent(msg.Text);

            msgParams.Add(new MessageParam
            {
                Role    = msg.Role == LlmRole.User ? Role.User : Role.Assistant,
                Content = content
            });
        }
        return (systemPrompt, msgParams);
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
    internal static MessageParamContent BuildUserContent(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        var (images, files) = PromptComposer.PartitionAttachments(attachments);
        var text = PromptComposer.AppendFileList(prompt, files);

        if (images.Count == 0)
            return new MessageParamContent(text);

        var blocks = new List<ContentBlockParam> { new TextBlockParam(text) };
        foreach (var img in images)
            blocks.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data      = Convert.ToBase64String(img.ReadBytes()),
                    MediaType = ToMediaType(img.ResolvedMimeType),
                },
            });

        return blocks;   // implicit List<ContentBlockParam> -> MessageParamContent
    }

    private static MediaType ToMediaType(string mime) => mime switch
    {
        "image/jpeg" => MediaType.ImageJpeg,
        "image/gif"  => MediaType.ImageGif,
        "image/webp" => MediaType.ImageWebP,
        _            => MediaType.ImagePng,
    };
}
