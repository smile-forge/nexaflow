using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Svg.Loaders;
using Nexaflow.Features.Svg.ViewModels;
using Nexaflow.Features.Svg.Views;

namespace Nexaflow.Features.Svg;

/// <summary>
/// Registers the SVG viewer page. Opens a single <c>.svg</c>/<c>.svgz</c> given by the <c>"path"</c> param
/// (the "As SVG" action). Discovered by reflection via <see cref="StaticPageKind"/>.
/// </summary>
public sealed class SvgTabRegistration : IPageRegistration
{
    private readonly SvgLoader _loader = new();

    public static string StaticPageKind => "Svg";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Path to an SVG vector file (.svg or .svgz).", Required: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "SVG" : Path.GetFileName(path);

        var page = new Page
        {
            Title = title,
            Icon = "🎨",
            ContentFactory = () => new SvgView(new SvgViewModel(path, _loader)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
