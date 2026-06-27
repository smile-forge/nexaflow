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

        // Breadcrumbs: each path segment routes to a FileSystem tab; the final segment
        // is the file name itself (non-clickable).
        BuildBreadcrumbs(page, path);
        return page;
    }

    private static void BuildBreadcrumbs(Page page, string filePath)
    {
        page.Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(filePath))
        {
            page.Breadcrumbs.Add(new BreadcrumbSegment { Label = "Tabular" });
            return;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            var parts = dir.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            string acc = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                acc = i == 0 ? parts[i] + Path.DirectorySeparatorChar : Path.Combine(acc, parts[i]);
                page.Breadcrumbs.Add(new BreadcrumbSegment
                {
                    Label            = parts[i],
                    TargetPageKind   = "FileSystem",
                    TargetPageParams = new Dictionary<string, string>
                    {
                        ["mode"]  = "path",
                        ["path"]  = acc,
                        ["label"] = parts[i],
                    }
                });
            }
        }
        page.Breadcrumbs.Add(new BreadcrumbSegment { Label = Path.GetFileName(filePath) });
    }
}
