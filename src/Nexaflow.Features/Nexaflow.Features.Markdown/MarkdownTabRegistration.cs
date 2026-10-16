using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Features.Markdown.Views;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// Registers the Markdown viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter (the file — a Markdown file, or a Markdown Package, *.mdz, edited unpacked and packed
/// again on save), an optional ">"-joined "heading" to scroll to, and an optional "draft" —
/// the folder a document made for the reader and kept in a temp folder is offered to be saved in. A document saved as
/// another file moves its tab with it.
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
        var draft    = pageParams?.GetValueOrDefault("draft") is { Length: > 0 } d ? d : null;
        var page = new Page
        {
            Title       = title,
            Icon        = "📝",
        };
        page.ContentFactory = () =>
        {
            var viewModel = new MarkdownViewModel(filePath, shell, heading, draft);
            viewModel.Moved += moved => Follow(page, moved);
            page.Closed     += (_, _) => viewModel.Release();
            return new MarkdownView(viewModel);
        };
        page.SetFileBreadcrumbs(filePath, title);
        return page;
    }

    /// <summary>The tab is the file the document now is: named after it, reopened as it, its crumbs its folders.</summary>
    private static void Follow(Page page, string path)
    {
        page.Title      = Path.GetFileName(path);
        page.PageParams = new Dictionary<string, string> { ["path"] = path };
        page.SetFileBreadcrumbs(path, page.Title);
    }
}
