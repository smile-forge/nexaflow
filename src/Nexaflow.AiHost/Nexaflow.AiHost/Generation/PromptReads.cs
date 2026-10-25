using Nexaflow.AiHost.Llama;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The prompts a model's conversations are reading, read together. A request hands over its prompt a slice at a time and
/// waits for its turn on the GPU; whichever step comes first — its own, another request's, or a round of the replies being
/// written (<see cref="TokenRounds"/>) — reads every slice handed over that fits beside what that step reads, in one
/// llama.cpp call. Several prompts read in one step cost little more than one, and a round of replies reads a slice in the
/// room its handful of tokens leave, so a prompt that comes while replies are written is read with them rather than between
/// their tokens.
/// </summary>
internal sealed class PromptReads(LlamaContext context)
{
    private readonly object _gate = new();
    private readonly List<Slice> _waiting = [];

    /// <summary>
    /// Reads <paramref name="tokens"/> — at most a step's — into <paramref name="conversation"/>, and chooses the token after
    /// them where <paramref name="sample"/>: in the step <paramref name="turn"/> waits for, or sooner in another's. Given up
    /// while it waits, nothing of it is read; given up once a step has it, that step is waited for, so the conversation is never
    /// let go part-way through a read.
    /// </summary>
    public async Task ReadAsync(LiveConversation conversation, ReadOnlyMemory<int> tokens, bool sample, Func<ValueTask<IDisposable>> turn)
    {
        var slice = new Slice(conversation, tokens, sample);
        lock (_gate) _waiting.Add(slice);
        try
        {
            using (await turn())
                if (Take(slice, context.Batch) is { Count: > 0 } taken) Read(taken);
        }
        catch
        {
            bool waiting;
            lock (_gate) waiting = _waiting.Remove(slice);
            if (!waiting)
            {
                try { await slice.Done.Task; }
                catch { }   // what went wrong is the read's, and it is what the caller hears below
            }
            throw;
        }
        await slice.Done.Task;
    }

    /// <summary>The slices waiting that fit in <paramref name="room"/> tokens, oldest first — <paramref name="first"/> before
    /// them, while it still waits: none at all where another step has taken it — out of waiting.</summary>
    internal List<Slice> Take(Slice? first, int room)
    {
        var taken = new List<Slice>();
        lock (_gate)
        {
            if (first is not null)
            {
                if (!_waiting.Remove(first)) return taken;
                taken.Add(first);
                room -= first.Tokens.Length;
            }
            for (int i = 0; i < _waiting.Count && room > 0;)
            {
                if (_waiting[i].Tokens.Length > room)
                {
                    i++;
                    continue;
                }
                room -= _waiting[i].Tokens.Length;
                taken.Add(_waiting[i]);
                _waiting.RemoveAt(i);
            }
        }
        return taken;
    }

    /// <summary>Reads <paramref name="taken"/> in a step of their own.</summary>
    private void Read(List<Slice> taken)
    {
        var reads = new SequenceRead[taken.Count];
        for (int i = 0; i < taken.Count; i++) reads[i] = taken[i].Reading();
        var chosen = new int[taken.Count];
        try
        {
            context.Read(reads, chosen);
        }
        catch (Exception ex)
        {
            Failed(taken, ex);
            return;
        }
        Finished(taken, chosen);
    }

    /// <summary>Each of <paramref name="taken"/> has been read, and the token after it chosen where it asked — in
    /// <paramref name="chosen"/>, in the same order.</summary>
    internal static void Finished(IReadOnlyList<Slice> taken, ReadOnlySpan<int> chosen)
    {
        for (int i = 0; i < taken.Count; i++)
        {
            taken[i].Conversation.ReadIn(taken[i].Tokens.Length, chosen[i]);
            taken[i].Done.TrySetResult();
        }
    }

    /// <summary>The read <paramref name="taken"/> were in failed.</summary>
    internal static void Failed(IReadOnlyList<Slice> taken, Exception ex)
    {
        foreach (var slice in taken) slice.Done.TrySetException(ex);
    }

    /// <summary>One slice of a prompt, handed over to be read.</summary>
    internal sealed class Slice(LiveConversation conversation, ReadOnlyMemory<int> tokens, bool sample)
    {
        public LiveConversation Conversation { get; } = conversation;

        public ReadOnlyMemory<int> Tokens { get; } = tokens;

        /// <summary>Completes once it is read.</summary>
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>It, as its conversation reads it next.</summary>
        public SequenceRead Reading() => Conversation.Reading(Tokens, sample);
    }
}
