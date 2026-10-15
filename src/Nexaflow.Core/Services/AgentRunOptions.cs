using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// How one run of the agent differs from a request typed into the bar: a conversation it continues, how its reply is
/// to be written, and tools of the caller's own beside the page's — always offered, never ranked away.
/// </summary>
public sealed record AgentRunOptions
{
    /// <summary>A request typed into the bar: no history of its own, written to be read.</summary>
    public static AgentRunOptions Typed { get; } = new();

    /// <summary>The conversation before this request, oldest first.</summary>
    public IReadOnlyList<LlmMessage> History { get; init; } = [];

    /// <summary>Guidance on how the reply is written, added to the system prompt; null for the usual.</summary>
    public string? ReplyStyle { get; init; }

    /// <summary>Tools the caller offers beside the page's.</summary>
    public IReadOnlyList<IClientTool> Tools { get; init; } = [];

    /// <summary>Whether the model may reason before it answers — off for a quick spoken answer.</summary>
    public LlmThinking Thinking { get; init; } = LlmThinking.Allowed;

    /// <summary>Told, once the run ends however it ends, every message it added to the conversation: the request, each
    /// step as said and what came of its calls, and the final reply. A caller that keeps the conversation keeps the run as
    /// it happened — what was called and what came of it, not only what was said.</summary>
    public Action<IReadOnlyList<LlmMessage>>? Ran { get; init; }

    /// <summary>Told, before the run, the conversation with its older part condensed to make room in the model's context:
    /// a caller that keeps the conversation keeps this in place of the <see cref="History"/> it passed, and adds what the run
    /// adds after it. Not told when the conversation fits.</summary>
    public Action<IReadOnlyList<LlmMessage>>? HistoryCondensed { get; init; }
}
