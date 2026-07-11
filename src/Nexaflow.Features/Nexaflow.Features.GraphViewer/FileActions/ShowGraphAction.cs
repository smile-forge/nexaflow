using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.GraphViewer.FileActions;

/// <summary>
/// Opens a knowledge-graph file in the Graph viewer. The bundled file-type map points the <c>/graph</c>
/// experience at <c>**/graph.json</c> via a PathPattern criterion (specificity 5), so it beats the JSON
/// viewer's <c>*.json</c> extension (4) and wins the default double-click.
/// </summary>
public sealed class ShowGraphAction(IShellServices shell) : IFileAction, ICacheable
{
    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public string Icon => "🕸";
    public string DisplayName => "As Graph";
    public static string? StaticExperienceId => "/graph";
    public string ExperienceId => "/graph";
    public string ExperienceDescription => "Knowledge-graph viewer: pan/zoom the product ⊕ code graph with communities and a property drawer (graph.json)";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(GraphViewerTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        return first is not null && PerformAction(first);
    }
}
