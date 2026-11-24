using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

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

    /// <summary>Every token it has read, in order, up to the first medium — whose positions are not tokens; what a prompt is matched
    /// against to cut it back (<see cref="CutPointFor"/>).</summary>
    private readonly List<int> _history = [];
    private bool _mediumRead;
    /// <summary>The token <see cref="Readying"/> gave, read once <see cref="Advanced"/> says so.</summary>
    private int _readying = -1;
    /// <summary>Its partial state as saved at each position, for a model that holds such state (<see cref="LlamaContext.HoldsPartialState"/>):
    /// where it may be cut back to. Changed only under its log's gate.</summary>
    private readonly SortedList<int, byte[]> _checkpoints = [];

    public LiveConversation(LlamaContext context, int sequence, ClientIdentity client, Action<LiveConversation> release)
    {
        _context = context;
        _release = release;
        Sequence = sequence;
        Client = client;
    }

    /// <summary>The client whose request began it: only that client's requests continue it or cut it back, and it is kept as that
    /// client's.</summary>
    public ClientIdentity Client { get; }

    /// <summary>Its client's name for it, which it is kept under for the next turn; null for a one-off, or one its client has forgotten,
    /// which is let go once its reply ends. Set under the log's gate.</summary>
    public string? Name { get; set; }

    public int Sequence { get; private set; }

    /// <summary>What it holds is now held in sequence <paramref name="to"/>.</summary>
    public void Moved(int to)
    {
        _context.Move(Sequence, to);
        Sequence = to;
    }

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
        _history.AddRange(opening.Tokens);
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

    /// <summary>What <see cref="Reading"/> gave, <paramref name="tokens"/>, has been read, and <paramref name="next"/> chosen after it
    /// — -1 where it was not asked for.</summary>
    public void ReadIn(ReadOnlySpan<int> tokens, int next)
    {
        Tokens += tokens.Length;
        Positions += tokens.Length;
        if (!_mediumRead) _history.AddRange(tokens);
        if (next >= 0) Next = next;
    }

    /// <summary>Reads a medium through the model's projector.</summary>
    public void ReadMedium(MediaProjector projector, PromptPiece piece, bool sample)
    {
        _mediumRead = true;
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
        _readying = token;
        return new SequenceRead(Sequence, Positions, new[] { token }, Sample: true, Sampler);
    }

    /// <summary>The token <see cref="Readying"/> gave has been read, and <paramref name="next"/> chosen after it.</summary>
    public void Advanced(int next)
    {
        Tokens++;
        Positions++;
        _written++;
        if (!_mediumRead) _history.Add(_readying);
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

    /// <summary>Whether it can be cut back: it has read text alone, every token of it as read, and forgets nothing it writes.</summary>
    public bool CutsBack => !_mediumRead && _window is null && _history.Count == Positions;

    /// <summary>Where it may be cut back to for <paramref name="prompt"/> to be read from there; 0 where it may not, or not usefully —
    /// see <see cref="ConversationMatch.CutPoint"/>. Called under its log's gate.</summary>
    public int CutPointFor(int[] prompt)
        => CutsBack ? ConversationMatch.CutPoint(_history, prompt, _context.HoldsPartialState ? _checkpoints.Keys : null,
                                                 Opening?.Tokens.Length ?? 0, ConversationLog.ShortestOpening)
                    : 0;

    /// <summary>The positions its partial state is saved at.</summary>
    public int Checkpoints => _checkpoints.Count;

    /// <summary>Keeps <paramref name="state"/> — its partial state as <see cref="LlamaContext.SavePartial"/> saved it, at its position
    /// now. Called under its log's gate.</summary>
    public void Checkpoint(byte[] state) => _checkpoints[Positions] = state;

    /// <summary>Lets go of the partial state saved earliest. Called under its log's gate.</summary>
    public void DropEarliestCheckpoint() => _checkpoints.RemoveAt(0);

    /// <summary>
    /// Cuts it back to <paramref name="point"/> — a position <see cref="CutPointFor"/> gave — its cells after that forgotten and, for
    /// a model holding partial state, that state given again as saved there; what it was asked and answered is forgotten with them,
    /// and only a read from <paramref name="point"/> on continues it. False where llama.cpp would not, and it is to be let go. Called
    /// under its log's gate.
    /// </summary>
    public bool CutBack(int point)
    {
        byte[]? partial = null;
        if (_context.HoldsPartialState && !_checkpoints.TryGetValue(point, out partial)) return false;
        if (!_context.CutBack(Sequence, point, partial)) return false;
        Tokens -= Positions - point;
        Positions = point;
        _history.RemoveRange(point, _history.Count - point);
        while (_checkpoints.Count > 0 && _checkpoints.Keys[^1] > point) _checkpoints.RemoveAt(_checkpoints.Count - 1);
        Read = [];
        Prompt = "";
        StoppedAt = "";
        return true;
    }

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
        _checkpoints.Clear();
        _context.Forget(Sequence);
        _release(this);
    }
}
