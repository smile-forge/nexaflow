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

        // One VM per page (the factory runs once) so the dirty marker can ride on the tab title.
        var vm = new TextViewModel(path, shell);
        var page = new Page
        {
            Title       = title,
            Icon        = "📄",
            ContentFactory = () => new TextView(vm),
        };
        page.SetFileBreadcrumbs(path, title);
        vm.DirtyChanged += dirty => page.Title = dirty ? "*" + title : title;
        return page;
    }
}
