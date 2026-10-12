using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The conversations a model keeps open, each a live <see cref="Generator"/> whose context only grows. A request
/// continues one when the turns it has read — its last reply included — begin the request's turns and the rest is
/// text: only those are read, in tens of milliseconds where re-reading the conversation costs hundreds. Anything else
/// starts a conversation afresh. Requests carry whole conversations, so nothing in the app has to know this cache
/// exists; a few are kept, least recently used dropped first.
/// </summary>
internal sealed class ConversationLog(LoadedModel model) : IDisposable
{
    internal const int Capacity = 4;

    private readonly LinkedList<LiveConversation> _kept = new();
    private readonly object _gate = new();

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
            while (_kept.Count > Capacity)
            {
                _kept.Last!.Value.Dispose();
                _kept.RemoveLast();
            }
        }
    }

    public LiveConversation Start() => new(model);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var conversation in _kept) conversation.Dispose();
            _kept.Clear();
        }
    }
}

/// <summary>One conversation's context: what it has read, and the generator holding it.</summary>
internal sealed class LiveConversation : IDisposable
{
    /// <summary>Prompt is read in slices of this many tokens — a step each, so a long read yields to a reply.</summary>
    internal const int Chunk = 512;

    private readonly GeneratorParams _params;

    public LiveConversation(LoadedModel model)
    {
        _params = new GeneratorParams(model.Model);
        _params.SetSearchOption("max_length", model.Spec.ContextTokens);
        _params.SetSearchOption("chunk_size", Chunk);
        _params.SetSearchOption("do_sample", false);
        Generator = new Generator(model.Model, _params);
    }

    public Generator Generator { get; }

    /// <summary>The turns read, ending with the reply generated for them.</summary>
    public IReadOnlyList<ChatTurn> Read { get; private set; } = [];

    public int Tokens => (int)Generator.TokenCount();

    public bool IsContinuedBy(IReadOnlyList<ChatTurn> turns) => ConversationMatch.Continues(Read, turns);

    public void Answered(IReadOnlyList<ChatTurn> turns, string reply) => Read = [.. turns, new ChatTurn(TurnRole.Assistant, reply)];

    public void Dispose()
    {
        Generator.Dispose();
        _params.Dispose();
    }
}
