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
/// a step of its own — each token read in one round with every other reply the model is writing
/// (<see cref="TokenRounds"/>) — so speech gets in between.
/// </summary>
internal sealed class GenerateHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<Generate>
{
    /// <summary>The progress stage a reply in its reasoning reports, with how many tokens it has thought so far.</summary>
    public const string Thinking = "thinking";

    /// <summary>Reasoning reports itself every this many tokens — a few times a second.</summary>
    private const int ThinkingReport = 64;

    protected override async Task HandleAsync(Generate request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        using var lease = await models.AcquireAsync(request.Model, resident: false,
            stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        var model = lease.As<ChatModel>();
        using var answering = await model.Conversations.AnsweringAsync(ct);
        using var attended = gpu.Engage(request.Priority, model.Spec.Device);   // a young job on its card waits for this reply

        LiveConversation? conversation = model.Conversations.TakeContinued(request.Turns);
        bool kept = false;
        using var sampler = new LlamaSampler(request.Sampling, model.Context.Tokens, request.Decoding?.RepeatBan);   // as the model's makers recommend
        try
        {
            string prompt = ChatPrompt.Render(model, request.Turns, generationPrompt: true, request.Tools, request.Thinks);
            int read;
            if (conversation is not null && Delta(model, conversation, request.Turns, request.Tools, request.Thinks) is { } delta)
            {
                conversation.Sampler = sampler;
                read = await ReadAsync(model, conversation, delta, ChatPrompt.Media(model, request.Turns.Skip(conversation.Read.Count)),
                                       request.Priority, ct);
            }
            else
            {
                conversation?.Dispose();
                conversation = model.Conversations.Start();
                conversation.Sampler = sampler;
                read = await ReadAsync(model, conversation, prompt, ChatPrompt.Media(model, request.Turns), request.Priority, ct);
            }
            conversation.Prompted(prompt);
            if (request.Decoding?.WrittenWindow is { } window) conversation.KeepWritten(window);

            var outcome = await GenerateAsync(model, conversation, request, model.Thinking?.Opens(prompt) == true, reply, ct);
            if (outcome.Continuable && !conversation.Forgets)
            {
                conversation.Answered(request.Turns, outcome.Text, outcome.Calls, outcome.StoppedAt);
                model.Conversations.Keep(conversation);
                kept = true;
            }
            await reply(new GenerationEnded(request.RequestId, outcome.Reason, outcome.PromptTokens, read, outcome.Generated));
        }
        finally
        {
            if (conversation is not null) conversation.Sampler = null;   // the sampler goes with the request
            // Failed, cancelled or cut short part-way: its context is not one anybody will continue.
            if (!kept) conversation?.Dispose();
        }
    }

    /// <summary>What extends the conversation's context to <paramref name="turns"/> — see <see cref="ConversationMatch.Delta"/>.</summary>
    private static string? Delta(ChatModel model, LiveConversation conversation, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ChatTool>? tools, bool thinks)
        => ConversationMatch.Delta(conversation.Read, conversation.Prompt, conversation.StoppedAt, turns,
                                   (t, generationPrompt) => ChatPrompt.Render(model, t, generationPrompt, tools, thinks));

    /// <summary>Reads <paramref name="text"/> into the conversation — its text in slices, read with whatever else is being
    /// read (<see cref="PromptReads"/>), each medium whole in a step of its own — and chooses the first token of the reply with
    /// the last. A conversation started afresh with a prompt of text alone begins with the opening it shares with prompts read
    /// lately, where there is one (<see cref="ConversationLog.OpeningFor"/>), and reads only what follows it. Returns the tokens
    /// read.</summary>
    private async Task<int> ReadAsync(ChatModel model, LiveConversation conversation, string text, IReadOnlyList<ChatMedia> media,
                                      RequestPriority priority, CancellationToken ct)
    {
        using var prompt = model.Prepare(text, [.. media.Select(Bytes)]);
        var pieces = prompt.Pieces;

        int opened = 0;
        if (conversation.Length == 0 && pieces is [{ IsText: true, Tokens: { } all }] && model.Conversations.OpeningFor(all) is { } opening
            && await OpenedAsync(model, opening, priority, ct) && model.Conversations.Open(conversation, opening))
            opened = opening.Tokens.Length;
        await model.Conversations.MakeRoomAsync(conversation, prompt.Tokens - opened, ct);

        for (int p = 0; p < pieces.Count; p++)
        {
            var piece = pieces[p];
            bool lastPiece = p == pieces.Count - 1;
            if (!piece.IsText)
            {
                using (await gpu.NextAsync(priority, model.Spec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    conversation.ReadMedium(prompt.Projector!, piece, sample: lastPiece);
                }
                continue;
            }
            var tokens = piece.Tokens!;
            await ReadTextAsync(model, conversation, tokens.AsMemory(p == 0 ? opened : 0), lastPiece, priority, ct);
        }
        return prompt.Tokens - opened;
    }

    /// <summary>Reads <paramref name="tokens"/> into the conversation in slices, read with whatever else is being read, choosing
    /// the token after the last where <paramref name="sample"/>.</summary>
    private async Task ReadTextAsync(ChatModel model, LiveConversation conversation, ReadOnlyMemory<int> tokens, bool sample, RequestPriority priority,
                                     CancellationToken ct)
    {
        for (int at = 0; at < tokens.Length; at += model.Context.Batch)
        {
            int count = Math.Min(model.Context.Batch, tokens.Length - at);
            ct.ThrowIfCancellationRequested();
            await model.Prompts.ReadAsync(conversation, tokens.Slice(at, count), sample && at + count == tokens.Length,
                                          async () => await gpu.NextAsync(priority, model.Spec.Device, ct));
        }
    }

    /// <summary>Whether <paramref name="opening"/> is read and can be begun with: read here by the request that claims it, waited
    /// for by the rest. One that could not be read is let go, and its prompts read whole.</summary>
    private async Task<bool> OpenedAsync(ChatModel model, PromptOpening opening, RequestPriority priority, CancellationToken ct)
    {
        if (!opening.Claim()) return await opening.Ready.Task.WaitAsync(ct);
        try
        {
            await model.Conversations.MakeRoomAsync(opening.Holder, opening.Tokens.Length, ct);
            await ReadTextAsync(model, opening.Holder, opening.Tokens, sample: false, priority, ct);
            opening.Ready.TrySetResult(true);
            return true;
        }
        catch
        {
            model.Conversations.Forsake(opening);
            opening.Ready.TrySetResult(false);
            throw;
        }
        finally
        {
            opening.Let();
        }
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
        var text = new ReplyText(model.Model, request.Decoding?.WritesControlTokens == true);
        var reader = request.Tools is { Count: > 0 } tools && model.ToolCalls is { } syntax ? syntax.Read(text.Decode, tools) : null;
        var calls = new List<ChatToolCall>();
        var written = new StringBuilder();
        int prompt = conversation.Tokens, generated = 0, thought = 0;
        var circles = new CircleWatch();
        using var rounds = model.Rounds.Join(conversation, request.Priority, async () => await gpu.NextAsync(request.Priority, model.Spec.Device, CancellationToken.None));

        Outcome Stop(StopReason reason, bool continuable, int token)
            => new(reason, written.ToString(), calls, generated, prompt, continuable, model.Model.TokenText(token));

        while (true)
        {
            int token = conversation.Next;
            generated++;
            if (model.Model.EndsTurn(token))
                return Stop(calls.Count > 0 ? StopReason.ToolCalls : StopReason.EndOfTurn, continuable: true, token);
            if (circles.Take(token)) return Stop(StopReason.Circling, continuable: false, token);

            if (model.Thinking is { } span && (reasoning || token == span.Open))
            {
                reasoning = token != span.Close;
                // Reasoning is not shown, so it is told as it goes: the app hears the reply is alive, however long it thinks.
                if (++thought % ThinkingReport == 0) await reply(new Progress(request.RequestId, Thinking, thought, 0));
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

            if (request.MaxNewTokens is { } most && generated >= most) return Stop(StopReason.MaxTokens, continuable: false, token);
            // Full when its cells fill the cache, when its positions reach what the model was trained to read — which a
            // reply written in a window, holding few cells, can come to first — or when the conversations being answered
            // hold every cell the kept ones could give up.
            if (conversation.Length + 1 >= model.Context.Tokens || conversation.Positions + 1 >= model.Model.TrainedContext
                || !model.Conversations.MakeRoomForNext())
                return Stop(StopReason.ContextFull, continuable: false, token);
            await rounds.AdvanceAsync(token, ct);
            ct.ThrowIfCancellationRequested();
        }
    }
}
