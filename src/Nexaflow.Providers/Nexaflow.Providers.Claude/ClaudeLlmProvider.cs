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

    private readonly ClaudeConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;

    public ClaudeLlmProvider(IBackgroundActivityManager activityManager, ClaudeConfig config)
    {
        _activityManager = activityManager;
        _config          = config;
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage>     messages,
        string                        model,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken             ct = default)
    {
        // Claude's API separates the system prompt from the messages array
        string? systemPrompt = null;
        var start = 0;
        if (messages.Count > 0 && messages[0].Role == LlmRole.System)
        {
            systemPrompt = messages[0].Text;
            start        = 1;
        }

        var msgParams = new List<MessageParam>();
        for (var i = start; i < messages.Count; i++)
        {
            var msg        = messages[i];
            var isLastUser = msg.Role == LlmRole.User && i == messages.Count - 1;
            var content    = isLastUser ? BuildUserContent(msg.Text, attachments) : msg.Text;

            msgParams.Add(new MessageParam
            {
                Role    = msg.Role == LlmRole.User ? Role.User : Role.Assistant,
                Content = new MessageParamContent(content)
            });
        }

        return SendAsync(systemPrompt, model, msgParams, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(
        string? systemPrompt,
        string model,
        IReadOnlyList<MessageParam> messages,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Claude ({model})…");
        try
        {
            var client = new AnthropicClient(new ClientOptions
            {
                ApiKey  = _config.ApiKey,
                BaseUrl = _config.BaseUrl
            });

            var request = new MessageCreateParams
            {
                Model     = model,
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

    private static string BuildUserContent(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return prompt;

        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var a in attachments)
            sb.AppendLine($"  {a.FilePath}");

        return sb.ToString();
    }
}
