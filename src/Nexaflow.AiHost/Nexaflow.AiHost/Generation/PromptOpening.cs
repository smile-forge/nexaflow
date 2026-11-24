using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

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

    /// <summary>The client whose request read it: only that client's conversations take its cells.</summary>
    public ClientIdentity Client => Holder.Client;

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
