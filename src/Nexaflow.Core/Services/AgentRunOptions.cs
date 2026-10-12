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
}
