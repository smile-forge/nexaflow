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

        return new Page
        {
            Title = title,
            Icon = "📦",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new CompressedView(new CompressedViewModel(path, shell, VirtualFileSystem.Instance)),
        };
    }
}
