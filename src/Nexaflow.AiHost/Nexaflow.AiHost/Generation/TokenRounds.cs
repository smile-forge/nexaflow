using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The replies one model is writing, advanced a token at a time together: each round reads the token every one of them
/// chose last into its own sequence in one llama.cpp call, and chooses each one's next from its own scores by its own
/// sampler — its repeat ban, its window of what it wrote. A small model reading one sequence leaves most of a GPU idle
/// between its kernels, so several pages read at once cost little more a token than one does.
/// <para>
/// A round starts once every reply in the rounds has chosen its token, so they keep step: one that takes longer between
/// tokens — sending its text — holds the others back by as long. A round is one GPU step, taken at the highest priority
/// among its replies, so a prompt being read or another model's work goes between rounds.
/// </para>
/// </summary>
internal sealed class TokenRounds(LlamaContext context)
{
    private readonly object _gate = new();
    private readonly List<Member> _members = [];
    private readonly List<Member> _posted = [];

    /// <summary>A reply about to be written in <paramref name="conversation"/>, joining the rounds; it leaves them when
    /// the returned member is disposed.</summary>
    /// <param name="turn">Waits for the GPU step a round is read in, at this reply's priority.</param>
    public Member Join(LiveConversation conversation, RequestPriority priority, Func<ValueTask<IDisposable>> turn)
    {
        var member = new Member(this, conversation, priority, turn);
        lock (_gate) _members.Add(member);
        return member;
    }

    private void Post(Member member)
    {
        List<Member>? round;
        lock (_gate)
        {
            _posted.Add(member);
            round = Ready();
        }
        Start(round);
    }

    /// <summary>Takes back a token not yet in a round; false when its round has it.</summary>
    private bool Withdraw(Member member)
    {
        List<Member>? round;
        lock (_gate)
        {
            if (!_posted.Remove(member)) return false;
            round = Ready();
        }
        Start(round);
        return true;
    }

    private void Leave(Member member)
    {
        List<Member>? round;
        lock (_gate)
        {
            _members.Remove(member);
            _posted.Remove(member);
            round = Ready();
        }
        Start(round);
    }

    /// <summary>The next round, once every member has chosen its token; null until then. Called under the gate.</summary>
    private List<Member>? Ready()
    {
        if (_posted.Count == 0 || _posted.Count < _members.Count) return null;
        List<Member> round = [.. _posted];
        _posted.Clear();
        return round;
    }

    private void Start(List<Member>? round)
    {
        if (round is not null) _ = RunAsync(round);
    }

    private async Task RunAsync(List<Member> round)
    {
        try
        {
            var reads = new SequenceToken[round.Count];
            var samplers = new LlamaSampler?[round.Count];
            var chosen = new int[round.Count];
            using (await round.MinBy(m => m.Priority)!.Turn())
            {
                for (int i = 0; i < round.Count; i++)
                {
                    reads[i] = round[i].Conversation.Readying(round[i].Token);
                    samplers[i] = round[i].Conversation.Sampler;
                }
                context.Step(reads, samplers, chosen);
            }
            for (int i = 0; i < round.Count; i++) round[i].Conversation.Advanced(chosen[i]);
            foreach (var member in round) member.Done!.TrySetResult();
        }
        catch (Exception ex)
        {
            foreach (var member in round) member.Done!.TrySetException(ex);
        }
    }

    /// <summary>One reply in the rounds.</summary>
    public sealed class Member : IDisposable
    {
        private readonly TokenRounds _rounds;
        private int _left;

        internal Member(TokenRounds rounds, LiveConversation conversation, RequestPriority priority, Func<ValueTask<IDisposable>> turn)
        {
            _rounds = rounds;
            Conversation = conversation;
            Priority = priority;
            Turn = turn;
        }

        internal LiveConversation Conversation { get; }

        internal RequestPriority Priority { get; }

        internal Func<ValueTask<IDisposable>> Turn { get; }

        /// <summary>The token it chose last, to be read in the next round.</summary>
        internal int Token { get; private set; }

        internal TaskCompletionSource? Done { get; private set; }

        /// <summary>Reads <paramref name="token"/> — the reply's latest — in the next round, which chooses the one after it
        /// (<see cref="LiveConversation.Next"/>). Cancelled while the round waits for the others, the token is taken back;
        /// once the round has it, the round is waited for.</summary>
        public async Task AdvanceAsync(int token, CancellationToken ct)
        {
            Token = token;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Done = done;
            _rounds.Post(this);
            using (ct.Register(() =>
                   {
                       if (_rounds.Withdraw(this)) done.TrySetCanceled(ct);
                   }))
                await done.Task;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0) _rounds.Leave(this);
        }
    }
}
