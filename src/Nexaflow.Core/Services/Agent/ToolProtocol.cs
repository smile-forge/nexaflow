using System.Diagnostics;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// One step of the model, read: what it wrote, what that means to the agent loop, and its calls — each with an id,
/// <see cref="Calls"/> lined up with the turn's <see cref="ParsedAssistantTurn.ToolCalls"/>, and the plan it proposed
/// as a call of its own.
/// </summary>
internal sealed record AgentStep(string Written, ParsedAssistantTurn Turn, IReadOnlyList<LlmToolCall> Calls, LlmToolCall? PlanCall);

/// <summary>What came of one call, as the model reads it.</summary>
internal sealed record ToolOutcome(string Text, bool IsError = false);

/// <summary>
/// How the model is offered its tools, calls them, and hears what came of them. A model that calls tools in a protocol
/// of its own is offered them in it (<see cref="NativeToolProtocol"/>); any other is told in its instructions to write a
/// fenced block (<see cref="FencedToolProtocol"/>). Everything between — approving, running, guarding against loops — is
/// the agent loop's, the same for both.
/// </summary>
internal abstract class ToolProtocol
{
    /// <summary>Draft updates reach the handler at most this often — quick enough to read as live, slow enough that a
    /// long reply is not laid out again for every token.</summary>
    private static readonly TimeSpan DraftInterval = TimeSpan.FromMilliseconds(50);

    public static ToolProtocol For(ILlmProvider provider)
        => provider.SupportsToolCalls ? new NativeToolProtocol() : new FencedToolProtocol();

    /// <summary>What the system prompt says about acting — how to call a tool and which there are — or nothing, when the
    /// tools are offered in the model's own protocol.</summary>
    public abstract string? Instructions(IReadOnlyList<IClientTool> tools);

    /// <summary>One step of the model on the conversation so far: its prose streamed to the handler as a draft, and
    /// the step read.</summary>
    public abstract Task<AgentStep> StepAsync(ILlmProvider provider, IReadOnlyList<LlmMessage> messages, IReadOnlyList<IClientTool> tools,
                                              IReadOnlyCollection<string> toolNames, IAIResponseHandler handler, CancellationToken ct);

    /// <summary>The step as the conversation remembers it.</summary>
    public abstract LlmMessage Said(AgentStep step);

    /// <summary>What came of the step's calls — one outcome for each, in order, the plan's first when it proposed one —
    /// and, after them, anything else the model should read: what the user said meanwhile, what a picture shows.</summary>
    public abstract IEnumerable<LlmMessage> Answer(AgentStep step, IReadOnlyList<ToolOutcome> outcomes, string? besides,
                                                   IReadOnlyList<LlmAttachment>? attachments);

    /// <summary>What in the step could not be read — calls the model meant, written so they cannot run. Empty when every
    /// call was read.</summary>
    public abstract IReadOnlyList<string> Misread(AgentStep step);

    /// <summary>
    /// The step with what could not be read written again: the model is asked, in a context of its own that holds only the
    /// call as written, why it did not read and an example of it made from the tool's definition, to write it so it reads.
    /// The step it returns takes the misread one's place, so the conversation never holds the attempt. Null when the call
    /// still cannot be read, or what it meant to call is not known.
    /// </summary>
    /// <param name="tools">Every tool the run can call — the misread call may name one not offered this step.</param>
    public abstract Task<AgentStep?> RepairAsync(ILlmProvider provider, AgentStep step, IReadOnlyList<IClientTool> tools,
                                                 IReadOnlyCollection<string> toolNames, CancellationToken ct);

    /// <summary>The model's reply streamed: its prose to the handler as a draft, kept clear of anything it acts on, and
    /// the calls it made in its own protocol.</summary>
    protected static async Task<(string Text, IReadOnlyList<LlmToolCall> Calls)> StreamAsync(IAsyncEnumerable<LlmStreamEvent> events,
                                                                                              IAIResponseHandler handler)
    {
        var raw   = new System.Text.StringBuilder();
        var calls = new List<LlmToolCall>();
        var draft = new ReplyDraft();
        var shown = string.Empty;
        var next  = 0L;   // the first words show at once; later ones wait out the interval

        await foreach (var e in events)
        {
            if (e is LlmToolCallRequested requested)
            {
                calls.Add(requested.Call);
                continue;
            }
            if (e is not LlmTextDelta delta) continue;

            raw.Append(delta.Text);
            draft.Append(delta.Text);
            if (Stopwatch.GetTimestamp() < next) continue;

            var text = draft.Text;
            if (text.Length == 0 || string.Equals(text, shown, StringComparison.Ordinal)) continue;

            handler.ShowReplyDraft(text);
            shown = text;
            next  = Stopwatch.GetTimestamp() + (long)(DraftInterval.TotalSeconds * Stopwatch.Frequency);
        }
        return (raw.ToString().Trim(), calls);
    }
}
