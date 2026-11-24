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
/// tokens — sending its text — holds the others back by as long. A reply whose client is slow to take what it wrote — its
/// session's queue full — stands aside while it waits (<see cref="Member.StandAside"/>), so the others go on without it, and
/// steps back in with its next token. A round is one GPU step, taken at the highest priority among its replies and counted
/// to that reply's client, so a prompt being read or another model's work goes between rounds.
/// </para>
/// </summary>
internal sealed class TokenRounds(LlamaContext context, PromptReads prompts, Action<IEnumerable<LiveConversation>> gather)
{
    private readonly object _gate = new();
    private readonly List<Member> _members = [];
    private readonly List<Member> _posted = [];
    /// <summary>The members not standing aside: how many tokens a round waits for.</summary>
    private int _attending;

    /// <summary>A reply about to be written in <paramref name="conversation"/>, joining the rounds; it leaves them when
    /// the returned member is disposed.</summary>
    /// <param name="turn">Waits for the GPU step a round is read in, at this reply's priority.</param>
    public Member Join(LiveConversation conversation, RequestPriority priority, Func<ValueTask<IDisposable>> turn)
    {
        var member = new Member(this, conversation, priority, turn, ClientIdentity.Current);
        lock (_gate)
        {
            _members.Add(member);
            _attending++;
        }
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
            if (_members.Remove(member) && !member.Aside) _attending--;
            _posted.Remove(member);
            round = Ready();
        }
        Start(round);
    }

    /// <summary>Sets <paramref name="member"/> aside from the rounds, or back in them: aside, a round waits for its token no
    /// longer.</summary>
    private void SetAside(Member member, bool aside)
    {
        List<Member>? round = null;
        lock (_gate)
        {
            if (member.Aside == aside || !_members.Contains(member)) return;
            member.Aside = aside;
            _attending += aside ? -1 : 1;
            if (aside) round = Ready();
        }
        Start(round);
    }

    /// <summary>The next round, once every member not standing aside has chosen its token; null until then. Called under the gate.</summary>
    private List<Member>? Ready()
    {
        if (_posted.Count == 0 || _posted.Count < _attending) return null;
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
        List<PromptReads.Slice> slices = [];
        try
        {
            var chosen = new int[0];
            // The round's step is the most urgent reply's, counted to its client.
            var leading = round.MinBy(m => m.Priority)!;
            leading.Client.Enter();
            using (await leading.Turn())
            {
                // The room its tokens leave read with whatever prompts are waiting to be read.
    // Each conversation's own slice: read in one step only in the order of their sequences, a token each; prompts are read in
    // steps of their own.
    if (context.PerConversation)
    {
        gather(round.Select(member => member.Conversation));
        round.Sort((a, b) => a.Conversation.Sequence.CompareTo(b.Conversation.Sequence));
    }
    else slices = prompts.Take(null, context.Batch - round.Count);
                var reads = new SequenceRead[round.Count + slices.Count];
                for (int i = 0; i < round.Count; i++) reads[i] = round[i].Conversation.Readying(round[i].Token);
                for (int i = 0; i < slices.Count; i++) reads[round.Count + i] = slices[i].Reading();
                chosen = new int[reads.Length];
                context.Read(reads, chosen);
            }
            for (int i = 0; i < round.Count; i++) round[i].Conversation.Advanced(chosen[i]);
            PromptReads.Finished(slices, chosen.AsSpan(round.Count));
            foreach (var member in round) member.Done!.TrySetResult();
        }
        catch (Exception ex)
        {
            PromptReads.Failed(slices, ex);
            foreach (var member in round) member.Done!.TrySetException(ex);
        }
    }

    /// <summary>One reply in the rounds.</summary>
    public sealed class Member : IDisposable
    {
        private readonly TokenRounds _rounds;
        private int _left;

        internal Member(TokenRounds rounds, LiveConversation conversation, RequestPriority priority, Func<ValueTask<IDisposable>> turn,
                        ClientIdentity client)
        {
            _rounds = rounds;
            Conversation = conversation;
            Priority = priority;
            Turn = turn;
            Client = client;
        }

        /// <summary>The client the reply is for.</summary>
        internal ClientIdentity Client { get; }

        /// <summary>Whether it stands aside from the rounds now. Read and written under the rounds' gate.</summary>
        internal bool Aside { get; set; }

        /// <summary>Stands aside from the rounds until the returned value is disposed — while what the reply wrote waits for its client
        /// to take it — so they go on without it; disposed, it steps back in, and its next token is waited for again.</summary>
        public IDisposable StandAside()
        {
            _rounds.SetAside(this, aside: true);
            return new StepBack(this);
        }

        private sealed class StepBack(Member member) : IDisposable
        {
            private int _done;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0) member._rounds.SetAside(member, aside: false);
            }
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
                await RequestActivity.WaitAsync(done.Task);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0) _rounds.Leave(this);
        }
    }
}
