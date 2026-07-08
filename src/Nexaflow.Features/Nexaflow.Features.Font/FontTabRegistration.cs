using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Features.Font.Views;

namespace Nexaflow.Features.Font;

/// <summary>
/// Registers the Font viewer page. Opens blank ("System Fonts" compare mode) or with a pipe-separated
/// <c>"paths"</c> of font files preloaded (the "As Font" action) — one tab comparing all of them, the
/// first preselected. The view-model owns a temp-file resolver for decoded WOFF, disposed on
/// <see cref="Page.Closed"/>.
/// </summary>
public sealed class FontTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Font";
    public string PageKind => StaticPageKind;

    /// <summary>Offer the Font viewer as a standalone page (ribbon / AI context) — it needs no file.</summary>
    public bool CanBeContextItem => true;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("paths", "Pipe-separated font files to compare (.ttf/.otf/.ttc/.woff). Omit for the blank " +
                     "'System Fonts' compare mode.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var paths = pageParams?.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];

        var page = new Page { Icon = "🔤" };

        if (paths.Count == 0)
        {
            page.Title = "Fonts";
            page.Breadcrumbs.Add(new BreadcrumbSegment { Label = "System Fonts" });
            // Give the standalone tab its own identity so it isn't a null-param singleton that swallows
            // every "As Font" open (FeatureManager keeps these when opened with no params).
            page.PageParams = new Dictionary<string, string> { ["source"] = "system" };
        }
        else if (paths.Count == 1)
        {
            page.Title = Path.GetFileName(paths[0]);
            page.SetFileBreadcrumbs(paths[0], page.Title);
        }
        else
        {
            page.Title = $"{paths.Count} fonts";
            page.SetMultiFileBreadcrumbs(paths, $"{paths.Count} fonts");
        }

        page.ContentFactory = () =>
        {
            var vm = new FontViewModel(paths, shell);
            page.Closed += (_, _) => vm.Dispose();
            return new FontView(vm);
        };

        return page;
    }
}
