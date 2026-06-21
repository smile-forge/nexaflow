using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local.Harness;

/// <summary>
/// Drives the provider's server-side tool loop: format → infer → parse → (run server tool → feed result
/// back → repeat) → return the final visible text. The inference call is injected so the loop is
/// testable without a real model. This is the INNER loop; any <c>client_tool</c> fences in the final
/// text pass through untouched to Nexaflow's outer client loop.
/// </summary>
public static class ServerToolLoop
{
    /// <summary>Runs one inference: returns the model's raw output for <paramref name="prompt"/>.</summary>
    public delegate Task<string> InferFunc(string prompt, IReadOnlyList<string> antiPrompts, CancellationToken ct);

    public static async Task<string> RunAsync(
        IModelHarness            harness,
        IReadOnlyList<LlmMessage> messages,
        HarnessOptions           options,
        ServerToolRegistry       registry,
        InferFunc                infer,
        int                      maxRounds,
        CancellationToken        ct)
    {
        var transcript = harness.Format(messages, options, registry.Tools);
        HarnessResult parsed = new();

        for (int round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var raw = await infer(transcript, harness.AntiPrompts, ct);
            parsed  = harness.Parse(raw);

            if (parsed.ToolCall is null)
                return parsed.VisibleText;   // final answer (may contain client_tool fences — left intact)

            var result = await registry.InvokeAsync(parsed.ToolCall, ct);
            transcript += harness.RenderToolRound(parsed.ToolCall, parsed.Thought, result);
        }

        return string.IsNullOrWhiteSpace(parsed.VisibleText)
            ? "(stopped after too many server-side tool calls)"
            : parsed.VisibleText;
    }
}
