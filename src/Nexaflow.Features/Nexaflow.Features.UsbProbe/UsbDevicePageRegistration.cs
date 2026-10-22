using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Features.UsbProbe.Views;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// Advertises the device page: one USB device and its live tools. One tab per device — <c>device</c> is which —
/// sharing the USB page's inventory, so opening a device costs no second walk of the hubs.
/// </summary>
/// <remarks>
/// The live tools arrive as <see cref="ISubfeatureHandle{T}"/>s of <see cref="IUsbToolProvider"/>: no provider
/// assembly loads until a device page offers a device to it, and a new kind of tool is a new assembly, never an edit
/// here.
/// </remarks>
public sealed class UsbDevicePageRegistration(IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> tools, IShellServices shell) : IPageRegistration
{
    public const string DeviceParam = "device";

    public static string StaticPageKind => "UsbDevice";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new(DeviceParam, "The device: its port path such as '3-10.2' (the USB page's location for it), anything inside it such as " +
            "'3-10.2/config1/if0.0', or a name or VID:PID such as '046d:0843' for the first match."),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var target = UsbLocationPath.Normalize(pageParams?.GetValueOrDefault(DeviceParam));
        var page = new Page
        {
            Title = target.Length == 0 ? Str.Get("UsbProbe.Device.Page.Title") : target,
            Icon = "🔌",
            PageParams = Params(target),
        };
        Crumbs(page, page.Title, target);

        page.ContentFactory = () =>
        {
            // Built here so defining the tab stays cheap: the inventory is shared with any USB page already open.
            var names = UsbIdsStore.ForUser;
            var inventory = UsbInventoryShare.Machine.Acquire(() => new WindowsUsbInventory(names.Current));
            var vm = new UsbDeviceViewModel(inventory, names, tools, shell, BridgeUsbDeviceBroker.ForMachine(shell.RunElevatedAsync), target);
            var view = new UsbDeviceView(vm);
            vm.Moved += () =>
            {
                page.Title = vm.Title;
                page.PageParams = Params(vm.Path);
                Crumbs(page, vm.Title, vm.Path);
            };
            page.Closed += (_, _) => vm.Dispose();
            return view;
        };
        return page;
    }

    static Dictionary<string, string> Params(string target) => target.Length == 0 ? [] : new() { [DeviceParam] = target };

    /// <summary>"USB › Logitech G502": the first crumb opens the USB page at this device.</summary>
    void Crumbs(Page page, string title, string path)
    {
        page.Breadcrumbs.Clear();
        page.Breadcrumbs.Add(new BreadcrumbSegment
        {
            Label = Str.Get("UsbProbe.Page.Title"),
            Navigate = () => shell.OpenTab(UsbProbePageRegistration.StaticPageKind,
                path.Length == 0 ? null : new() { [UsbProbePageRegistration.LocationParam] = path }),
        });
        page.Breadcrumbs.Add(new BreadcrumbSegment { Label = title, Path = path.Length == 0 ? null : path });
    }
}
