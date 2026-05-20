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

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage>     messages,
        string                        model,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken             ct = default)
    {
        // Ollama's API accepts system messages inline in the messages array
        var msgList = new List<Message>();
        for (var i = 0; i < messages.Count; i++)
        {
            var msg  = messages[i];
            ChatRole role = msg.Role switch
            {
                LlmRole.System    => ChatRole.System,
                LlmRole.Assistant => ChatRole.Assistant,
                _                 => ChatRole.User
            };
            var isLastUser = msg.Role == LlmRole.User && i == messages.Count - 1;
            var content    = isLastUser ? BuildUserContent(msg.Text, attachments) : msg.Text;

            msgList.Add(new Message { Role = role, Content = content });
        }

        return SendAsync(model, msgList, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(string model, List<Message> messages, CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Ollama ({model})…");
        try
        {
            var client  = new OllamaApiClient(new Uri(_config.Url));
            var request = new ChatRequest
            {
                Model    = model,
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
