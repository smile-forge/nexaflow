using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local.Harness;

/// <summary>Per-request options that shape prompt formatting.</summary>
public sealed record HarnessOptions(bool ThinkingEnabled = false);

/// <summary>The parsed outcome of one inference: visible text, optional thought, optional tool call.</summary>
public sealed class HarnessResult
{
    public string         VisibleText { get; init; } = string.Empty;
    public string?        Thought     { get; init; }
    public ServerToolCall? ToolCall   { get; init; }
}

/// <summary>
/// Per-family prompt formatter + response parser. Implementations turn Nexaflow's role-tagged messages
/// into the model's native prompt and parse its native output, including the model's own
/// server-side tool calls.
/// </summary>
public interface IModelHarness
{
    /// <summary>Stop strings passed to the executor for this family.</summary>
    IReadOnlyList<string> AntiPrompts { get; }

    /// <summary>
    /// Builds the full prompt with the prompt role model: the harness's own (authoritative) server
    /// system turn FIRST (identity + native tool syntax + tool declarations), then Nexaflow's incoming
    /// <see cref="LlmRole.System"/> message demoted to user-side input, then the User/Assistant history,
    /// ending with an open model turn ready to generate.
    /// </summary>
    string Format(IReadOnlyList<LlmMessage> messages, HarnessOptions options, IReadOnlyList<IServerTool> tools);

    /// <summary>Parses raw model output into visible text, optional thought, and an optional tool call.</summary>
    HarnessResult Parse(string raw);

    /// <summary>
    /// Native fragment appended to the transcript representing the model's tool call plus the tool
    /// response, leaving generation open so the model continues. Reconstructed from parsed data so it
    /// doesn't depend on exact raw token boundaries.
    /// </summary>
    string RenderToolRound(ServerToolCall call, string? thought, string resultText);
}
