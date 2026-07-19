using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>
/// Lists the titles of other application windows the user has open (a Win32 enumeration). Gated by the
/// "Allow AI to view other windows" security setting: withheld entirely on <c>No</c>, and confirmed
/// with the user at invoke time on <c>Prompt</c>.
/// </summary>
public sealed class GetOpenWindowsTool(IShellServices shell, SecurityConfig security) : IClientTool
{
    public string Name => "get_open_windows";
    public string Description => "List the titles of other application windows the user currently has open on their desktop.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => false;   // may show a confirmation prompt

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (security.AllowWindowAccess == WindowAccessOption.Prompt)
        {
            var ok = await shell.ConfirmAsync(
                "Allow window access?",
                "The AI wants to see the list of other application windows you have open. Allow this?",
                ct);
            if (!ok)
                return ToolResult.Error("window access declined",
                    "The user declined to share the list of their open windows.");
        }

        var titles = NativeMethods.ListVisibleWindowTitles();
        if (titles.Count == 0)
            return ToolResult.Ok("no windows", "No other titled windows are currently open.");

        var sb = new StringBuilder("Open windows:\n");
        foreach (var t in titles) sb.Append("  - ").Append(t).Append('\n');
        return ToolResult.Ok($"{titles.Count} window(s)", sb.ToString().TrimEnd());
    }
}
