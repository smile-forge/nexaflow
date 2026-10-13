using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// Tools in the model's own protocol: each is declared to the provider with its JSON Schema, the model's calls come back
/// structured, and what came of each goes back as a tool message answering it. Laying out a plan and filling in the
/// user's input box are tools here too, so the model never writes anything in a format it was not trained on.
/// </summary>
internal sealed class NativeToolProtocol : ToolProtocol
{
    /// <summary>The tool the model lays out a plan with.</summary>
    public const string PlanTool = "propose_plan";

    /// <summary>The tool the model fills in the user's input box with.</summary>
    public const string SuggestTool = "suggest_input";

    private static readonly LlmTool Plan = new(PlanTool,
        "For work of several steps: lay out a plan before carrying it out. Then carry out its tool steps in order, " +
        "stopping to reassess at a step marked decision.",
        Schema(("title", "string", "What the plan achieves.", true),
               ("mermaid", "string", "The plan as a Mermaid flowchart, e.g. flowchart TD; A[…]-->B[…].", false),
               ("steps", "array", "The steps: each {\"title\": …, \"tool\": the tool it calls} or {\"title\": …, \"decision\": true}.", true)));

    private static readonly LlmTool Suggest = new(SuggestTool,
        "Fill in the user's input box with something for them to run, instead of doing it.",
        Schema(("text", "string", "What to put in the box.", true)));

    public override string? Instructions(IReadOnlyList<IClientTool> tools) => null;

    public override async Task<AgentStep> StepAsync(ILlmProvider provider, IReadOnlyList<LlmMessage> messages, IReadOnlyList<IClientTool> tools,
                                                    IReadOnlyCollection<string> toolNames, IAIResponseHandler handler, CancellationToken ct)
    {
        IReadOnlyList<LlmTool> declared = [.. tools.Select(Declare), Plan, Suggest];
        var (text, made) = await StreamAsync(provider.StreamAsync(messages, declared, LlmUrgency.Interactive, ct), handler);

        var planCall = made.FirstOrDefault(c => c.Name == PlanTool);
        var suggest  = made.FirstOrDefault(c => c.Name == SuggestTool);
        var calls    = made.Where(c => c.Name is not (PlanTool or SuggestTool)).ToList();
        var turn = new ParsedAssistantTurn(
            text,
            [.. calls.Select(c => new ToolCall(c.Name, c.Arguments))],
            planCall is null ? null : ClientBlockParser.ParsePlan(planCall.Arguments),
            suggest is null ? null : (string?)suggest.Arguments["text"] ?? "",
            []);
        return new AgentStep(Written(text, made), turn, calls, planCall);
    }

    public override LlmMessage Said(AgentStep step)
        => new(LlmRole.Assistant, step.Turn.ExplanationMarkdown)
        {
            ToolCalls = step.PlanCall is { } plan ? [plan, .. step.Calls] : step.Calls,
        };

    public override IEnumerable<LlmMessage> Answer(AgentStep step, IReadOnlyList<ToolOutcome> outcomes, string? besides,
                                                   IReadOnlyList<LlmAttachment>? attachments)
    {
        foreach (var (call, outcome) in (step.PlanCall is { } plan ? [plan, .. step.Calls] : step.Calls).Zip(outcomes))
            yield return new LlmMessage(LlmRole.Tool, outcome.IsError ? "Error: " + outcome.Text : outcome.Text) { Answers = call };

        // What is not the result of a call — what the user said meanwhile, a picture to look at — is the user's to say.
        if (besides is { Length: > 0 } || attachments is { Count: > 0 })
            yield return new LlmMessage(LlmRole.User, besides ?? "") { Attachments = attachments is { Count: > 0 } ? attachments : null };
    }

    /// <summary>A tool as the model's protocol declares it: its parameters as a JSON Schema object.</summary>
    internal static LlmTool Declare(IClientTool tool)
        => new(tool.Name, tool.Description,
               Schema([.. tool.Parameters.Select(p => (p.Name, p.Type, p.Description, p.Required))]));

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] parameters)
    {
        var properties = new JsonObject();
        foreach (var (name, type, description, _) in parameters)
            properties[name] = new JsonObject { ["type"] = type, ["description"] = description };
        return new JsonObject
        {
            ["type"]       = "object",
            ["properties"] = properties,
            ["required"]   = new JsonArray([.. parameters.Where(p => p.Required).Select(p => (JsonNode)p.Name)]),
        };
    }

    /// <summary>The step as a transcript shows it: the prose, then each call.</summary>
    private static string Written(string text, IReadOnlyList<LlmToolCall> calls)
        => string.Join('\n', [text, .. calls.Select(c => $"[call {c.Name} {c.Arguments.ToJsonString()}]")]).Trim();
}
