using System.Text;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.Agent;

/// <summary>Tools described in words for a model to read: each with what it does and its parameters.</summary>
internal static class ToolCatalog
{
    public static string Describe(IEnumerable<IClientTool> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
            sb.Append(Describe(t)).Append('\n');
        var s = sb.ToString().TrimEnd();
        return s.Length == 0 ? "(no tools available here)" : s;
    }

    private static string Describe(IClientTool t)
    {
        var sb = new StringBuilder();
        sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
        foreach (var p in t.Parameters)
        {
            sb.Append("\n    • ").Append(p.Name).Append(" (").Append(p.Type);
            if (!p.Required) sb.Append(", optional");
            sb.Append("): ").Append(p.Description);
        }
        return sb.ToString();
    }
}
