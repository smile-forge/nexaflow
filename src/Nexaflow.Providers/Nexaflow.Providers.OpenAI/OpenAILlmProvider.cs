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

    /// <summary>OpenAI's current chat models are multimodal; image attachments are sent as vision parts.</summary>
    public bool SupportsImages => true;

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;

    public OpenAILlmProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
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
        var chatMessages = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            chatMessages.Add(msg.Role switch
            {
                LlmRole.System    => new SystemChatMessage(msg.Text),
                LlmRole.Assistant => new AssistantChatMessage(msg.Text),
                _                 => BuildUserMessage(msg.Text, msg.Attachments)
            });
        }

        return SendAsync(chatMessages, ct);
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
        IReadOnlyList<ChatMessage> messages,
        CancellationToken          ct)
    {
        var activity = _activityManager.StartActivity($"OpenAI ({_model})…");
        try
        {
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(_config.BaseUrl))
                clientOptions.Endpoint = new Uri(_config.BaseUrl);

            var client     = new OpenAIClient(new ApiKeyCredential(_config.ApiKey), clientOptions);
            var chatClient = client.GetChatClient(_model);

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

    /// <summary>
    /// Builds the final user message. Image attachments become native vision content parts; non-image
    /// attachments keep the path-as-text behaviour. With no images, returns a plain-text message.
    /// </summary>
    private static UserChatMessage BuildUserMessage(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return new UserChatMessage(prompt);

        var images = attachments.Where(a => a.IsImage).ToList();
        var files  = attachments.Where(a => !a.IsImage).ToList();
        var text   = files.Count == 0 ? prompt : AppendFileList(prompt, files);

        if (images.Count == 0)
            return new UserChatMessage(text);

        var parts = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(text) };
        foreach (var img in images)
            parts.Add(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(img.ReadBytes()), img.ResolvedMimeType));

        return new UserChatMessage(parts);
    }

    private static string AppendFileList(string prompt, IReadOnlyList<LlmAttachment> files)
    {
        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var a in files)
            sb.AppendLine($"  {a.FilePath}");
        return sb.ToString();
    }
}
