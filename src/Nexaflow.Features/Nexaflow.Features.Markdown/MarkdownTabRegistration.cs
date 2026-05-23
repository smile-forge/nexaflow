using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Features.Markdown.Views;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// Registers the Markdown viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter containing the file path to display.
/// </summary>
public sealed class MarkdownTabRegistration : ITabRegistration
{
    public string PageKind => "Markdown";

    public Page CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var filePath = pageParams?.GetValueOrDefault("path") ?? "";
        var title    = Path.GetFileName(filePath);
        return new Page
        {
            Title       = title,
            Icon        = "📝",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () => new MarkdownView(new MarkdownViewModel(filePath))
        };
    }
}
