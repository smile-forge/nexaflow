using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Chat;
using System.Text;

#pragma warning disable OPENAI001 // chat audio input is marked for evaluation in the SDK, as the realtime API is

namespace Nexaflow.Providers.OpenAICompatible;

/// <summary>
/// A language model behind an OpenAI-compatible chat-completions API — OpenAI's own, or another vendor's speaking the
/// same wire format. What the format fixes is here: the neutral messages as chat messages, tools as functions, the reply
/// read back as it streams, and the model list. What differs by vendor — its name, its endpoint and key, which of its
/// models see or hear, how much each reads — is the vendor's provider's to say.
/// </summary>
public abstract class ChatCompletionsLlmProvider : ILlmProvider
{
    private readonly IBackgroundActivityManager _activityManager;
    private OpenAIClient?                       _client;
    private ChatClient?                         _chatClient;

    protected ChatCompletionsLlmProvider(IBackgroundActivityManager activityManager, ProviderModel model)
    {
        _activityManager = activityManager;
        Model            = model.Model;
    }

    /// <summary>The bound model; empty for the instance that only lists models.</summary>
    protected string Model { get; }

    public abstract string        Name        { get; }
    public abstract DataResidency Residency   { get; }
    public abstract bool          IsAvailable { get; }

    /// <summary>Whether the bound model takes image parts. Default: it does not.</summary>
    public virtual bool SupportsImages => false;

    /// <summary>Whether the bound model takes audio parts. Default: it does not.</summary>
    public virtual bool SupportsAudio => false;

    /// <summary>Chat-completions models call the tools they are offered as function calls, streamed in pieces.</summary>
    public bool SupportsToolCalls => true;

    public string? LastModelListError { get; private set; }

    /// <summary>The SDK client for the vendor's endpoint and key. The pooled instance is long-lived with a fixed config
    /// payload, so it is built once — and may be asked for by a read begun at construction as well as by a request.</summary>
    protected OpenAIClient Client => LazyInitializer.EnsureInitialized(ref _client, CreateClient);

    /// <summary>Builds <see cref="Client"/>: the vendor's key, and its endpoint.</summary>
    protected abstract OpenAIClient CreateClient();

    private ChatClient ChatClient => LazyInitializer.EnsureInitialized(ref _chatClient, () => Client.GetChatClient(Model));

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
        => Stream(messages, [], ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool>    tools,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
        => Stream(messages, tools, ct);

    private IAsyncEnumerable<LlmStreamEvent> Stream(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, CancellationToken ct)
    {
        var chatMessages = BuildChatMessages(messages, SupportsAudio);
        return LlmStreamRunner.RunAsync(_activityManager, $"{Name} ({Model})…", Name,
            ct => Events(ChatClient.CompleteChatStreamingAsync(chatMessages, Options(tools), ct)), ct);
    }

    /// <summary>Metadata about the bound model, or null when the vendor's provider does not know it. Default: null.</summary>
    public virtual Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult<ModelInfo?>(null);

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var models = await ListModelsAsync(ct);
            LastModelListError = null;
            return models;
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    /// <summary>The models the endpoint offers, sorted. May throw: <see cref="GetAvailableModelsAsync"/> keeps the reason
    /// as <see cref="LastModelListError"/>. Default: every model the OpenAI-compatible model list names.</summary>
    protected virtual async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        var result = await Client.GetOpenAIModelClient().GetModelsAsync(ct);
        return result.Value.Select(m => m.Id).Order().ToList();
    }

    // ── The wire format ────────────────────────────────────────────────────

    /// <summary>A fresh request's options — the SDK's options are not reused across calls.</summary>
    private static ChatCompletionOptions Options(IReadOnlyList<LlmTool> tools)
    {
        var options = new ChatCompletionOptions();
        foreach (var tool in tools) options.Tools.Add(Declare(tool));
        return options;
    }

    /// <summary>A tool as chat completions declare it: a function, its parameters' JSON Schema as given.</summary>
    internal static ChatTool Declare(LlmTool tool)
        => ChatTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.Parameters.ToJsonString()));

    /// <summary>The reply as it streams: its text, then each call — a call comes in pieces keyed by its index, the first
    /// naming it, so the calls are read once the stream ends.</summary>
    internal static async IAsyncEnumerable<LlmStreamEvent> Events(IAsyncEnumerable<StreamingChatCompletionUpdate> updates)
    {
        var calls = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Arguments)>();
        await foreach (var update in updates)
        {
            foreach (var part in update.ContentUpdate)
                if (!string.IsNullOrEmpty(part.Text))
                    yield return new LlmTextDelta(part.Text);

            foreach (var piece in update.ToolCallUpdates)
            {
                var (id, name, arguments) = calls.TryGetValue(piece.Index, out var call) ? call : (null, null, new StringBuilder());
                if (piece.FunctionArgumentsUpdate is { } written) arguments.Append(written.ToString());
                calls[piece.Index] = (id ?? piece.ToolCallId, name ?? piece.FunctionName, arguments);
            }
        }

        foreach (var (index, (id, name, arguments)) in calls)
            yield return new LlmToolCallRequested(LlmToolCall.Read(id ?? $"call_{index}", name ?? "", arguments.ToString()));
    }

    /// <summary>Maps the neutral message list to chat messages (system/assistant/user roles; image attachments as vision
    /// parts; a reply's tool calls on its assistant message, and what came of each as a tool message answering it).
    /// Internal + static so the mapping is unit-testable.</summary>
    /// <param name="hearsAudio">The model takes audio input: recordings go as audio rather than as named files.</param>
    internal static List<ChatMessage> BuildChatMessages(IReadOnlyList<LlmMessage> messages, bool hearsAudio = false)
    {
        var chatMessages = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            chatMessages.Add(msg.Role switch
            {
                LlmRole.System    => new SystemChatMessage(msg.Text),
                LlmRole.Assistant => BuildAssistantMessage(msg),
                LlmRole.Tool      => new ToolChatMessage(msg.Answers?.Id ?? "", msg.Text),
                _                 => BuildUserMessage(msg.Text, msg.Attachments, hearsAudio)
            });
        }
        return chatMessages;
    }

    /// <summary>A reply: its text, and the tools it called.</summary>
    private static AssistantChatMessage BuildAssistantMessage(LlmMessage msg)
    {
        if (msg.ToolCalls is not { Count: > 0 } calls) return new AssistantChatMessage(msg.Text);

        var message = new AssistantChatMessage(calls.Select(c =>
            ChatToolCall.CreateFunctionToolCall(c.Id, c.Name, BinaryData.FromString(c.Arguments.ToJsonString()))));
        if (!string.IsNullOrWhiteSpace(msg.Text)) message.Content.Add(ChatMessageContentPart.CreateTextPart(msg.Text));
        return message;
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
