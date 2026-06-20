using Nexaflow.Features.Common;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Features.Text.Views;
using System.IO;

namespace Nexaflow.Features.Text;

public sealed class TextTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Text";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Text" : Path.GetFileName(path);

        return new Page
        {
            Title       = title,
            Icon        = "📄",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () => new TextView(new TextViewModel(path, shell)),
        };
    }
}
