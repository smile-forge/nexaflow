using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Nexaflow.Providers.Common;
using System.Text;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Claude";
    public string Name => ProviderName;

    /// <summary>Every Claude model in our list (Claude 3 and later) accepts image input.</summary>
    public bool SupportsImages => true;

    private readonly ClaudeConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;

    public ClaudeLlmProvider(IBackgroundActivityManager activityManager, ClaudeConfig config, ProviderModel model)
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

        return SendAsync(systemPrompt, msgParams, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(
        string? systemPrompt,
        IReadOnlyList<MessageParam> messages,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Claude ({_model})…");
        try
        {
            var client = new AnthropicClient(new ClientOptions
            {
                ApiKey  = _config.ApiKey,
                BaseUrl = _config.BaseUrl
            });

            var request = new MessageCreateParams
            {
                Model     = _model,
                MaxTokens = 8096,
                Messages  = messages
            };

            if (!string.IsNullOrWhiteSpace(systemPrompt))
                request = request with { System = new MessageCreateParamsSystem(systemPrompt, null) };

            var sb = new StringBuilder();
            await foreach (var evt in client.Messages.CreateStreaming(request, ct))
            {
                if (evt.TryPickContentBlockDelta(out var deltaEvt) &&
                    deltaEvt.Delta.TryPickText(out var textDelta))
                {
                    sb.Append(textDelta.Text);
                }
            }

            activity.Complete();
            var text = sb.ToString();
            return string.IsNullOrEmpty(text) ? null : new LlmResponse(text);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation is a clean stop, not a provider failure — propagate unwrapped.
            activity.Fail("canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"Claude request failed: {ex.Message}", ex);
        }
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> models =
        [
            "claude-opus-4-7",
            "claude-sonnet-4-6",
            "claude-haiku-4-5-20251001",
            "claude-3-5-sonnet-20241022",
            "claude-3-5-haiku-20241022",
            "claude-3-opus-20240229",
        ];
        return Task.FromResult(models);
    }

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        // All currently-shipping Claude models in our model list use a 200k context window.
        // Future models with larger windows can override here.
        ModelInfo? info = string.IsNullOrWhiteSpace(_model)
            ? null
            : new ModelInfo(ContextWindowTokens: 200_000, DisplayName: _model);
        return Task.FromResult(info);
    }

    /// <summary>
    /// Builds the final user turn. Image attachments become native vision blocks (base64); non-image
    /// attachments keep the path-as-text behaviour. With no images, returns a plain string content.
    /// </summary>
    private static MessageParamContent BuildUserContent(string prompt, IReadOnlyList<LlmAttachment>? attachments)
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
