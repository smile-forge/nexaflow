using Nexaflow.Providers.Common;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using System.Text;

namespace Nexaflow.Providers.Ollama;

public sealed class OllamaLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Ollama";
    public string Name => ProviderName;

    private readonly OllamaConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;

    public OllamaLlmProvider(IBackgroundActivityManager activityManager, OllamaConfig config)
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
        var messages = new List<Message>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new Message { Role = ChatRole.System, Content = systemPrompt });

        messages.Add(new Message { Role = ChatRole.User, Content = BuildUserContent(userPrompt, attachments) });

        return SendAsync(messages, ct);
    }

    public Task<LlmResponse?> ChatAsync(
        IReadOnlyList<LlmMessage> history,
        string newUserPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        var messages = history
            .Select(m => new Message
            {
                Role    = m.IsUser ? ChatRole.User : ChatRole.Assistant,
                Content = m.Text
            })
            .ToList();

        messages.Add(new Message { Role = ChatRole.User, Content = BuildUserContent(newUserPrompt, attachments) });

        return SendAsync(messages, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(List<Message> messages, CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Ollama ({_config.Model})…");
        try
        {
            var client  = new OllamaApiClient(new Uri(_config.Url));
            var request = new ChatRequest
            {
                Model    = _config.Model,
                Messages = messages
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
