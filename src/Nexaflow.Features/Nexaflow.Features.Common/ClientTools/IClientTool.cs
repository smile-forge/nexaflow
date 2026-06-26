using System.Text.Json.Nodes;

namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// A locally-executed ("client-side") tool a page or the shell exposes to the AI agent harness.
/// Distinct from any server-side / MCP tool the underlying model may natively support: these run
/// in-process via <see cref="InvokeAsync"/>.
/// <para>
/// Tools are objects, not methods on a ViewModel — each carries its own metadata and execution,
/// so the harness never switches on a tool name and new tools never touch the shell. Implement
/// this directly for non-trivial tools; use <see cref="DelegateClientTool"/> for one-liners.
/// </para>
/// </summary>
public interface IClientTool
{
    /// <summary>Stable, snake_case identifier the model emits in a <c>client_tool</c> block.</summary>
    string Name { get; }

    /// <summary>One-line description shown to the model in the tool catalogue.</summary>
    string Description { get; }

    /// <summary>Named arguments the tool accepts.</summary>
    IReadOnlyList<ClientToolParameter> Parameters { get; }

    /// <summary>Risk classification. <see cref="ToolSafety.ReadOnly"/> tools auto-run; others need approval.</summary>
    ToolSafety Safety { get; }

    /// <summary>True when several invocations of this tool may run together in one batch.</summary>
    bool Parallelizable { get; }

    /// <summary>
    /// True for a tool whose identical repeated calls are legitimate progress, not spinning — e.g. a
    /// "scroll the page" tool the model calls repeatedly to walk down a long page. Such calls are exempt
    /// from the agent loop's repeated-batch guard (the overall step cap still bounds them). Default: false.
    /// </summary>
    bool ExemptFromRepeatGuard => false;

    /// <summary>
    /// Executes the tool. The harness keeps the UI synchronization context, so implementations may
    /// read and mutate ViewModel state directly and should offload blocking work with
    /// <c>await … Async</c> / <c>Task.Run</c>. Do not throw for an expected failure — return a
    /// <see cref="ToolResult"/> with <see cref="ToolResult.IsError"/> set so the model can recover.
    /// </summary>
    Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct);
}
