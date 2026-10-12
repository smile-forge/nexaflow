using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Audio;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The model's next turn, streamed as it is generated. The conversation is continued where the model already holds
/// it — its new turns appended as a delta against what was read — or read afresh; then tokens are generated one GPU
/// step at a time until the model ends its turn, the request's limit, or the context's.
/// </summary>
internal sealed class GenerateHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<Generate>
{
    protected override async Task HandleAsync(Generate request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        using var lease = await models.AcquireAsync(request.Model, resident: false,
            stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        var model = lease.As<ChatModel>();

        LiveConversation? conversation = model.Conversations.TakeContinued(request.Turns);
        try
        {
            int read;
            if (conversation is not null && Delta(model, conversation, request.Turns) is { } delta)
            {
                read = await AppendAsync(model, conversation, delta, request.Priority, ct);
            }
            else
            {
                conversation?.Dispose();
                conversation = model.Conversations.Start();
                read = await ReadAfreshAsync(model, conversation, request.Turns, request.Priority, ct);
            }

            var (reason, text, tokens, prompt) = await GenerateAsync(model, conversation, request, reply, ct);
            conversation.Answered(request.Turns, text);
            model.Conversations.Keep(conversation);
            conversation = null;

            await reply(new GenerationEnded(request.RequestId, reason, prompt, read == 0 ? prompt : read, tokens.Count));
        }
        finally
        {
            conversation?.Dispose();   // failed or cancelled part-way: its context is not one anybody will continue
        }
    }

    /// <summary>What extends the conversation's context to <paramref name="turns"/> — see <see cref="ConversationMatch.Delta"/>.</summary>
    private static string? Delta(ChatModel model, LiveConversation conversation, IReadOnlyList<ChatTurn> turns)
        => ConversationMatch.Delta(conversation.Read, turns,
                                   (t, generationPrompt) => ChatPrompt.Render(model, t, generationPrompt, MediaPlan.None));

    private async Task<int> AppendAsync(ChatModel model, LiveConversation conversation, string text, RequestPriority priority,
                                        CancellationToken ct)
    {
        using var sequences = model.Tokenizer.Encode(text);
        var tokens = sequences[0].ToArray();
        EnsureRoom(model, conversation.Tokens + tokens.Length);
        for (int at = 0; at < tokens.Length; at += LiveConversation.Chunk)
            using (await gpu.NextAsync(priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                conversation.Generator.AppendTokens(tokens.AsSpan(at, Math.Min(LiveConversation.Chunk, tokens.Length - at)));
            }
        return tokens.Length;
    }

    /// <summary>Reads the whole conversation into a new context — with its newest image and recording.
    /// Returns the tokens read, or 0 when media is read and the count is known only once the first token is out.</summary>
    private async Task<int> ReadAfreshAsync(ChatModel model, LiveConversation conversation, IReadOnlyList<ChatTurn> turns,
                                            RequestPriority priority, CancellationToken ct)
    {
        var media = MediaPlan.Of(turns);
        string prompt = ChatPrompt.Render(model, turns, generationPrompt: true, media);
        if (media.IsEmpty) return await AppendAsync(model, conversation, prompt, priority, ct);

        using var images = media.Image is { } image ? Images.Load(Bytes(image)) : null;
        using var audios = media.Audio is { } audio ? Audios.Load(SpeechWav(audio)) : null;
        using var inputs = (images, audios) switch
        {
            ({ } i, { } a) => model.Processor.ProcessImagesAndAudios(prompt, i, a),
            ({ } i, null)  => model.Processor.ProcessImages(prompt, i),
            _              => model.Processor.ProcessAudios(prompt, audios!),
        };
        using (await gpu.NextAsync(priority, ct))
            conversation.Generator.SetInputs(inputs);
        return 0;
    }

    private static byte[] Bytes(ChatMedia media)
    {
        if (media.Bytes is { Length: > 0 } bytes) return bytes;
        if (media.Path is { } path && File.Exists(path)) return File.ReadAllBytes(path);
        throw new HostRequestException(HostErrorKind.NotFound, $"The {media.Kind.ToString().ToLowerInvariant()} to read is not there: {media.Path}.");
    }

    /// <summary>A recording as the model's audio processor takes it — 16 kHz mono — which it cannot make of a lower rate.</summary>
    private static byte[] SpeechWav(ChatMedia media)
    {
        try { return WavAudio.ToMono(Bytes(media)); }
        catch (InvalidDataException ex) { throw new HostRequestException(HostErrorKind.Invalid, $"The recording cannot be read: {ex.Message}"); }
    }

    /// <summary>Generates until the model ends its turn or a limit is reached. The prompt's length is taken as the first
    /// token comes out — a turn read with media is counted only then — and the end token a reply stops on is never kept,
    /// so the count after the reply would be one short.</summary>
    private async Task<(StopReason Reason, string Text, List<int> Tokens, int PromptTokens)> GenerateAsync(
        ChatModel model, LiveConversation conversation, Generate request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        using var stream = model.Tokenizer.CreateStream();
        var tokens = new List<int>();
        var text = new StringBuilder();
        int prompt = conversation.Tokens;
        while (true)
        {
            if (tokens.Count >= request.MaxNewTokens) return (StopReason.MaxTokens, text.ToString(), tokens, prompt);
            if (conversation.Generator.IsDone()) return (StopReason.ContextFull, text.ToString(), tokens, prompt);

            using (await gpu.NextAsync(request.Priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                conversation.Generator.GenerateNextToken();
            }
            if (tokens.Count == 0) prompt = conversation.Tokens - 1;

            int token = conversation.Generator.GetNextTokens()[0];
            tokens.Add(token);
            if (model.EndTokens.Contains(token)) return (StopReason.EndOfTurn, text.ToString(), tokens, prompt);

            string piece = stream.Decode(token);
            if (piece.Length == 0) continue;
            text.Append(piece);
            await reply(new TextDelta(request.RequestId, piece));
        }
    }

    private static void EnsureRoom(ChatModel model, int tokens)
    {
        if (tokens >= model.Spec.ContextTokens)
            throw new HostRequestException(HostErrorKind.Invalid,
                $"The conversation is {tokens} tokens, longer than {model.Spec.Id}'s context of {model.Spec.ContextTokens}.");
    }
}
