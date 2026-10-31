using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

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
    public IconRef Icon => IconRef.Emoji("🎨");
    public string DisplayName => Str.Get("Svg.Action.AsSvg");
    public static string? StaticExperienceId => "/svg";
    public string ExperienceId => "/svg";
    public string ExperienceDescription => Str.Get("Svg.Action.ExperienceDescription");
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        shell.OpenTab(SvgTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var first = filePaths.FirstOrDefault();
        if (first is null) return Task.FromResult(false);
        return PerformActionAsync(first, ct);
    }
}
