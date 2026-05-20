using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;

namespace Nexaflow.Providers.OpenAI;

public sealed class OpenAILlmProvider : ILlmProvider
{
    public const  string ProviderName = "OpenAI";
    public string Name => ProviderName;

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;

    public OpenAILlmProvider(IBackgroundActivityManager activityManager, OpenAIConfig config)
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
        var chatMessages = new List<ChatMessage>();
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            chatMessages.Add(msg.Role switch
            {
                LlmRole.System    => new SystemChatMessage(msg.Text),
                LlmRole.Assistant => new AssistantChatMessage(msg.Text),
                _                 => new UserChatMessage(BuildUserContent(msg.Text,
                                         i == messages.Count - 1 ? attachments : null))
            });
        }

        return SendAsync(model, chatMessages, ct);
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(_config.BaseUrl))
                clientOptions.Endpoint = new Uri(_config.BaseUrl);

            var client      = new OpenAIClient(new ApiKeyCredential(_config.ApiKey), clientOptions);
            var modelClient = client.GetOpenAIModelClient();
            var result      = await modelClient.GetModelsAsync(ct);

            return result.Value
                         .Select(m => m.Id)
                         .Order()
                         .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(
        string                     model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken          ct)
    {
        var activity = _activityManager.StartActivity($"OpenAI ({model})…");
        try
        {
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(_config.BaseUrl))
                clientOptions.Endpoint = new Uri(_config.BaseUrl);

            var client     = new OpenAIClient(new ApiKeyCredential(_config.ApiKey), clientOptions);
            var chatClient = client.GetChatClient(model);

            var sb = new StringBuilder();
            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
            {
                foreach (var part in update.ContentUpdate)
                    sb.Append(part.Text);
            }

            activity.Complete();
            var text = sb.ToString();
            return string.IsNullOrEmpty(text) ? null : new LlmResponse(text);
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"OpenAI request failed: {ex.Message}", ex);
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
