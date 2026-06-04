using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Presents one tool name that exists in several security contexts (e.g. <c>get_file_contents</c> from
/// two file-system tabs rooted at different folders) as a SINGLE tool the agent sees, plus a
/// <c>security_context</c> argument that selects which one to act in. The agent learns the context
/// names from this tool's description and from the labelled context listings; on invoke we route to the
/// underlying tool bound to that context's page — so the call hits the right scope, never the wrong one.
/// </summary>
internal sealed class MultiContextClientTool : IClientTool
{
    private const string ContextArg = "security_context";

    private readonly IClientTool _template;                                   // metadata source (Name/Safety/Params)
    private readonly IReadOnlyDictionary<string, IClientTool> _byContextName; // context name -> bound tool
    private readonly IReadOnlyDictionary<string, string>      _scopeByName;   // context name -> security-context string

    public MultiContextClientTool(
        IClientTool template,
        IReadOnlyDictionary<string, IClientTool> byContextName,
        IReadOnlyDictionary<string, string> scopeByName)
    {
        _template      = template;
        _byContextName = byContextName;
        _scopeByName   = scopeByName;
    }

    public string Name => _template.Name;

    public string Description =>
        _template.Description +
        " This tool is available in multiple security contexts; set '" + ContextArg + "' to one of: " +
        string.Join("; ", _byContextName.Keys.Select(n => $"\"{n}\" = {_scopeByName.GetValueOrDefault(n, n)}")) +
        ". Each context's listing in the conversation is labelled with its name.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        .. _template.Parameters,
        new ClientToolParameter(ContextArg,
            "Which security context to act in — one of the named contexts in this tool's description. " +
            "Match it to the context listing the file/path came from.", Required: true),
    ];

    public ToolSafety Safety => _template.Safety;
    public bool Parallelizable => _template.Parallelizable;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = arguments.TryGetPropertyValue(ContextArg, out var n) && n is not null ? n.ToString() : null;
        if (string.IsNullOrWhiteSpace(name) || !_byContextName.TryGetValue(name, out var tool))
        {
            var valid = string.Join(", ", _byContextName.Keys.Select(k => $"\"{k}\""));
            return Task.FromResult(ToolResult.Error(
                $"missing/unknown {ContextArg}",
                $"Set '{ContextArg}' to one of: {valid}. Each is described in the tool catalogue and labelled on its context listing."));
        }

        // The underlying tool reads only its own named args; the extra context selector is ignored.
        return tool.InvokeAsync(arguments, ct);
    }
}
