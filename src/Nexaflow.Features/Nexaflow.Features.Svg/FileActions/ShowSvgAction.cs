using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Svg.FileActions;

/// <summary>
/// Opens an SVG (or gzipped <c>.svgz</c>) file in the SVG viewer tab. The bundled file-type map points the
/// <c>/svg</c> experience at those extensions; this action realises it by opening the tab. One image per
/// tab — opening several files opens the first.
/// </summary>
public sealed class ShowSvgAction(IShellServices shell) : IFileAction, ICacheable
{
    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public string Icon => "🎨";
    public string DisplayName => "As SVG";
    public static string? StaticExperienceId => "/svg";
    public string ExperienceId => "/svg";
    public string ExperienceDescription => "SVG vector viewer: pan/zoom crisp vector art with dimensions and element count (.svg/.svgz)";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(SvgTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        if (first is null) return false;
        return PerformAction(first);
    }
}
