using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>Lists the user's ribbon buttons — label, the page each opens, and any params.</summary>
public sealed class GetRibbonButtonsTool(WorkspaceRuntime workspace) : IClientTool
{
    public string Name => "get_ribbon_buttons";
    public string Description => "List the user's ribbon buttons: each button's label, the page it opens, and its parameters.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        workspace.Workspace.EnsureSharedServicesLoaded();
        var items = workspace.Workspace.RibbonService?.Load() ?? RibbonLayoutService.LoadDefaults();

        var sb    = new StringBuilder("Ribbon buttons:\n");
        var count = 0;
        foreach (var item in items)
        {
            // A HalfGroup is two stacked sub-buttons; flatten to its leaves. Separators are skipped.
            var leaves = item.Kind == RibbonItemKind.HalfGroup ? item.HalfItems ?? [] : [item];
            foreach (var leaf in leaves)
            {
                if (leaf.Kind == RibbonItemKind.Separator || string.IsNullOrWhiteSpace(leaf.Label)) continue;
                count++;
                sb.Append("  - ").Append(leaf.Label);
                if (!string.IsNullOrEmpty(leaf.PageKind))
                {
                    sb.Append(" → ").Append(leaf.PageKind);
                    if (leaf.PageParams is { Count: > 0 } p)
                        sb.Append(" {").Append(string.Join(", ", p.Select(kv => $"{kv.Key}={kv.Value}"))).Append('}');
                }
                sb.Append('\n');
            }
        }

        return Task.FromResult(count == 0
            ? ToolResult.Ok("no ribbon buttons", "The ribbon has no buttons.")
            : ToolResult.Ok($"{count} ribbon button(s)", sb.ToString().TrimEnd()));
    }
}
