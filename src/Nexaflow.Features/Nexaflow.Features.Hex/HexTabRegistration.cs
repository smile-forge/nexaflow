using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.ViewModels;
using Nexaflow.Features.Hex.Views;
using System.IO;

namespace Nexaflow.Features.Hex;

public sealed class HexTabRegistration : IPageRegistration
{
    public string PageKind => "Hex";

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Hex" : Path.GetFileName(path);

        return new Page
        {
            Title          = title,
            Icon           = "⬡",
            Breadcrumbs    = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new HexView(new HexViewModel(path)),
        };
    }
}
