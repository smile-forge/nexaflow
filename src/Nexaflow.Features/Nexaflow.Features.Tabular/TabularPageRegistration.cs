using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Features.Tabular.Views;

namespace Nexaflow.Features.Tabular;

public sealed class TabularPageRegistration : IPageRegistration
{
    private readonly IShellServices         _shell;
    private readonly IAIService             _ai;
    private readonly TabularTemplatesConfig _templates;

    public TabularPageRegistration(IShellServices shell, IAIService ai, TabularTemplatesConfig templates)
    {
        _shell     = shell;
        _ai        = ai;
        _templates = templates;
    }

    public static string StaticPageKind => "Tabular";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path       = pageParams?.GetValueOrDefault("path")       ?? string.Empty;
        var transforms = pageParams?.GetValueOrDefault("transforms");
        var title      = string.IsNullOrEmpty(path) ? "Tabular" : Path.GetFileName(path);

        var page = new Page
        {
            Title          = title,
            Icon           = "▦",
            PageParams     = pageParams is null ? null : new Dictionary<string, string>(pageParams),
            ContentFactory = () => new TabularView(new TabularViewModel(path, _shell, _ai, _templates, transforms)),
        };

        // "folder › file.csv" — the folder crumb opens a file-explorer tab there.
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
