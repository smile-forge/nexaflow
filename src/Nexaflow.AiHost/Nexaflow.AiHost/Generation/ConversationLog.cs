using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The conversations a model keeps open, each a sequence of the model's one KV cache whose context only grows. A request
/// continues one when the turns it has read — its last reply included — begin the request's turns: only the rest is
/// read, in tens of milliseconds where re-reading the conversation costs hundreds. Anything else starts a conversation
/// afresh. Requests carry whole conversations, so nothing in the app has to know this cache exists. A few are kept,
/// least recently used dropped first — and dropped sooner when a new read needs their room in the cache.
/// </summary>
internal sealed class ConversationLog : IDisposable
{
    /// <summary>Conversations kept between requests.</summary>
    internal const int Capacity = 4;

    /// <summary>Sequences the cache has: those kept, and those being answered.</summary>
    internal const int Sequences = 8;

    private readonly ChatModel _model;
    private readonly LinkedList<LiveConversation> _kept = new();
    private readonly HashSet<LiveConversation> _live = [];
    private readonly Stack<int> _free = new(Enumerable.Range(0, Sequences).Reverse());
    private readonly object _gate = new();

    public ConversationLog(ChatModel model) => _model = model;

    /// <summary>Takes the conversation <paramref name="turns"/> continues, out of the log while it is used.</summary>
    public LiveConversation? TakeContinued(IReadOnlyList<ChatTurn> turns)
    {
        lock (_gate)
        {
            for (var node = _kept.First; node is not null; node = node.Next)
            {
                if (!node.Value.IsContinuedBy(turns)) continue;
                _kept.Remove(node);
                return node.Value;
            }
            return null;
        }
    }

    /// <summary>Keeps a conversation for the next request, as the most recently used.</summary>
    public void Keep(LiveConversation conversation)
    {
        lock (_gate)
        {
            _kept.AddFirst(conversation);
            while (_kept.Count > Capacity) DropOldest();
        }
    }

    /// <summary>A new conversation in a sequence of its own — the oldest kept one dropped when every sequence is taken.</summary>
    /// <exception cref="HostRequestException">Every sequence is being answered.</exception>
    public LiveConversation Start()
    {
        lock (_gate)
        {
            if (_free.Count == 0 && _kept.Count > 0) DropOldest();
            if (_free.Count == 0)
                throw new HostRequestException(HostErrorKind.Refused, $"{_model.Spec.Id} is answering {Sequences} conversations already.");
            var conversation = new LiveConversation(_model.Context, _free.Pop(), Release);
            _live.Add(conversation);
            return conversation;
        }
    }

    /// <summary>Makes room in the cache for <paramref name="tokens"/> more of <paramref name="conversation"/>'s: the
    /// kept conversations go, oldest first, until it fits.</summary>
    /// <exception cref="HostRequestException">The conversation would outgrow the model's context, or the conversations
    /// being answered hold the room.</exception>
    public void MakeRoom(LiveConversation conversation, int tokens)
    {
        int context = _model.Context.Tokens;
        if (conversation.Tokens + tokens >= context)
            throw new HostRequestException(HostErrorKind.Invalid,
                $"The conversation is {conversation.Tokens + tokens} tokens, longer than {_model.Spec.Id}'s context of {context}.");
        lock (_gate)
        {
            while (_live.Sum(c => c.Tokens) + tokens > context && _kept.Count > 0) DropOldest();
            if (_live.Sum(c => c.Tokens) + tokens > context)
                throw new HostRequestException(HostErrorKind.Refused, $"{_model.Spec.Id}'s context is held by the conversations it is answering.");
        }
    }

    private void DropOldest()
    {
        var oldest = _kept.Last!.Value;
        _kept.RemoveLast();
        oldest.Dispose();
    }

    private void Release(LiveConversation conversation)
    {
        lock (_gate)
            if (_live.Remove(conversation)) _free.Push(conversation.Sequence);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var conversation in _kept) conversation.Dispose();
            _kept.Clear();
        }
    }
}

/// <summary>
/// One conversation's context: the sequence holding it, what it has read, and where it stopped. The cache holds the
/// prompt it was last given and the reply generated after it, up to the token the reply stopped on — which it has not
/// read.
/// </summary>
internal sealed class LiveConversation : IDisposable
{
    private readonly LlamaContext _context;
    private readonly Action<LiveConversation> _release;
    private bool _disposed;

    public LiveConversation(LlamaContext context, int sequence, Action<LiveConversation> release)
    {
        _context = context;
        _release = release;
        Sequence = sequence;
    }

    public int Sequence { get; }

    /// <summary>The cache cells it holds.</summary>
    public int Tokens { get; private set; }

    /// <summary>The position its next token is read at.</summary>
    public int Positions { get; private set; }

    /// <summary>The token chosen to come next, from the logits the last read left; not yet read.</summary>
    public int Next { get; private set; }

    /// <summary>The turns read, ending with the reply generated for them.</summary>
    public IReadOnlyList<ChatTurn> Read { get; private set; } = [];

    /// <summary>The whole prompt the conversation was last given, as text — what the next request's must begin with.</summary>
    public string Prompt { get; private set; } = "";

    /// <summary>The text of the token the last reply stopped on, which the next read begins with.</summary>
    public string StoppedAt { get; private set; } = "";

    public bool IsContinuedBy(IReadOnlyList<ChatTurn> turns) => ConversationMatch.Continues(Read, turns);

    /// <summary>Reads text tokens — at most <see cref="LlamaContext.Batch"/> — choosing the next token after the
    /// last when <paramref name="sample"/>.</summary>
    public void ReadTokens(ReadOnlySpan<int> tokens, bool sample)
    {
        var next = _context.Read(Sequence, Positions, tokens, sample);
        Tokens += tokens.Length;
        Positions += tokens.Length;
        if (next is { } chosen) Next = chosen;
    }

    /// <summary>Reads a medium through the model's projector.</summary>
    public void ReadMedium(MediaProjector projector, PromptPiece piece, bool sample)
    {
        Positions = projector.Read(_context, piece, Sequence, Positions, sample, out var next);
        Tokens += piece.TokenCount;
        if (next is { } chosen) Next = chosen;
    }

    /// <summary>Reads the token generated last and chooses the one after it.</summary>
    public void Advance(int token) => ReadTokens([token], sample: true);

    /// <summary>The prompt now read in full, before its reply is generated.</summary>
    public void Prompted(string prompt) => Prompt = prompt;

    public void Answered(IReadOnlyList<ChatTurn> turns, string reply, IReadOnlyList<ChatToolCall> calls, string stoppedAt)
    {
        Read = [.. turns, new ChatTurn(TurnRole.Assistant, reply) { ToolCalls = calls.Count > 0 ? calls : null }];
        StoppedAt = stoppedAt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.Forget(Sequence);
        _release(this);
    }
}
