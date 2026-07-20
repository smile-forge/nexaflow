using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Features.Markdown.Views;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// Registers the Markdown viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter (the file) and an optional ">"-joined "heading" to scroll to.
/// </summary>
public sealed class MarkdownTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Markdown";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var filePath = pageParams?.GetValueOrDefault("path") ?? "";
        var title    = Path.GetFileName(filePath);
        var heading  = pageParams?.GetValueOrDefault("heading") is { Length: > 0 } h
            ? h.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;
        var page = new Page
        {
            Title       = title,
            Icon        = "📝",
            ContentFactory = () => new MarkdownView(new MarkdownViewModel(filePath, shell, heading))
        };
        page.SetFileBreadcrumbs(filePath, title);
        return page;
    }
}
