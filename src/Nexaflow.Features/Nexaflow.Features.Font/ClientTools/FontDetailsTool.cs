using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Font.ViewModels;

namespace Nexaflow.Features.Font.ClientTools;

/// <summary>
/// Returns the details-panel contents (identity, style &amp; metrics, licensing, technical) for any font in
/// the compare list — not just the selected one. Read-only: it only reads the already-loaded font's
/// metadata. The AI references a font by its 1-based list index or its name (see the page context).
/// </summary>
public sealed class FontDetailsTool(FontViewModel vm) : IClientTool
{
    public string Name => "get_font_details";

    public string Description =>
        "Read the full metadata (identity, style & metrics, licensing, technical) of a font in the " +
        "compare list. Reference it by its 1-based index in the list or by its name; omit to use the " +
        "currently selected font.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("font", "The font to describe: its 1-based index in the compare list, or its name. " +
                    "Omit for the currently selected font.", Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var reference = ToolArgs.Str(arguments, "font");
        var item = vm.ResolveFont(reference);
        if (item is null)
            return Task.FromResult(ToolResult.Error(
                "No matching font.",
                "No font matched that reference. Reference a font by its 1-based index in the compare " +
                "list or by its name; the page context lists them."));

        if (!item.CanRender)
            return Task.FromResult(ToolResult.Ok(
                $"{item.DisplayName} could not be loaded.",
                $"\"{item.DisplayName}\" could not be loaded: {item.LoadError}"));

        item.EnsureFacesLoaded();

        var sb = new StringBuilder();
        sb.AppendLine($"Details for \"{item.DisplayName}\" ({item.SourceLabel}):");
        foreach (var row in item.Details)
        {
            if (row.IsHeader) sb.Append('\n').Append('[').Append(row.Label).Append("]\n");
            else sb.Append("  ").Append(row.Label).Append(": ").Append(row.Value).Append('\n');
        }

        return Task.FromResult(ToolResult.Ok($"Read details for {item.DisplayName}.", sb.ToString()));
    }
}
