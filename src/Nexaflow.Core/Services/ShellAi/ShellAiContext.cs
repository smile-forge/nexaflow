using System.Text;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>
/// Builds the shell-level context block and tool list folded into every AI call (alongside the active
/// page's). Gives the AI ambient awareness — local time, workspace, open windows/tabs — plus shell tools
/// (ribbon, the pages it can open and opening one, open windows).
/// </summary>
public sealed class ShellAiContext(WorkspaceRuntime workspace)
{
    /// <summary>Where the page sits, in a few lines: the time, the workspace, and the windows and tabs open.</summary>
    public string BuildContext()
    {
        var sb  = new StringBuilder();
        var now = DateTime.Now;

        sb.AppendLine($"Local time: {now:yyyy-MM-dd HH:mm} ({now:dddd})");
        sb.AppendLine($"Workspace: {workspace.Name}");

        var windows = workspace.ShellServices?.GetWindowsWithTabs() ?? [];
        if (windows.Count > 0)
        {
            sb.AppendLine("Open windows and tabs:");
            foreach (var (title, tabs) in windows)
            {
                sb.AppendLine($"  {title}:");
                foreach (var (tabTitle, kind) in tabs)
                    sb.AppendLine(kind is null ? $"    - {tabTitle}" : $"    - {tabTitle} ({kind})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The shell tools. <c>get_open_windows</c> is withheld entirely when the user has set window
    /// access to <see cref="WindowAccessOption.No"/>; on <see cref="WindowAccessOption.Prompt"/> the
    /// tool confirms with the user at invoke time.
    /// </summary>
    public IReadOnlyList<IClientTool> BuildTools(PathScope scope)
    {
        var shell = workspace.ShellServices;
        if (shell is null) return [];

        var tools = new List<IClientTool>
        {
            new GetRibbonButtonsTool(workspace),
            new OpenPageTool(shell, Parameters, scope),
            new GetOpenablePagesTool(workspace),
        };

        var security = ConfigManager.Instance.GetAll().OfType<SecurityConfig>().FirstOrDefault()
                       ?? new SecurityConfig();
        if (security.AllowWindowAccess != WindowAccessOption.No)
            tools.Add(new GetOpenWindowsTool(shell, security));

        return tools;
    }

    /// <summary>The parameters <paramref name="kind"/> declares, or null where the workspace has no such page.</summary>
    private IReadOnlyList<PageParameter>? Parameters(string kind)
        => FeatureManager.Instance.GetPageCatalog(workspace)
                         .Where(c => string.Equals(c.PageKind, kind, StringComparison.OrdinalIgnoreCase))
                         .Select(c => c.Parameters)
                         .FirstOrDefault();
}
