using System.Text;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// Tools for a model with no tool calling of its own: its instructions describe a fenced <c>client_tool</c> block and
/// list the tools, its reply is read by <see cref="ClientBlockParser"/>, and what came of its calls goes back as one
/// message beginning "TOOL RESULTS".
/// </summary>
internal sealed class FencedToolProtocol : ToolProtocol
{
    public override string? Instructions(IReadOnlyList<IClientTool> tools)
        => new StringBuilder()
            .Append("# Acting\n")
            .Append("You act in the app by calling its tools. To call one, write a fenced block tagged `client_tool` holding ")
            .Append("one JSON object, and stop — the results come back in a message beginning \"TOOL RESULTS\":\n\n")
            .Append("```client_tool\n{\"tool\": \"tool_name\", \"arguments\": {\"key\": \"value\"}}\n```\n\n")
            .Append("- Tools that do not depend on each other can be called together: one block each, in the same reply.\n")
            .Append("- Text outside a block is your message to the user; a reply with no block is your final answer.\n")
            .Append("- get_client_commands lists every tool this page has, if the one you need is not below.\n")
            .Append("- To suggest something for the user to run instead of doing it, fill in their input box with:\n\n")
            .Append("```client_prefill\n{\"text\": \"suggested input\"}\n```\n\n")
            .Append("For work of several steps you may lay out a plan first, then carry it out:\n\n")
            .Append("```client_plan\n{\"title\": \"…\", \"mermaid\": \"flowchart TD; A[…]-->B[…]\", ")
            .Append("\"steps\": [{\"title\":\"…\",\"tool\":\"tool_name\"},{\"title\":\"Decide …\",\"decision\":true}]}\n```\n\n")
            .Append("Then carry out its tool steps in order, stopping to reassess at a step marked \"decision\": true.\n\n")
            .Append("# Tools\n").Append(ToolCatalog.Describe(tools))
            .ToString();

    public override async Task<AgentStep> StepAsync(ILlmProvider provider, IReadOnlyList<LlmMessage> messages, IReadOnlyList<IClientTool> tools,
                                                    IReadOnlyCollection<string> toolNames, IAIResponseHandler handler, CancellationToken ct)
    {
        var (raw, _) = await StreamAsync(provider.StreamAsync(messages, ct), handler);
        var turn = ClientBlockParser.Parse(raw, toolNames);
        var calls = turn.ToolCalls.Select((c, i) => new LlmToolCall($"call-{i}", c.Tool, c.Arguments)).ToList();
        var plan = turn.Plan is null ? null : new LlmToolCall("plan", "client_plan", []);
        return new AgentStep(raw, turn, calls, plan);
    }

    public override LlmMessage Said(AgentStep step) => new(LlmRole.Assistant, step.Written);

    public override IEnumerable<LlmMessage> Answer(AgentStep step, IReadOnlyList<ToolOutcome> outcomes, string? besides,
                                                   IReadOnlyList<LlmAttachment>? attachments)
    {
        var answered = (step.PlanCall is { } plan ? [plan, .. step.Calls] : step.Calls).Zip(outcomes);
        var text = new StringBuilder("TOOL RESULTS\n");
        foreach (var (call, outcome) in answered)
        {
            text.Append("## ").Append(call.Name);
            if (outcome.IsError) text.Append(" (error)");
            text.Append('\n').Append(outcome.Text).Append("\n\n");
        }
        if (besides is { Length: > 0 }) text.Append(besides);
        yield return new LlmMessage(LlmRole.User, text.ToString().TrimEnd()) { Attachments = attachments is { Count: > 0 } ? attachments : null };
    }
}
