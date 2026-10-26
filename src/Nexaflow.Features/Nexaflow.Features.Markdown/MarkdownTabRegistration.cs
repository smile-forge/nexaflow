using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Features.Markdown.Views;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// Registers the Markdown viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter (the file — a Markdown file, or a Markdown Package, *.mdz, edited unpacked and packed
/// again on save), an optional ">"-joined "heading" to scroll to or saved reference "place" to land on, and an optional
/// "draft" — the folder a document made for the reader and kept in a temp folder is offered to be saved in. A document saved
/// as another file moves its tab with it.
/// </summary>
public sealed class MarkdownTabRegistration(IShellServices shell, IAIService ai, IVirtualFileSystem vfs) : IPageRegistration
{
    public static string StaticPageKind => "Markdown";
    public string PageKind => StaticPageKind;

    /// <summary>A saved reference to land on in the document — <c>heading:getting-started</c>.</summary>
    public const string PlaceParameter = "place";

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the Markdown file or Markdown Package (.mdz) to open.", OnDisk: true),
        new("heading", "Headings to scroll to, outermost first, joined by '>'.", Required: false),
        new(PlaceParameter, "A saved reference to land on, such as heading:getting-started.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var filePath = pageParams?.GetValueOrDefault("path") ?? "";
        var title    = Path.GetFileName(filePath);
        var heading  = pageParams?.GetValueOrDefault("heading") is { Length: > 0 } h
            ? h.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;
        var draft    = pageParams?.GetValueOrDefault("draft") is { Length: > 0 } d ? d : null;
        var place    = pageParams?.GetValueOrDefault(PlaceParameter) is { Length: > 0 } p ? p : null;
        var page = new Page
        {
            Title          = title,
            Icon           = "📝",
            // Where in the document to land says nothing about which document it is: a tab open on it is brought forward and moved there.
            LocationParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heading", PlaceParameter },
        };
        page.ContentFactory = () =>
        {
            var viewModel = new MarkdownViewModel(filePath, shell, vfs, heading, draft, ai, place);
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
