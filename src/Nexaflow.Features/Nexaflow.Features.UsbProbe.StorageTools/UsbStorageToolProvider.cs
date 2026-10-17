using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Storage;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Plugins;

namespace Nexaflow.Features.UsbProbe.StorageTools;

/// <summary>
/// Offers a storage tool for every function that is a disk: one speaking the mass-storage class, or one Windows made a
/// disk drive of.
/// </summary>
[Subfeature("usbprobe", "storage",
    DisplayName = "Mass storage",
    Description = "Browses a USB disk's volumes and copies files to and from it, reading every copy back to check it.",
    Order = 0)]
public sealed class UsbStorageToolProvider : IUsbToolProvider
{
    readonly IUsbStorageLocator _locator;

    public UsbStorageToolProvider() : this(new WindowsUsbStorage()) { }

    internal UsbStorageToolProvider(IUsbStorageLocator locator) => _locator = locator;

    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.StorageTools;component/Views/StorageTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host) =>
    [
        .. device.Functions
            .Where(f => f.Mode != UsbFunctionMode.CompositeParent)
            .Where(f => device.Speaks(f, UsbClassCodes.MassStorage)
                     || f.Children.Any(c => string.Equals(c.ClassName, "DiskDrive", StringComparison.OrdinalIgnoreCase)))
            .Select(f => new UsbStorageTool(f, _locator, host)),
    ];
}
