using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Email.ViewModels;
using Nexaflow.Features.Email.Views;

namespace Nexaflow.Features.Email;

/// <summary>
/// Registers the Email viewer page. Opens one <c>.eml</c> or <c>.msg</c> given by the <c>"path"</c> param
/// (the "As Email" action / a double-click). The view-model owns a temp-folder exporter for the HTML body's
/// inline images, disposed on <see cref="Page.Closed"/>.
/// </summary>
public sealed class EmailTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Email";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "The .eml or .msg file to open."),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Email" : Path.GetFileName(path);

        var page = new Page { Icon = "✉️", Title = title };
        page.SetFileBreadcrumbs(path, title);

        page.ContentFactory = () =>
        {
            var vm = new EmailViewModel(path, shell);
            page.Closed += (_, _) => vm.Dispose();
            return new EmailView(vm);
        };

        return page;
    }
}
