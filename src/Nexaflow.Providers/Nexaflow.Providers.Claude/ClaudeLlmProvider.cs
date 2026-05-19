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

    public Task<LlmResponse?> QueryAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = new MessageParamContent(BuildUserContent(userPrompt, attachments)) }
        };

        return SendAsync(systemPrompt, messages, ct);
    }

    public Task<LlmResponse?> ChatAsync(
        IReadOnlyList<LlmMessage> history,
        string newUserPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        var messages = history
            .Select(m => new MessageParam
            {
                Role    = m.IsUser ? Role.User : Role.Assistant,
                Content = new MessageParamContent(m.Text)
            })
            .ToList();

        messages.Add(new MessageParam
        {
            Role    = Role.User,
            Content = new MessageParamContent(BuildUserContent(newUserPrompt, attachments))
        });

        return SendAsync(systemPrompt: null, messages, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(
        string? systemPrompt,
        IReadOnlyList<MessageParam> messages,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Claude ({_config.Model})…");
        try
        {
            var client = new AnthropicClient(new ClientOptions
            {
                ApiKey  = _config.ApiKey,
                BaseUrl = _config.BaseUrl
            });

            var request = new MessageCreateParams
            {
                Model     = _config.Model,
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

    private static string BuildUserContent(string prompt, IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return prompt;

        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var path in attachments)
            sb.AppendLine($"  {path}");

        return sb.ToString();
    }
}
