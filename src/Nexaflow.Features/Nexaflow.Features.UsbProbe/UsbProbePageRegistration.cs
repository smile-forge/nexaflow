using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Features.UsbProbe.Views;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// Advertises the USB page. <c>location</c> says where in the tree to look, not which page this is — there is one
/// USB tree, so opening another location re-points the open tab rather than opening a second.
/// </summary>
public sealed class UsbProbePageRegistration(UsbProbeConfig config, IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> tools, IShellServices shell) : IPageRegistration
{
    public const string LocationParam = "location";

    public static string StaticPageKind => "UsbProbe";
    public string PageKind => StaticPageKind;
    public bool CanBeContextItem => true;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new(LocationParam, "Where in the USB tree to open: '3' for bus 3, '3-10.2' for the device on port 2 of the hub on port 10 of bus 3, " +
            "'3-10.2/config1/if0.0' for an interface inside it. A device name or VID:PID (e.g. '046d:0843') opens the first match. Omit for every bus.",
            Required: false, Identity: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var location = pageParams?.GetValueOrDefault(LocationParam);
        var title = Str.Get("UsbProbe.Page.Title");
        var page = new Page
        {
            Title = title,
            Icon = "🔌",
            PageParams = Params(location),
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
        };

        page.ContentFactory = () =>
        {
            // Built here so defining the tab stays cheap: the inventory reads the machine once the view loads.
            var names = UsbIdsStore.ForUser;
            var inventory = UsbInventoryShare.Machine.Acquire(() => new WindowsUsbInventory(names.Current));
            var vm = new UsbProbeViewModel(inventory, names, config, tools, shell, location);
            var view = new UsbProbeView(vm);
            vm.NavigationChanged += crumbs => ApplyBreadcrumbs(page, vm, crumbs);
            page.Closed += (_, _) => vm.Dispose();
            return view;
        };
        return page;
    }

    static Dictionary<string, string> Params(string? location) =>
        string.IsNullOrEmpty(UsbLocationPath.Normalize(location)) ? [] : new() { [LocationParam] = UsbLocationPath.Normalize(location) };

    /// <summary>The tab follows the page: its crumbs walk back up the tree, its title is where it is, and its params reopen it there.</summary>
    static void ApplyBreadcrumbs(Page page, UsbProbeViewModel vm, IReadOnlyList<(string Label, string Path)> crumbs)
    {
        page.Breadcrumbs.Clear();
        for (int i = 0; i < crumbs.Count; i++)
        {
            var (label, path) = crumbs[i];
            bool last = i == crumbs.Count - 1;
            page.Breadcrumbs.Add(new BreadcrumbSegment
            {
                Label = path.Length == 0 ? Str.Get("UsbProbe.Page.Title") : label,
                Path = path.Length == 0 ? null : path,
                Navigate = last ? null : () => vm.Navigate(path),
            });
        }
        (string Label, string Path) here = crumbs.Count > 0 ? crumbs[^1] : ("", "");
        page.Title = here.Path.Length == 0 ? Str.Get("UsbProbe.Page.Title") : here.Label;
        page.PageParams = Params(here.Path);
    }
}
