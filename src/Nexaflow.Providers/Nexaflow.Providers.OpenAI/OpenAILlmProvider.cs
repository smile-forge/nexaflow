using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

#pragma warning disable OPENAI001 // chat audio input is marked for evaluation in the SDK, as the realtime API is

namespace Nexaflow.Providers.OpenAI;

public sealed class OpenAILlmProvider : ILlmProvider
{
    public const  string ProviderName = "OpenAI";
    public string Name => ProviderName;

    /// <summary>Vision is model-bound: the chat families accept image parts, the specialised
    /// models (embeddings, audio, images, legacy 3.5/instruct) don't.</summary>
    public bool SupportsImages => ModelSupportsVision(_model);
    public bool SupportsAudio  => ModelHearsAudio(_model);

    /// <summary>OpenAI's chat models that take audio input are named for it (gpt-audio, gpt-4o-audio-preview…);
    /// its realtime, transcription and speech models are reached by other APIs.</summary>
    internal static bool ModelHearsAudio(string model)
        => model.Contains("audio", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("realtime", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("transcribe", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("tts", StringComparison.OrdinalIgnoreCase);

    /// <summary>OpenAI's API, unless the base URL points at an OpenAI-compatible server on this machine.</summary>
    public DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private OpenAIClient?                       _client;
    private ChatClient?                         _chatClient;

    public string? LastModelListError { get; private set; }

    public OpenAILlmProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // The pooled instance is long-lived with a fixed config payload — build the SDK client once.
    private OpenAIClient Client
    {
        get => _client ??= OpenAIClients.Create(_config);
    }

    private ChatClient ChatClient => _chatClient ??= Client.GetChatClient(_model);

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
    {
        var chatMessages = BuildChatMessages(messages, SupportsAudio);
        return LlmStreamRunner.RunAsync(_activityManager, $"OpenAI ({_model})…", ProviderName,
            ct => Events(chatMessages, ct), ct);
    }

    private async IAsyncEnumerable<LlmStreamEvent> Events(
        List<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in ChatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
            foreach (var part in update.ContentUpdate)
                if (!string.IsNullOrEmpty(part.Text))
                    yield return new LlmTextDelta(part.Text);
    }

    /// <summary>Maps the neutral message list to OpenAI chat messages (system/assistant/user roles;
    /// image attachments as vision parts). Internal + static so the mapping is unit-testable.</summary>
    /// <param name="hearsAudio">The model takes audio input: recordings go as audio rather than as named files.</param>
    internal static List<ChatMessage> BuildChatMessages(IReadOnlyList<LlmMessage> messages, bool hearsAudio = false)
    {
        var chatMessages = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            chatMessages.Add(msg.Role switch
            {
                LlmRole.System    => new SystemChatMessage(msg.Text),
                LlmRole.Assistant => new AssistantChatMessage(msg.Text),
                _                 => BuildUserMessage(msg.Text, msg.Attachments, hearsAudio)
            });
        }
        return chatMessages;
    }

    /// <summary>Name-based vision heuristic — specialised/legacy families are text-only, current
    /// chat families (gpt-4o/4.1/5, o-series) accept image parts.</summary>
    internal static bool ModelSupportsVision(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        string[] textOnly = ["embedding", "whisper", "tts", "dall-e", "audio", "moderation",
                             "instruct", "gpt-3.5", "davinci", "babbage"];
        foreach (var t in textOnly)
            if (model.Contains(t, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>Context-window table by family; null when unknown (the host then skips budgeting).</summary>
    internal static int? ContextWindowTokens(string model) => model switch
    {
        _ when model.Contains("gpt-4.1",     StringComparison.OrdinalIgnoreCase) => 1_000_000,
        _ when model.Contains("gpt-5",       StringComparison.OrdinalIgnoreCase) => 400_000,
        _ when model.Contains("gpt-4o",      StringComparison.OrdinalIgnoreCase) => 128_000,
        _ when model.Contains("gpt-4-turbo", StringComparison.OrdinalIgnoreCase) => 128_000,
        _ when model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase)        => 200_000,
        _ when model.Contains("gpt-3.5",     StringComparison.OrdinalIgnoreCase) => 16_385,
        _ => null,
    };

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        ModelInfo? info = !string.IsNullOrWhiteSpace(_model) && ContextWindowTokens(_model) is { } window
            ? new ModelInfo(window, DisplayName: _model)
            : null;
        return Task.FromResult(info);
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var modelClient = Client.GetOpenAIModelClient();
            var result      = await modelClient.GetModelsAsync(ct);

            LastModelListError = null;
            return result.Value
                         .Select(m => m.Id)
                         .Order()
                         .ToList();
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    /// <summary>
    /// Builds the final user message. Image attachments become native vision content parts; non-image
    /// attachments keep the path-as-text behaviour. With no images, returns a plain-text message.
    /// </summary>
    internal static UserChatMessage BuildUserMessage(string prompt, IReadOnlyList<LlmAttachment>? attachments, bool hearsAudio = false)
    {
        var (images, others) = PromptComposer.PartitionAttachments(attachments);
        var audio = hearsAudio ? others.Where(a => a.IsAudio && AudioFormat(a) is not null).ToList() : [];
        var text  = PromptComposer.AppendFileList(prompt, [.. others.Except(audio)]);

        if (images.Count == 0 && audio.Count == 0)
            return new UserChatMessage(text);

        var parts = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(text) };
        foreach (var img in images)
            parts.Add(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(img.ReadBytes()), img.ResolvedMimeType));
        foreach (var clip in audio)
            parts.Add(ChatMessageContentPart.CreateInputAudioPart(BinaryData.FromBytes(clip.ReadBytes()), AudioFormat(clip)!.Value));

        return new UserChatMessage(parts);
    }

    /// <summary>The formats chat completions take audio in: WAV and MP3.</summary>
    private static ChatInputAudioFormat? AudioFormat(LlmAttachment clip)
        => (clip.MimeType?.ToLowerInvariant(), Path.GetExtension(clip.FilePath).ToLowerInvariant()) switch
        {
            ("audio/wav" or "audio/x-wav" or "audio/wave", _) or (null, ".wav") => ChatInputAudioFormat.Wav,
            ("audio/mpeg" or "audio/mp3", _) or (null, ".mp3")                   => ChatInputAudioFormat.Mp3,
            _                                                                    => null,
        };
}
