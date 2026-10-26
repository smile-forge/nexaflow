using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// One context of a language model, and what reads and writes in it: its conversations (<see cref="ConversationLog"/>), the
/// prompts being read (<see cref="PromptReads"/>) and the replies being written (<see cref="TokenRounds"/>). The model's own lane
/// holds one cache its conversations share, one as long as the model may be given. The lane for work nobody is watching
/// (<see cref="ChatModel.Enter"/>) gives each conversation a slice of its own: each step then reads every reply's token against that
/// reply's cells alone, rather than against every cell in use, so many replies are written at once as fast as few, each as long as
/// its slice.
/// </summary>
internal sealed class ChatLane : IDisposable
{
    public ChatLane(ChatModel model, int contextTokens, int sequences, int batch, bool perConversation)
    {
        Context = new LlamaContext(model.Model, contextTokens, sequences, batch, model.Spec.ContextCache, perConversation);
        try
        {
            Prompts = new PromptReads(Context);
            Rounds = new TokenRounds(Context, Prompts, writing => Conversations.Gather(writing));
            Conversations = new ConversationLog(this, model.Spec.Id);
        }
        catch
        {
            Context.Dispose();
            throw;
        }
    }

    public LlamaContext Context { get; }

    /// <summary>The prompts being read in its conversations, read together.</summary>
    public PromptReads Prompts { get; }

    /// <summary>The replies being written in its conversations, each token of every one read together.</summary>
    public TokenRounds Rounds { get; }

    public ConversationLog Conversations { get; }

    /// <summary>How many sequences its cache has: replies written at once, and conversations kept while fewer are.</summary>
    public int Sequences => Context.Sequences;

    /// <summary>Whether each conversation has a slice of the cache of its own.</summary>
    public bool PerConversation => Context.PerConversation;

    /// <summary>The longest a conversation in it may grow: the whole cache where its conversations share it, each one's slice where
    /// they do not.</summary>
    public int Longest => PerConversation ? Context.Tokens / Sequences : Context.Tokens;

    public void Dispose()
    {
        Conversations.Dispose();
        Context.Dispose();
    }
}
