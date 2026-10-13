using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>
/// The kinds of page <c>open_page</c> can open, with the parameters each takes — asked for when the assistant is
/// about to open one, rather than carried in every request.
/// </summary>
public sealed class GetOpenablePagesTool(WorkspaceRuntime workspace) : IClientTool
{
    public string Name => "get_openable_pages";

    public string Description =>
        "Lists the kinds of page open_page can open, with the parameters each takes (* required, ? optional). Takes no arguments.";

    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var catalog = FeatureManager.Instance.GetPageCatalog(workspace);
        var text = new StringBuilder();
        foreach (var (kind, parameters) in catalog.OrderBy(c => c.PageKind, StringComparer.OrdinalIgnoreCase))
        {
            text.Append("- ").Append(kind);
            if (parameters.Count > 0)
                text.Append(" (").Append(string.Join(", ", parameters.Select(p => p.Required ? $"{p.Name}*" : $"{p.Name}?"))).Append(')');
            text.Append('\n');
        }
        return Task.FromResult(ToolResult.Ok($"{catalog.Count} kinds of page", text.ToString().TrimEnd()));
    }
}
