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

    /// <summary>Whether the harness may auto-run this tool. <see cref="ToolSafety.SafeOperation"/> tools
    /// auto-run (even if they mutate visible/uncommitted state); <see cref="ToolSafety.RequiresApproval"/>
    /// tools are approved first.</summary>
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
    /// The abilities the tool needs a model for: one any of whose abilities has no model to serve it is not offered. One waiting
    /// on a download is offered, and its first use offers the download. A tool whose result is a picture needs
    /// <see cref="AiAbility.ImageRecognition"/>, which a General Intelligence model that sees pictures itself meets too; one whose
    /// result is a recording needs <see cref="AiAbility.SpeechIn"/>, met too by one that hears. Default: none.
    /// </summary>
    IReadOnlyList<AiAbility> Needs => [];

    /// <summary>
    /// How a call of this tool is announced where it is not shown for approval — in a voice conversation, which says
    /// it, then does it unless told to stop. Null leaves the announcement to the tool's name. Worth giving for a tool
    /// that <see cref="ToolSafety.RequiresApproval"/>. Default: null.
    /// </summary>
    ToolCallAnnouncement? Announce(JsonObject arguments) => null;

    /// <summary>
    /// The safety of one call, where it depends on what the call asks for — a picture drawn at high quality is slow
    /// and dearer, so it is approved where a standard one is not. Default: <see cref="Safety"/>.
    /// </summary>
    ToolSafety SafetyOf(JsonObject arguments) => Safety;

    /// <summary>
    /// Executes the tool. The harness keeps the UI synchronization context, so implementations may
    /// read and mutate ViewModel state directly and should offload blocking work with
    /// <c>await … Async</c> / <c>Task.Run</c>. Do not throw for an expected failure — return a
    /// <see cref="ToolResult"/> with <see cref="ToolResult.IsError"/> set so the model can recover.
    /// </summary>
    Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct);

    /// <summary>
    /// Whether a call runs on in the background after the request that made it moves on — slow work the user need not wait
    /// for, such as a picture drawn at high quality. The model is told it has started; the user sees it among the background
    /// tasks, can stop it, and is told when it ends. Default: false — the request waits for it.
    /// </summary>
    bool RunsInBackground(JsonObject arguments) => false;
}
