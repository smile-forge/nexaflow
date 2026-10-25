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
/// <para>
/// A conversation started afresh may still begin as one read lately began — the same instructions, a different question.
/// Where the start two prompts share is long enough, it is read once into a sequence of its own, an opening
/// (<see cref="PromptOpening"/>), and each conversation that begins with it takes its cells rather than reading them again. An
/// opening is let go, least recently used first, only once no conversation holds its cells.
/// </para>
/// </summary>
internal sealed class ConversationLog : IDisposable
{
    /// <summary>Conversations kept between requests.</summary>
    internal const int Capacity = 4;

    /// <summary>Openings held at once.</summary>
    internal const int Openings = 2;

    /// <summary>The shortest start two prompts share that is worth holding as an opening.</summary>
    internal const int ShortestOpening = 256;

    /// <summary>How many prompts read lately an opening is looked for in.</summary>
    private const int Remembered = 8;

    /// <summary>Sequences the cache has — the model's spec says how many: one for each reply written at once, and those not being
    /// answered kept for a conversation or held as an opening.</summary>
    private int Sequences => _model.Spec.Sequences;

    private readonly ChatModel _model;
    private readonly LinkedList<LiveConversation> _kept = new();
    private readonly HashSet<LiveConversation> _live = [];
    private readonly LinkedList<PromptOpening> _openings = new();
    private readonly LinkedList<int[]> _recent = new();
    private readonly Stack<int> _free;

    private readonly SemaphoreSlim _answering;

    /// <summary>Completes when cells are let go, for whoever waits for room.</summary>
    private TaskCompletionSource _freed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    public ConversationLog(ChatModel model)
    {
        _model = model;
        _free = new(Enumerable.Range(0, Sequences).Reverse());
        _answering = new(Sequences, Sequences);
    }

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
            Freed();   // kept, it is room that may be taken
        }
    }

    /// <summary>A new conversation in a sequence of its own — a kept conversation, oldest first, or an opening nobody holds let go
    /// when every sequence is taken.</summary>
    public LiveConversation Start()
    {
        lock (_gate)
        {
            while (_free.Count == 0 && Releasable()) { }
            var conversation = new LiveConversation(_model.Context, _free.Pop(), Release);
            _live.Add(conversation);
            return conversation;
        }
    }

    /// <summary>
    /// A turn to answer: there are as many as the model writes replies at once (<see cref="ModelSpec.Sequences"/>), so a
    /// request past them waits for one to finish rather than being turned away. The turn is given back when the returned value
    /// is disposed.
    /// </summary>
    public async Task<IDisposable> AnsweringAsync(CancellationToken ct)
    {
        await _answering.WaitAsync(ct);
        return new Answering(_answering);
    }

    private sealed class Answering(SemaphoreSlim turns) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 0) turns.Release();
        }
    }

    /// <summary>
    /// The opening <paramref name="tokens"/> begin with — held already, or one to hold: the longest start they share with a
    /// prompt read lately, where that is long enough to be worth holding. Null where there is neither. A new opening is read by
    /// whoever claims it (<see cref="PromptOpening.Claim"/>); every other request waits for it.
    /// </summary>
    public PromptOpening? OpeningFor(int[] tokens)
    {
        lock (_gate)
        {
            try
            {
                for (var node = _openings.First; node is not null; node = node.Next)
                {
                    if (!node.Value.Opens(tokens)) continue;
                    _openings.Remove(node);
                    _openings.AddFirst(node);
                    return node.Value;
                }

                // Never the whole prompt: its last token is read for the reply to start from.
                int shared = Math.Min(tokens.Length - 1, _recent.Max(recent => (int?)Common(recent, tokens)) ?? 0);
                if (shared < ShortestOpening) return null;
                while (_openings.Count >= Openings && _openings.LastOrDefault(o => !o.Held) is { } idle) DropOpening(idle);
                if (_openings.Count >= Openings) return null;

                // Only a sequence no reply could need: one left over for each turn to answer not yet taken.
                while (_free.Count <= _answering.CurrentCount && _kept.Count > 0) DropOldest();
                if (_free.Count <= _answering.CurrentCount) return null;
                var opening = new PromptOpening(new LiveConversation(_model.Context, _free.Pop(), FreeOpening), tokens[..shared]);
                _openings.AddFirst(opening);
                return opening;
            }
            finally
            {
                _recent.AddFirst(tokens);
                while (_recent.Count > Remembered) _recent.RemoveLast();
            }
        }
    }

    /// <summary>Has <paramref name="conversation"/>, read nothing yet, begin with <paramref name="opening"/>'s cells — false where
    /// the opening has been let go for room since it was read, and the conversation must read its whole beginning itself.</summary>
    public bool Open(LiveConversation conversation, PromptOpening opening)
    {
        lock (_gate)
        {
            if (!_openings.Contains(opening)) return false;
            conversation.Borrow(opening);
            return true;
        }
    }

    /// <summary>Lets go of an opening that could not be read.</summary>
    public void Forsake(PromptOpening opening)
    {
        lock (_gate) DropOpening(opening);
    }

    /// <summary>How many tokens two prompts begin with alike.</summary>
    private static int Common(int[] one, int[] other)
    {
        int length = Math.Min(one.Length, other.Length), at = 0;
        while (at < length && one[at] == other[at]) at++;
        return at;
    }

    /// <summary>
    /// Makes room in the cache for <paramref name="tokens"/> more of <paramref name="conversation"/>'s: the kept conversations
    /// go, oldest first, then the openings nobody holds, until it fits — and holds that room for them until they are read, so
    /// prompts arriving together are never let in to the same room. Where the conversations being answered hold it, this waits
    /// for them to let it go: a request that fits the model is answered once there is room, rather than turned away.
    /// </summary>
    /// <exception cref="HostRequestException">The conversation would outgrow the model's context.</exception>
    public async Task MakeRoomAsync(LiveConversation conversation, int tokens, CancellationToken ct)
    {
        int context = _model.Context.Tokens;
        if (conversation.Length + tokens >= context)
            throw new HostRequestException(HostErrorKind.Invalid,
                $"The conversation is {conversation.Length + tokens} tokens, longer than {_model.Spec.Id}'s context of {context}.");
        while (true)
        {
            Task freed;
            lock (_gate)
            {
                while (Held + tokens > context && Releasable()) { }
                if (Held + tokens <= context)
                {
                    conversation.Reserve(tokens);
                    return;
                }
                freed = _freed.Task;
            }
            await freed.WaitAsync(ct);
        }
    }

    /// <summary>Cells have been let go, or may now be: whoever waits for room looks again. Called under the gate.</summary>
    private void Freed()
    {
        var waiting = _freed;
        _freed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.TrySetResult();
    }

    /// <summary>
    /// Makes room in the cache for the token each conversation being answered reads next, as replies are written: the kept
    /// conversations go, oldest first, then the openings nobody holds, until there is. False where the conversations being
    /// answered hold it all — a reply that reaches it has filled what it can read.
    /// </summary>
    public bool MakeRoomForNext()
    {
        int context = _model.Context.Tokens;
        lock (_gate)
        {
            int answered = _live.Count - _kept.Count;
            while (Held + answered > context && Releasable()) { }
            return Held + answered <= context;
        }
    }

    /// <summary>Lets go of what may be let go for room — the oldest kept conversation, else the least used opening nobody
    /// holds — and says whether there was any. Called under the gate.</summary>
    private bool Releasable()
    {
        if (_kept.Count > 0)
        {
            DropOldest();
            return true;
        }
        if (_openings.LastOrDefault(o => !o.Held) is not { } idle) return false;
        DropOpening(idle);
        return true;
    }

    /// <summary>The cells held — by every conversation with a sequence, kept or being answered, with the room each being answered
    /// holds for what it is about to read; and by the openings — a cell several share counted once.</summary>
    private int Held => _live.Sum(c => c.Holding) + _openings.Sum(o => o.Holder.Holding);

    private void DropOldest()
    {
        var oldest = _kept.Last!.Value;
        _kept.RemoveLast();
        oldest.Dispose();
    }

    private void DropOpening(PromptOpening opening)
    {
        if (_openings.Remove(opening)) opening.Holder.Dispose();
    }

    private void Release(LiveConversation conversation)
    {
        lock (_gate)
        {
            conversation.Opening?.Let();
            if (_live.Remove(conversation)) _free.Push(conversation.Sequence);
            Freed();
        }
    }

    private void FreeOpening(LiveConversation holder)
    {
        lock (_gate)
        {
            _free.Push(holder.Sequence);
            Freed();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var conversation in _kept) conversation.Dispose();
            _kept.Clear();
            foreach (var opening in _openings) opening.Holder.Dispose();
            _openings.Clear();
        }
    }
}

/// <summary>
/// The start several prompts share, read once into a sequence of its own — its holder — whose cells each conversation that
/// begins with it takes as its own beginning (<see cref="LiveConversation.Borrow"/>).
/// </summary>
internal sealed class PromptOpening(LiveConversation holder, int[] tokens)
{
    private int _claimed, _borrowers;

    /// <summary>The sequence it is read into, and held in.</summary>
    public LiveConversation Holder { get; } = holder;

    public int[] Tokens { get; } = tokens;

    /// <summary>Completes once it is read: true, or false where it could not be.</summary>
    public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether a conversation holds its cells — one being answered, or one kept.</summary>
    public bool Held => Volatile.Read(ref _borrowers) > 0;

    /// <summary>Whether <paramref name="prompt"/> begins with it and goes on past it.</summary>
    public bool Opens(int[] prompt) => prompt.Length > Tokens.Length && prompt.AsSpan(0, Tokens.Length).SequenceEqual(Tokens);

    /// <summary>True for the one request that is to read it, which holds it while it reads (<see cref="Let"/> once read); false
    /// for every other, which waits for <see cref="Ready"/>.</summary>
    public bool Claim()
    {
        if (Interlocked.Exchange(ref _claimed, 1) != 0) return false;
        Lend();
        return true;
    }

    /// <summary>A conversation has taken its cells, or a request is reading them.</summary>
    public void Lend() => Interlocked.Increment(ref _borrowers);

    /// <summary>A conversation that took its cells has let them go, or the request reading them has read them.</summary>
    public void Let() => Interlocked.Decrement(ref _borrowers);
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

    private int? _window;
    private int _promptEnd, _written;

    public LiveConversation(LlamaContext context, int sequence, Action<LiveConversation> release)
    {
        _context = context;
        _release = release;
        Sequence = sequence;
    }

    public int Sequence { get; }

    /// <summary>The cache cells it holds of its own.</summary>
    public int Tokens { get; private set; }

    /// <summary>The cells it holds of its own once what it has room for is read.</summary>
    private int _reserved;

    /// <summary>The cells it holds of its own, or has room held for: what room is weighed against.</summary>
    public int Holding => Math.Max(Tokens, _reserved);

    /// <summary>Holds room for <paramref name="tokens"/> more cells than it has, until they are read.</summary>
    public void Reserve(int tokens) => _reserved = Tokens + tokens;

    /// <summary>The opening it began with, whose cells it shares; null where it read its whole beginning itself.</summary>
    public PromptOpening? Opening { get; private set; }

    /// <summary>How long it is: the opening's cells it shares, and its own.</summary>
    public int Length => (Opening?.Tokens.Length ?? 0) + Tokens;

    /// <summary>Begins, read nothing yet, with <paramref name="opening"/>'s cells: from here on it reads after them.</summary>
    public void Borrow(PromptOpening opening)
    {
        _context.Share(opening.Holder.Sequence, Sequence, opening.Tokens.Length);
        opening.Lend();
        Opening = opening;
        Positions = opening.Tokens.Length;
    }

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

    /// <summary>How the request answering in it chooses its tokens; null — the likeliest each time — between requests.</summary>
    public LlamaSampler? Sampler { get; set; }

    public bool IsContinuedBy(IReadOnlyList<ChatTurn> turns) => ConversationMatch.Continues(Read, turns);

    /// <summary>Its next <paramref name="tokens"/> to read, at its next positions — at most a step's — choosing the token after
    /// the last when <paramref name="sample"/>.</summary>
    public SequenceRead Reading(ReadOnlyMemory<int> tokens, bool sample) => new(Sequence, Positions, tokens, sample, Sampler);

    /// <summary>What <see cref="Reading"/> gave, <paramref name="count"/> tokens, has been read, and <paramref name="next"/> chosen
    /// after it — -1 where it was not asked for.</summary>
    public void ReadIn(int count, int next)
    {
        Tokens += count;
        Positions += count;
        if (next >= 0) Next = next;
    }

    /// <summary>Reads a medium through the model's projector.</summary>
    public void ReadMedium(MediaProjector projector, PromptPiece piece, bool sample)
    {
        Positions = projector.Read(_context, piece, Sequence, Positions, sample, out var next, Sampler);
        Tokens += piece.TokenCount;
        if (next is { } chosen) Next = chosen;
    }

    /// <summary>The token generated last, ready to be read in a round (<see cref="TokenRounds"/>) — forgetting first, when
    /// it keeps only so much of what it writes, the written token that leaves the window as it is read.</summary>
    public SequenceRead Readying(int token)
    {
        if (_window is { } window && _written >= window)
        {
            int oldest = _promptEnd + _written - window;
            _context.Forget(Sequence, oldest, oldest + 1);
            Tokens--;
        }
        return new SequenceRead(Sequence, Positions, new[] { token }, Sample: true, Sampler);
    }

    /// <summary>The token <see cref="Readying"/> gave has been read, and <paramref name="next"/> chosen after it.</summary>
    public void Advanced(int next)
    {
        Tokens++;
        Positions++;
        _written++;
        Next = next;
    }

    /// <summary>From here on each token written sees all that was read before it but only the last
    /// <paramref name="tokens"/> written: the oldest is forgotten as each new one is read, its position kept, as a model
    /// trained with reference sliding-window attention reads.</summary>
    public void KeepWritten(int tokens)
    {
        _window = tokens;
        _promptEnd = Positions;
        _written = 0;
    }

    /// <summary>Whether it forgets what it writes, and so holds a context nobody can continue.</summary>
    public bool Forgets => _window is not null;

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
