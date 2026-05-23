using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Features.Web.Views;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Web;

/// <summary>
/// Registers the HTML viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter containing the file path to display.
/// </summary>
public sealed class HtmlTabRegistration : ITabRegistration
{
    public string PageKind => "Html";

    public Page CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var filePath = pageParams?.GetValueOrDefault("path") ?? "";
        var title    = Path.GetFileName(filePath);
        return new Page
        {
            Title       = title,
            Icon        = "🌐",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () => new HtmlView(new HtmlViewModel(filePath))
        };
    }
}
