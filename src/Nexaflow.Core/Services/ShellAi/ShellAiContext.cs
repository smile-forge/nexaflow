using System.Text;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>
/// Builds the shell-level context block and tool list folded into every AI call (alongside the active
/// page's). Gives the AI ambient awareness — local time, workspace, theme, open windows/tabs, the
/// catalogue of openable pages — plus shell tools (ribbon, open-page, open-windows).
/// </summary>
public sealed class ShellAiContext(WorkspaceRuntime workspace)
{
    /// <summary>The shell context block (plain text) prepended to the page context.</summary>
    public string BuildContext()
    {
        var sb  = new StringBuilder();
        var now = DateTime.Now;

        sb.AppendLine("[Shell]");
        sb.AppendLine($"Local time: {now:yyyy-MM-dd HH:mm} ({now:dddd})");
        sb.AppendLine($"Workspace: {workspace.Name}");
        sb.AppendLine($"Theme: {ThemeManager.Current}");

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

        var catalog = FeatureManager.Instance.GetPageCatalog(workspace);
        if (catalog.Count > 0)
        {
            sb.AppendLine("Openable pages (use open_page; * = required param, ? = optional):");
            foreach (var (kind, parms) in catalog.OrderBy(c => c.PageKind, StringComparer.OrdinalIgnoreCase))
            {
                if (parms.Count == 0) { sb.AppendLine($"  - {kind}"); continue; }
                var ps = string.Join(", ", parms.Select(p => p.Required ? $"{p.Name}*" : $"{p.Name}?"));
                sb.AppendLine($"  - {kind} (params: {ps})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The shell tools. <c>get_open_windows</c> is withheld entirely when the user has set window
    /// access to <see cref="WindowAccessOption.No"/>; on <see cref="WindowAccessOption.Prompt"/> the
    /// tool confirms with the user at invoke time.
    /// </summary>
    public IReadOnlyList<IClientTool> BuildTools()
    {
        var shell = workspace.ShellServices;
        if (shell is null) return [];

        var tools = new List<IClientTool>
        {
            new GetRibbonButtonsTool(workspace),
            new OpenPageTool(shell),
        };

        var security = ConfigManager.Instance.GetAll().OfType<SecurityConfig>().FirstOrDefault()
                       ?? new SecurityConfig();
        if (security.AllowWindowAccess != WindowAccessOption.No)
            tools.Add(new GetOpenWindowsTool(shell, security));

        return tools;
    }
}
