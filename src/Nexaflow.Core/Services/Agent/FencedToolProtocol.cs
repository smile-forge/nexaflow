using System.Text;
using System.Text.Json.Nodes;
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
                                                    IReadOnlyCollection<string> toolNames, IAIResponseHandler handler, LlmThinking thinking,
                                                    CancellationToken ct)
    {
        var (raw, _, ended) = await StreamAsync(provider.StreamAsync(messages, [], LlmUrgency.Interactive, thinking, ct), handler);
        var turn = ClientBlockParser.Parse(raw, toolNames);
        var calls = turn.ToolCalls.Select((c, i) => new LlmToolCall($"call-{i}", c.Tool, c.Arguments)).ToList();
        var plan = turn.Plan is null ? null : new LlmToolCall("plan", "client_plan", []);
        return new AgentStep(raw, turn, calls, plan, ended);
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

    public override IReadOnlyList<string> Misread(AgentStep step) => step.Turn.ParseErrors;

    /// <summary>The reply's calls written again as fenced blocks, from the tools it names. A reply naming none is not
    /// repaired: what it meant is not known.</summary>
    public override async Task<AgentStep?> RepairAsync(ILlmProvider provider, AgentStep step, IReadOnlyList<IClientTool> tools,
                                                       IReadOnlyCollection<string> toolNames, CancellationToken ct)
    {
        var named = tools.Where(t => step.Written.Contains(t.Name, StringComparison.Ordinal)).ToList();
        if (named.Count == 0) return null;

        var example = new JsonObject { ["tool"] = named[0].Name, ["arguments"] = ToolCatalog.Example(named[0]) };
        var asked = new StringBuilder()
            .Append("# The tools it names\n").Append(ToolCatalog.Describe(named)).Append("\n\n")
            .Append("# A call, written so it runs\n```client_tool\n").Append(example.ToJsonString(ToolCatalog.Readable)).Append("\n```\n\n")
            .Append("# The reply\n").Append(step.Written).Append("\n\n")
            .Append("# Why its calls did not run\n").AppendJoin('\n', step.Turn.ParseErrors.Select(e => "- " + e));
        LlmMessage[] repair =
        [
            new(LlmRole.System, "A reply meant to call tools, but wrote the calls so they could not be read. Write each call it "
                              + "meant again, as the example is written: one fenced client_tool block for each, holding the tool "
                              + "and the arguments the reply meant. Write nothing else."),
            new(LlmRole.User, asked.ToString()),
        ];

        var turn = ClientBlockParser.Parse((await provider.CompleteAsync(repair, ct))?.RawText ?? "", toolNames);
        if (turn.ToolCalls.Count == 0 || turn.ParseErrors.Count > 0) return null;

        // The conversation remembers the calls exactly as a reply that read would have written them, and nothing besides.
        var written = string.Join("\n\n", turn.ToolCalls.Select(c =>
            "```client_tool\n" + new JsonObject { ["tool"] = c.Tool, ["arguments"] = c.Arguments.DeepClone() }.ToJsonString(ToolCatalog.Readable) + "\n```"));
        var calls = turn.ToolCalls.Select((c, i) => new LlmToolCall($"call-{i}", c.Tool, c.Arguments)).ToList();
        return new AgentStep(written, new ParsedAssistantTurn("", turn.ToolCalls, null, null, []), calls, null);
    }
}
