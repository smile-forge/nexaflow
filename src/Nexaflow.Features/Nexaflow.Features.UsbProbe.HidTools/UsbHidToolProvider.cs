using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Plugins;

namespace Nexaflow.Features.UsbProbe.HidTools;

/// <summary>Offers a HID tool for every function the HID class driver holds, or that speaks the HID class.</summary>
[Subfeature("usbprobe", "hid",
    DisplayName = "HID",
    Description = "Decodes a HID device's report layout, watches its input live, and reads and sends its feature and output reports.",
    Order = 2)]
public sealed class UsbHidToolProvider : IUsbToolProvider
{
    readonly IHidAccess _access;

    public UsbHidToolProvider() : this(new WindowsHidAccess()) { }

    internal UsbHidToolProvider(IHidAccess access) => _access = access;

    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.HidTools;component/Views/HidTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host) =>
    [
        .. device.Functions
            .Where(f => f.Mode != UsbFunctionMode.CompositeParent)
            .Where(f => string.Equals(f.Driver.Service, "HidUsb", StringComparison.OrdinalIgnoreCase) || device.Speaks(f, UsbClassCodes.Hid))
            .Select(f => new HidTool(f, _access, host)),
    ];
}
