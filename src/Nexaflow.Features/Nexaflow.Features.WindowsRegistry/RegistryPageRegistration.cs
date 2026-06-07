using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using Nexaflow.Features.WindowsRegistry.Views;

namespace Nexaflow.Features.WindowsRegistry;

/// <summary>Advertises the registry viewer tab ("Reg: &lt;key&gt;").</summary>
public sealed class RegistryPageRegistration(IShellServices shell) : IPageRegistration
{
    public const string PageKind = "WindowsRegistry";
    public static string StaticPageKind => PageKind;
    string IPageRegistration.PageKind => PageKind;

    /// <summary>Standalone page — offer it to the AI "add context" menu.</summary>
    public bool CanBeContextItem => true;

    IReadOnlyList<PageParameter> IPageRegistration.Parameters =>
    [
        new("hive", "Hive to open: HKCU, HKLM, or HKCR.", Required: false),
        new("path", "Key path under the hive to open (e.g. \"Software\\Microsoft\").", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title       = "Registry",
            Icon        = "🗝",
            PageKind    = PageKind,
            PageParams  = pageParams,
            Breadcrumbs = { new BreadcrumbSegment { Label = "Registry" } },
        };

        page.ContentFactory = () =>
        {
            var vm   = new RegistryViewModel(shell);
            var view = new RegistryView(vm);

            bool applying = false;
            view.NavigationChanged += segments =>
            {
                if (applying) return;
                applying = true;
                try { ApplyBreadcrumbs(page, vm, segments); }
                finally { applying = false; }
            };

            // Free the VM's resources (export/import cancellation, etc.) on permanent tab close.
            page.Closed += (_, _) => vm.Dispose();

            // Honour an initial hive/path param. A path roots the tree at that key; a bare hive just browses it.
            var hive = pageParams?.GetValueOrDefault("hive");
            var path = pageParams?.GetValueOrDefault("path");
            if (!string.IsNullOrWhiteSpace(hive))
            {
                if (string.IsNullOrWhiteSpace(path)) vm.NavigateTo(hive!);
                else                                 vm.RootAt($"{hive}\\{path}");
            }

            return view;
        };
        return page;
    }

    private static void ApplyBreadcrumbs(
        Page page, RegistryViewModel vm, IReadOnlyList<(string Label, string Path)> segments)
    {
        var crumbs = segments.Select((seg, i) =>
        {
            var capturedPath = seg.Path;
            var isLast = i == segments.Count - 1;
            return new BreadcrumbSegment
            {
                Label    = seg.Label,
                Navigate = isLast ? null : () => vm.NavigateTo(capturedPath),
            };
        }).ToList();

        var leaf = segments[^1].Label;
        page.Title = "Reg: " + (leaf.Length > 14 ? leaf[..11] + "…" : leaf);

        var (root, sub) = SplitForParams(segments[^1].Path);
        page.PageParams = sub.Length == 0
            ? new Dictionary<string, string> { ["hive"] = root }
            : new Dictionary<string, string> { ["hive"] = root, ["path"] = sub };

        page.Breadcrumbs.Clear();
        foreach (var c in crumbs) page.Breadcrumbs.Add(c);
    }

    private static (string Root, string Sub) SplitForParams(string fullPath)
    {
        var slash = fullPath.IndexOf('\\');
        return slash < 0 ? (fullPath, "") : (fullPath[..slash], fullPath[(slash + 1)..]);
    }
}
