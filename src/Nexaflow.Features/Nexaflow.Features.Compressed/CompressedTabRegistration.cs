using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.Features.Compressed.Views;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed;

/// <summary>Advertises the Compressed inspector tab. Opened by the "As Archive" file action (and, after
/// the browser routing lands, as a non-default action on an archive).</summary>
public sealed class CompressedTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Compressed";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Archive" : Path.GetFileName(path);
        var dir = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);

        var page = new Page
        {
            Title = title,
            Icon = "📦",
            PageParams = pageParams,
            ContentFactory = () => new CompressedView(new CompressedViewModel(path, shell, VirtualFileSystem.Instance)),
        };

        // First crumb = the folder the archive lives in; clicking it opens a file-browser tab there.
        if (!string.IsNullOrEmpty(dir))
            page.Breadcrumbs.Add(new BreadcrumbSegment
            {
                Label = dir,
                TargetPageKind = "FileSystem",
                TargetPageParams = new()
                {
                    ["mode"] = "path",
                    ["path"] = dir,
                    ["label"] = Path.GetFileName(dir) is { Length: > 0 } n ? n : dir,
                },
            });
        page.Breadcrumbs.Add(new BreadcrumbSegment { Label = title });

        return page;
    }
}
