using System.Text;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Audio;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The model's next turn, streamed as it is generated. The conversation is continued where the model already holds
/// it — what is new appended against what was read, media included — or read afresh; then tokens are generated one GPU
/// step at a time until the model ends its turn, the request's limit, or the context's. Every read and every token is
/// a step of its own, so speech gets in between.
/// </summary>
internal sealed class GenerateHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<Generate>
{
    protected override async Task HandleAsync(Generate request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        using var lease = await models.AcquireAsync(request.Model, resident: false,
            stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        var model = lease.As<ChatModel>();

        LiveConversation? conversation = model.Conversations.TakeContinued(request.Turns);
        bool kept = false;
        try
        {
            string prompt = ChatPrompt.Render(model, request.Turns, generationPrompt: true, request.Tools);
            int read;
            if (conversation is not null && Delta(model, conversation, request.Turns, request.Tools) is { } delta)
            {
                read = await ReadAsync(model, conversation, delta, ChatPrompt.Media(model, request.Turns.Skip(conversation.Read.Count)),
                                       request.Priority, ct);
            }
            else
            {
                conversation?.Dispose();
                conversation = model.Conversations.Start();
                read = await ReadAsync(model, conversation, prompt, ChatPrompt.Media(model, request.Turns), request.Priority, ct);
            }
            conversation.Prompted(prompt);

            var outcome = await GenerateAsync(model, conversation, request, model.Thinking?.Opens(prompt) == true, reply, ct);
            if (outcome.Continuable)
            {
                conversation.Answered(request.Turns, outcome.Text, outcome.Calls, outcome.StoppedAt);
                model.Conversations.Keep(conversation);
                kept = true;
            }
            await reply(new GenerationEnded(request.RequestId, outcome.Reason, outcome.PromptTokens, read, outcome.Generated));
        }
        finally
        {
            // Failed, cancelled or cut short part-way: its context is not one anybody will continue.
            if (!kept) conversation?.Dispose();
        }
    }

    /// <summary>What extends the conversation's context to <paramref name="turns"/> — see <see cref="ConversationMatch.Delta"/>.</summary>
    private static string? Delta(ChatModel model, LiveConversation conversation, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ChatTool>? tools)
        => ConversationMatch.Delta(conversation.Read, conversation.Prompt, conversation.StoppedAt, turns,
                                   (t, generationPrompt) => ChatPrompt.Render(model, t, generationPrompt, tools));

    /// <summary>Reads <paramref name="text"/> into the conversation — its text in slices, each medium whole, a step
    /// each — and chooses the first token of the reply with the last. Returns the tokens read.</summary>
    private async Task<int> ReadAsync(ChatModel model, LiveConversation conversation, string text, IReadOnlyList<ChatMedia> media,
                                      RequestPriority priority, CancellationToken ct)
    {
        using var prompt = model.Prepare(text, [.. media.Select(Bytes)]);
        model.Conversations.MakeRoom(conversation, prompt.Tokens);

        var pieces = prompt.Pieces;
        for (int p = 0; p < pieces.Count; p++)
        {
            var piece = pieces[p];
            bool lastPiece = p == pieces.Count - 1;
            if (!piece.IsText)
            {
                using (await gpu.NextAsync(priority, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    conversation.ReadMedium(prompt.Projector!, piece, sample: lastPiece);
                }
                continue;
            }
            var tokens = piece.Tokens!;
            for (int at = 0; at < tokens.Length; at += LlamaContext.Batch)
            {
                int count = Math.Min(LlamaContext.Batch, tokens.Length - at);
                using (await gpu.NextAsync(priority, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    conversation.ReadTokens(tokens.AsSpan(at, count), sample: lastPiece && at + count == tokens.Length);
                }
            }
        }
        return prompt.Tokens;
    }

    /// <summary>A medium's bytes as the projector takes them: an image as its file, a recording as 16 kHz mono WAV.</summary>
    private static byte[] Bytes(ChatMedia media)
    {
        byte[] bytes = media.Bytes is { Length: > 0 } inline ? inline
                     : media.Path is { } path && File.Exists(path) ? File.ReadAllBytes(path)
                     : throw new HostRequestException(HostErrorKind.NotFound, $"The {media.Kind.ToString().ToLowerInvariant()} to read is not there: {media.Path}.");
        if (media.Kind != MediaKind.Audio) return bytes;
        try { return WavAudio.ToMono(bytes); }
        catch (InvalidDataException ex) { throw new HostRequestException(HostErrorKind.Invalid, $"The recording cannot be read: {ex.Message}"); }
    }

    /// <param name="Continuable">Whether the conversation can be kept for the next request: its reply ended where the
    /// model ends turns, not cut short by a limit.</param>
    /// <param name="StoppedAt">The text of the token the reply stopped on.</param>
    private sealed record Outcome(StopReason Reason, string Text, List<ChatToolCall> Calls, int Generated, int PromptTokens, bool Continuable,
                                  string StoppedAt);

    /// <summary>Generates until the model ends its turn, waits on the tools it called, or a limit is reached. Each
    /// token is chosen from the logits of the read before it and read in the step after it is sent — all but the one
    /// a reply stops on, which the next request's read begins with. The model's reasoning is read and kept in its context,
    /// never sent; <paramref name="reasoning"/> says the prompt began it. The reply's text starts at its first word.
    /// Offered tools, a model that calls them in its own syntax has its calls read from its tokens and sent as they are
    /// made; they are never text.</summary>
    private async Task<Outcome> GenerateAsync(ChatModel model, LiveConversation conversation, Generate request, bool reasoning,
                                              Func<HostReply, Task> reply, CancellationToken ct)
    {
        var text = new ReplyText(model.Model);
        var reader = request.Tools is { Count: > 0 } tools && model.ToolCalls is { } syntax ? syntax.Read(text.Decode, tools) : null;
        var calls = new List<ChatToolCall>();
        var written = new StringBuilder();
        int prompt = conversation.Tokens, generated = 0;

        Outcome Stop(StopReason reason, bool continuable, int token)
            => new(reason, written.ToString(), calls, generated, prompt, continuable, model.Model.TokenText(token));

        while (true)
        {
            int token = conversation.Next;
            generated++;
            if (model.Model.EndsTurn(token))
                return Stop(calls.Count > 0 ? StopReason.ToolCalls : StopReason.EndOfTurn, continuable: true, token);

            if (model.Thinking is { } span && (reasoning || token == span.Open))
            {
                reasoning = token != span.Close;
            }
            else
            {
                switch (reader?.Take(token) ?? ToolCallToken.Text)
                {
                    case ToolCallToken.Made:
                        calls.Add(reader!.Made!);
                        await reply(new ToolCallMade(request.RequestId, reader.Made!));
                        break;
                    case ToolCallToken.Waits:
                        return Stop(StopReason.ToolCalls, continuable: true, token);
                    case ToolCallToken.Text:
                        string piece = text.Decode(token);
                        if (written.Length == 0) piece = piece.TrimStart();
                        if (piece.Length == 0) break;
                        written.Append(piece);
                        await reply(new TextDelta(request.RequestId, piece));
                        break;
                }
            }

            if (generated >= request.MaxNewTokens) return Stop(StopReason.MaxTokens, continuable: false, token);
            if (conversation.Tokens + 1 >= model.Context.Tokens) return Stop(StopReason.ContextFull, continuable: false, token);
            using (await gpu.NextAsync(request.Priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                conversation.Advance(token);
            }
        }
    }
}
