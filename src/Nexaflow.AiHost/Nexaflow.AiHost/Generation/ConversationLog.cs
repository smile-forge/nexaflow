using Nexaflow.AiHost.Catalog;
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
/// <para>
/// Each conversation, opening and prompt read is the client's whose request made it (<see cref="ClientIdentity"/>), and a request
/// continues, cuts back or opens with its own client's alone: no client reads from what another left, however alike their turns. The
/// capacities are the log's, every client's together. A lone client may fill them; beside others each is owed an equal part, and room
/// is made first from whoever holds the most beyond its part (<see cref="ClientShares{T}"/>), so a busy client lets its own go before
/// another's.
/// </para>
/// </summary>
internal sealed class ConversationLog : IDisposable
{
    /// <summary>Conversations kept between requests.</summary>
    internal const int Capacity = 4;

    /// <summary>Openings held at once: the sequences the lane holds for them beside its replies'.</summary>
    internal const int Openings = ModelSpec.OpeningSequences;

    /// <summary>The shortest start two prompts share that is worth holding as an opening.</summary>
    internal const int ShortestOpening = 256;

    /// <summary>How many prompts read lately an opening is looked for in.</summary>
    private const int Remembered = 8;

    /// <summary>Sequences the cache has — the lane's: one for each reply written at once and one for each opening, those not in use
    /// kept for a conversation or held as an opening.</summary>
    private int Sequences => _lane.Sequences;

    private readonly ChatLane _lane;
    private readonly string _id;
    private readonly ClientShares<LiveConversation> _kept = new(Capacity);
    private readonly HashSet<LiveConversation> _live = [];
    private readonly ClientShares<PromptOpening> _openings = new(Openings);
    /// <summary>The prompts read lately, each its client's: where an opening is looked for.</summary>
    private readonly ClientShares<int[]> _recent = new(Remembered);
    /// <summary>The sequences nobody holds, lowest first: where each conversation has a slice of its own, sequences in use read in
    /// one step only where their numbers follow one another, so the lowest free is always the one taken.</summary>
    private readonly SortedSet<int> _free;

    private readonly SemaphoreSlim _answering;

    /// <summary>Completes when cells are let go, for whoever waits for room.</summary>
    private TaskCompletionSource _freed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    public ConversationLog(ChatLane lane, string id)
    {
        _lane = lane;
        _id = id;
        _free = [.. Enumerable.Range(0, Sequences)];
        _answering = new(lane.Replies, lane.Replies);
    }

    /// <summary>Whether the client the request serves keeps a conversation named <paramref name="name"/> that <paramref name="turns"/>
    /// continue; never for a turn with no name, which continues nothing.</summary>
    public bool Continues(IReadOnlyList<ChatTurn> turns, string? name)
    {
        if (name is null) return false;
        var client = ClientIdentity.Current;
        lock (_gate) return _kept.Of(client).Any(conversation => conversation.Name == name && conversation.IsContinuedBy(turns));
    }

    /// <summary>Takes the conversation the client the request serves keeps under <paramref name="name"/>, for the request to continue or
    /// cut back; null where it keeps none so named.</summary>
    public LiveConversation? TakeNamed(string name)
    {
        var client = ClientIdentity.Current;
        lock (_gate) return _kept.Take(client, conversation => conversation.Name == name);
    }

    /// <summary>
    /// Cuts <paramref name="conversation"/> — taken for a request that does not continue it as its turns now render: an edited last turn,
    /// reasoning the template leaves out of what was answered — back to where it and <paramref name="prompt"/> part, where that start is long
    /// enough to be worth more than reading it again (<see cref="ShortestOpening"/>) and it can be cut there: anywhere, for a model whose
    /// sequences are their cells; where a turn ended and its partial state was saved (<see cref="Checkpoint"/>), for one holding more.
    /// False where it cannot be, and the request reads its prompt afresh.
    /// </summary>
    public bool CutBack(LiveConversation conversation, int[] prompt)
    {
        if (_lane.PerConversation) return false;
        lock (_gate) return conversation.CutPointFor(prompt) is > 0 and var point && conversation.CutBack(point);
    }

    /// <summary>
    /// Saves <paramref name="conversation"/>'s partial state where it stands — at the end of a turn it has read — so it can be cut back
    /// to here, where its lane keeps such saves (<see cref="ChatLane.Checkpoints"/>). Past as many as the lane keeps, the earliest of the
    /// kept conversations' go first, theirs in the order the kept conversations would be let go for this one's room
    /// (<see cref="ClientShares{T}.InEvictionOrder"/>), then those of the conversations being answered, this one's last.
    /// </summary>
    public void Checkpoint(LiveConversation conversation)
    {
        if (_lane.Checkpoints == 0 || !conversation.CutsBack) return;
        var state = _lane.Context.SavePartial(conversation.Sequence);
        lock (_gate)
        {
            conversation.Checkpoint(state);
            int saved = _live.Sum(c => c.Checkpoints);
            if (saved <= _lane.Checkpoints) return;
            var kept = _kept.InEvictionOrder(conversation.Client);
            foreach (var holder in kept.Concat(_live.Except(kept).Where(c => c != conversation)).Append(conversation))
                while (saved > _lane.Checkpoints && holder.Checkpoints > 0)
                {
                    holder.DropEarliestCheckpoint();
                    saved--;
                }
        }
    }

    /// <summary>Keeps <paramref name="conversation"/> for its client's next turn under its name, in place of what was kept under that name
    /// before; one that has no name — a one-off, or one its client has forgotten — is let go.</summary>
    public void Keep(LiveConversation conversation)
    {
        // Where each conversation has a slice of its own, the work it does is never continued, and one kept would stand among
        // those being written.
        if (_lane.PerConversation || conversation.Name is not { } name)
        {
            conversation.Dispose();
            return;
        }
        lock (_gate)
        {
            _kept.Take(conversation.Client, kept => kept.Name == name)?.Dispose();
            _kept.Add(conversation.Client, conversation);
            while (_kept.Count > Capacity && _kept.Evict(adding: null, beyondShare: true) is { } dropped) dropped.Dispose();
            Freed();   // kept, it is room that may be taken
        }
    }

    /// <summary>Lets go of what the client the request serves keeps under <paramref name="name"/>; a turn of it being answered now is let
    /// go when it ends, not kept.</summary>
    public void Forget(string name)
    {
        var client = ClientIdentity.Current;
        lock (_gate)
        {
            _kept.Take(client, kept => kept.Name == name)?.Dispose();
            foreach (var live in _live.Where(c => c.Client == client && c.Name == name)) live.Name = null;
        }
    }

    /// <summary>A new conversation of the client the request serves, in a sequence of its own — a kept conversation or an opening nobody
    /// holds let go (<see cref="Releasable"/>) when every sequence is taken. Where each conversation has a slice of its own it takes the
    /// highest free, out of the way of the replies being written, which are gathered into the lowest (<see cref="Gather"/>).</summary>
    public LiveConversation Start(string? name)
    {
        var client = ClientIdentity.Current;
        lock (_gate)
        {
            while (_free.Count == 0 && Releasable(client)) { }
            int sequence = _lane.PerConversation ? _free.Max : _free.Min;
            _free.Remove(sequence);
            var conversation = new LiveConversation(_lane.Context, sequence, client, Release) { Name = name };
            _live.Add(conversation);
            return conversation;
        }
    }

    /// <summary>
    /// Gathers the replies being written — <paramref name="writing"/>, a round's — into the lowest sequences, each that stands above
    /// a free one moved down into it. Where each conversation has a slice of its own, llama.cpp reads in one step only sequences
    /// whose numbers follow one another: a step whose replies stood apart, as they come to as some end and others begin, would be
    /// read in as many as there are gaps, the model's weights read again for each. Called in the round's turn on the GPU, so
    /// nothing reads them as they move.
    /// </summary>
    public void Gather(IEnumerable<LiveConversation> writing)
    {
        lock (_gate)
            foreach (var conversation in writing.OrderByDescending(c => c.Sequence))
            {
                if (_free.Count == 0 || _free.Min > conversation.Sequence) break;
                int from = conversation.Sequence;
                conversation.Moved(Lowest());
                _free.Add(from);
            }
    }

    /// <summary>The lowest sequence nobody holds, taken. Called under the gate.</summary>
    private int Lowest()
    {
        int lowest = _free.Min;
        _free.Remove(lowest);
        return lowest;
    }

    /// <summary>
    /// A turn to answer: there are as many as the model writes replies at once (<see cref="ChatLane.Replies"/>), so a
    /// request past them waits for one to finish rather than being turned away. The turn is given back when the returned value
    /// is disposed.
    /// </summary>
    public async Task<IDisposable> AnsweringAsync(CancellationToken ct)
    {
        await RequestActivity.WaitAsync(_answering.WaitAsync(ct));
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
    /// The opening of the client the request serves that <paramref name="tokens"/> begin with — held already, or one to hold: the longest
    /// start they share with a prompt that client read lately, where that is long enough to be worth holding. Null where there is neither,
    /// or no opening may be let go for it: one held, or another client's within its share. A new opening is read by whoever claims it
    /// (<see cref="PromptOpening.Claim"/>); every other request waits for it.
    /// </summary>
    public PromptOpening? OpeningFor(int[] tokens)
    {
        // Where each conversation has a slice of its own, none can share another's cells.
        if (_lane.PerConversation) return null;
        var client = ClientIdentity.Current;
        lock (_gate)
        {
            try
            {
                if (_openings.Use(client, o => o.Opens(tokens)) is { } held) return held;

                // Never the whole prompt: its last token is read for the reply to start from.
                int shared = Math.Min(tokens.Length - 1, _recent.Of(client).Max(recent => (int?)Common(recent, tokens)) ?? 0);
                if (shared < ShortestOpening) return null;
                while (_openings.Count >= Openings && _openings.Evict(client, o => !o.Held, beyondShare: true) is { } idle)
                    idle.Holder.Dispose();
                if (_openings.Count >= Openings) return null;

                // A free sequence, and one no reply could go short of: every turn to answer not yet taken still finds one, free or given up for it
                // by a kept conversation or an opening nobody holds. A kept conversation is never let go to make an opening.
                if (!OpeningFits(_free.Count, _kept.Count + _openings.All.Count(o => !o.Held), _answering.CurrentCount)) return null;
                var opening = new PromptOpening(new LiveConversation(_lane.Context, Lowest(), client, FreeOpening), tokens[..shared]);
                _openings.Add(client, opening);
                return opening;
            }
            finally
            {
                _recent.Add(client, tokens);
                while (_recent.Count > Remembered && _recent.Evict(adding: null, beyondShare: true) is not null) { }
            }
        }
    }

    /// <summary>Whether an opening may take one of <paramref name="free"/> sequences: there is one, and once it is taken each of the
    /// <paramref name="unanswered"/> turns to answer not yet taken still finds a sequence — a free one, or one of the
    /// <paramref name="reclaimable"/> a request makes room from (<see cref="Releasable"/>).</summary>
    internal static bool OpeningFits(int free, int reclaimable, int unanswered) => free > 0 && free - 1 + reclaimable >= unanswered;

    /// <summary>Has <paramref name="conversation"/>, read nothing yet, begin with <paramref name="opening"/>'s cells — false where
    /// the opening has been let go for room since it was read, or is another client's, and the conversation must read its whole beginning
    /// itself.</summary>
    public bool Open(LiveConversation conversation, PromptOpening opening)
    {
        lock (_gate)
        {
            if (!opening.Client.Equals(conversation.Client) || !_openings.Contains(opening.Client, opening)) return false;
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
    /// go, then the openings nobody holds (<see cref="Releasable"/>), until it fits — and holds that room for them until they are read, so
    /// prompts arriving together are never let in to the same room. Where the conversations being answered hold it, this waits
    /// for them to let it go: a request that fits the model is answered once there is room, rather than turned away. Where each
    /// conversation has a slice of its own, its slice is its room.
    /// </summary>
    /// <exception cref="HostRequestException">The conversation would outgrow what it may be given.</exception>
    public async Task MakeRoomAsync(LiveConversation conversation, int tokens, CancellationToken ct)
    {
        int longest = _lane.Longest, context = _lane.Context.Tokens;
        if (conversation.Length + tokens >= longest)
            throw new HostRequestException(HostErrorKind.Invalid,
                $"The conversation is {conversation.Length + tokens} tokens, longer than {_id}'s context of {longest}.");
        while (true)
        {
            Task freed;
            lock (_gate)
            {
                while (!_lane.PerConversation && Held + tokens > context && Releasable(conversation.Client)) { }
                if (_lane.PerConversation || Held + tokens <= context)
                {
                    conversation.Reserve(tokens);
                    return;
                }
                freed = _freed.Task;
            }
            await RequestActivity.WaitAsync(freed.WaitAsync(ct));
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
    /// conversations go, then the openings nobody holds (<see cref="Releasable"/>), until there is. False where the conversations being
    /// answered hold it all — a reply that reaches it has filled what it can read.
    /// </summary>
    public bool MakeRoomForNext()
    {
        if (_lane.PerConversation) return true;   // its slice is its own: how long it may grow is the lane's to say
        int context = _lane.Context.Tokens;
        var client = ClientIdentity.Current;
        lock (_gate)
        {
            int answered = _live.Count - _kept.Count;
            while (Held + answered > context && Releasable(client)) { }
            return Held + answered <= context;
        }
    }

    /// <summary>Lets go of what may be let go for the room <paramref name="asking"/>'s request is to take — a kept conversation, else an
    /// opening nobody holds, each the least recently used of the client holding the most of them, <paramref name="asking"/> counted with
    /// one more (<see cref="ClientShares{T}.Evict"/>) — and says whether there was any. Called under the gate.</summary>
    private bool Releasable(ClientIdentity asking)
    {
        if (_kept.Evict(asking) is { } kept)
        {
            kept.Dispose();
            return true;
        }
        if (_openings.Evict(asking, opening => !opening.Held) is not { } idle) return false;
        idle.Holder.Dispose();
        return true;
    }

    /// <summary>The cells held — by every conversation with a sequence, kept or being answered, with the room each being answered
    /// holds for what it is about to read; and by the openings — a cell several share counted once.</summary>
    private int Held => _live.Sum(c => c.Holding) + _openings.All.Sum(o => o.Holder.Holding);

    private void DropOpening(PromptOpening opening)
    {
        if (_openings.Remove(opening.Client, opening)) opening.Holder.Dispose();
    }

    private void Release(LiveConversation conversation)
    {
        lock (_gate)
        {
            conversation.Opening?.Let();
            if (_live.Remove(conversation)) _free.Add(conversation.Sequence);
            Freed();
        }
    }

    private void FreeOpening(LiveConversation holder)
    {
        lock (_gate)
        {
            _free.Add(holder.Sequence);
            Freed();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var conversation in _kept.All) conversation.Dispose();
            _kept.Clear();
            foreach (var opening in _openings.All) opening.Holder.Dispose();
            _openings.Clear();
        }
    }
}
