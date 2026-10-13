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
        Schema(("title", Property("string", "What the plan achieves."), true),
               ("mermaid", Property("string", "The plan as a Mermaid flowchart, e.g. flowchart TD; A[…]-->B[…]."), false),
               ("steps", Property("array", "The steps, in order.", new JsonObject
               {
                   ["type"]       = "object",
                   ["properties"] = new JsonObject
                   {
                       ["title"]    = Property("string", "What the step does."),
                       ["tool"]     = Property("string", "The tool it calls."),
                       ["decision"] = Property("boolean", "A point to stop and reassess, in place of a tool."),
                   },
                   ["required"]   = new JsonArray("title"),
               }), true)));

    private static readonly LlmTool Suggest = new(SuggestTool,
        "Fill in the user's input box with something for them to run, instead of doing it.",
        Schema(("text", Property("string", "What to put in the box."), true)));

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

    public override IReadOnlyList<string> Misread(AgentStep step)
        => [.. step.Calls.Where(c => c.Unread is not null).Select(c => $"{c.Name}: its arguments did not read as a JSON object: {c.Unread}")];

    /// <summary>Each call whose arguments did not read, made again in the model's own protocol with only its tool offered;
    /// the call keeps its place, its id and its signature, so the step reads as if it had been written so.</summary>
    public override async Task<AgentStep?> RepairAsync(ILlmProvider provider, AgentStep step, IReadOnlyList<IClientTool> tools,
                                                       IReadOnlyCollection<string> toolNames, CancellationToken ct)
    {
        var calls = step.Calls.ToList();
        for (int i = 0; i < calls.Count; i++)
        {
            if (calls[i].Unread is not { } unread) continue;
            if (tools.FirstOrDefault(t => t.Name == calls[i].Name) is not { } tool) return null;

            LlmMessage[] repair =
            [
                new(LlmRole.System, $"A call to {tool.Name} was written with arguments that could not be read. Call {tool.Name} "
                                  + "again with the arguments it meant, as its parameters describe — for example "
                                  + $"{tool.Name}({ToolCatalog.Example(tool).ToJsonString(ToolCatalog.Readable)})."),
                new(LlmRole.User, $"The arguments as written: {unread}"),
            ];
            LlmToolCall? made = null;
            await foreach (var e in provider.StreamAsync(repair, [Declare(tool)], LlmUrgency.Interactive, ct))
                if (e is LlmToolCallRequested { Call: { Unread: null } call } && call.Name == tool.Name)
                    made ??= call;
            if (made is null) return null;

            calls[i] = calls[i] with { Arguments = made.Arguments, Unread = null };
        }

        var turn = step.Turn with { ToolCalls = [.. calls.Select(c => new ToolCall(c.Name, c.Arguments))] };
        var written = Written(step.Turn.ExplanationMarkdown, step.PlanCall is { } plan ? [plan, .. calls] : calls);
        return step with { Written = written, Turn = turn, Calls = calls };
    }

    /// <summary>A tool as the model's protocol declares it: its parameters as a JSON Schema object.</summary>
    internal static LlmTool Declare(IClientTool tool)
        => new(tool.Name, tool.Description,
               Schema([.. tool.Parameters.Select(p => (p.Name,
                                                       Property(p.Type, p.Description, p.ItemType is { } item ? new JsonObject { ["type"] = item } : null, p.Choices),
                                                       p.Required))]));

    /// <summary>One parameter's schema. An array always says what it holds — some providers refuse one that does not —
    /// and holds anything when <paramref name="items"/> is not given.</summary>
    private static JsonObject Property(string type, string description, JsonObject? items = null, IReadOnlyList<string>? choices = null)
    {
        var property = new JsonObject { ["type"] = type, ["description"] = description };
        if (type == "array") property["items"] = items ?? new JsonObject();
        if (choices is { Count: > 0 }) property["enum"] = new JsonArray([.. choices.Select(c => JsonValue.Create(c))]);
        return property;
    }

    private static JsonObject Schema(params (string Name, JsonObject Property, bool Required)[] parameters)
    {
        var properties = new JsonObject();
        foreach (var (name, property, _) in parameters)
            properties[name] = property;
        return new JsonObject
        {
            ["type"]       = "object",
            ["properties"] = properties,
            ["required"]   = new JsonArray([.. parameters.Where(p => p.Required).Select(p => (JsonNode)p.Name)]),
        };
    }

    /// <summary>The step as a transcript shows it: the prose, then each call.</summary>
    private static string Written(string text, IReadOnlyList<LlmToolCall> calls)
        => string.Join('\n', [text, .. calls.Select(c => $"[call {c.Name} {c.Arguments.ToJsonString(ToolCatalog.Readable)}]")]).Trim();
}
