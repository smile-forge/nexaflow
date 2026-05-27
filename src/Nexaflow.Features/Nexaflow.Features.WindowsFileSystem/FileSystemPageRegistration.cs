using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Features.WindowsFileSystem.Views;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem;

public sealed class FileSystemPageRegistration(
    IShellServices shell,
    IAIService ai,
    IReadOnlyDictionary<Type, IFeatureConfig> configs) : IPageRegistration
{
    public const string PageKind       = "FileSystem";
    public const string FileActionKind = "FileAction";

    public static string StaticPageKind => PageKind;
    string IPageRegistration.PageKind => PageKind;

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var mode = pageParams?.GetValueOrDefault("mode") ?? "thispc";

        if (mode == "path" && pageParams!.TryGetValue("path", out var path))
        {
            var label = pageParams.GetValueOrDefault("label") ?? "Files";
            var tab = new Page
            {
                Title       = label,
                Icon        = "📁",
                PageKind    = PageKind,
                PageParams  = pageParams,
                Breadcrumbs = { new BreadcrumbSegment { Label = label } }
            };
            tab.ContentFactory = () => CreateView(new FileSystemViewModel(path, shell, ai, configs), tab);
            return tab;
        }
        else
        {
            var tab = new Page
            {
                Title       = "This PC",
                Icon        = "🖥",
                PageKind    = PageKind,
                PageParams  = pageParams ?? new() { ["mode"] = "thispc" },
                Breadcrumbs = { new BreadcrumbSegment { Label = "This PC" } }
            };
            tab.ContentFactory = () => CreateView(FileSystemViewModel.CreateThisPc(shell, ai, configs), tab);
            return tab;
        }
    }

    private static FileSystemView CreateView(FileSystemViewModel fsVm, Page tab)
    {
        var keyHandler = new FileSystemKeyboardHandler(fsVm);
        var dropTarget = new FileSystemDropTarget(fsVm);
        var page       = new FileSystemView(fsVm, keyHandler, dropTarget);

        bool applying = false;
        page.NavigationChanged += segments =>
        {
            if (applying) return;
            applying = true;
            try { ApplyBreadcrumbs(tab, page, segments); }
            finally { applying = false; }
        };
        return page;
    }

    private static void ApplyBreadcrumbs(
        Page tab,
        FileSystemView page,
        IReadOnlyList<(string Label, string Path)> segments)
    {
        var crumbs = segments.Select((seg, i) =>
        {
            var capturedPath = seg.Path;
            var isLast = i == segments.Count - 1;
            return new BreadcrumbSegment
            {
                Label    = seg.Label,
                Navigate = isLast ? null : (string.IsNullOrEmpty(capturedPath)
                    ? () => page.ViewModel.GoToThisPc(rebuildTree: true)
                    : () => page.ViewModel.NavigateTo(capturedPath))
            };
        }).ToList();

        var currentLabel = segments[^1].Label;
        tab.Title = currentLabel.Length > 15 ? currentLabel[..10] + "…" : currentLabel;
        tab.PageParams = string.IsNullOrEmpty(segments[^1].Path)
            ? new Dictionary<string, string> { ["mode"] = "thispc" }
            : new Dictionary<string, string> { ["mode"] = "path", ["path"] = segments[^1].Path };

        tab.Breadcrumbs.Clear();
        foreach (var c in crumbs) tab.Breadcrumbs.Add(c);
    }
}
