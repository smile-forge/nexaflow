using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.IO.Usb.Inventory;

/// <summary>Who answers for a function: a Windows class driver, a generic driver Nexaflow can talk raw through, or nobody.</summary>
public enum UsbFunctionMode
{
    /// <summary>An inbox class driver (HID, CDC serial, storage, audio, video…): reachable through its class API, not raw.</summary>
    WindowsClass,
    /// <summary>WinUSB, libusbK or libusb0: control, bulk and interrupt transfers with no driver change.</summary>
    GenericRaw,
    /// <summary>A third-party driver: only whatever API its vendor offers.</summary>
    VendorDriver,
    /// <summary>No driver installed — descriptors only until taken over.</summary>
    NoDriver,
    /// <summary>usbccgp, which splits a composite device into the functions beneath it.</summary>
    CompositeParent,
}

/// <summary>The most recent device power state Windows recorded (CM_POWER_DATA). D2/D3 is a selectively suspended device.</summary>
public enum UsbPowerState { Unknown, D0, D1, D2, D3 }

/// <summary>What Windows did about a devnode: its driver, where it came from, and whether it started.</summary>
public sealed record UsbDevNodeDriver(
    string? Service, string? ClassName, Guid? ClassGuid, string? InfPath, string? InfSection, string? Version, string? Provider,
    string? MatchingDeviceId, uint ProblemCode, IReadOnlyList<string> HardwareIds, IReadOnlyList<string> CompatibleIds)
{
    public static readonly UsbDevNodeDriver None = new(null, null, null, null, null, null, null, null, 0, [], []);
}

/// <summary>A devnode beneath a function or a composite parent that is not itself a USB interface (a HID collection, a LampArray…).</summary>
public sealed record UsbChildNode(string InstanceId, string? ClassName, string? Service, string? Name);

/// <summary>
/// One devnode Windows binds a driver to: the whole device when it is not composite, otherwise one <c>MI_xx</c>
/// function — which spans several interfaces when an interface association groups them (a UVC camera, USB audio).
/// This is the unit Nexaflow takes over and restores.
/// </summary>
public sealed record UsbFunctionNode(
    string InstanceId, IReadOnlyList<byte> InterfaceNumbers, string? Name, UsbDevNodeDriver Driver, UsbFunctionMode Mode,
    UsbPowerState Power, IReadOnlyList<Guid> DeviceInterfaceGuids, string? ComPort, IReadOnlyList<UsbChildNode> Children)
{
    /// <summary>The <c>MI_xx</c> number, or null when the function is the whole (non-composite) device.</summary>
    public byte? FunctionInterface { get; init; }
}

/// <summary>Strings the device returned. Null fields with <see cref="Complete"/> false mean the device was asleep or refused.</summary>
public sealed record UsbDeviceStrings(string? Manufacturer, string? Product, string? SerialNumber, IReadOnlyDictionary<byte, string> ByIndex, IReadOnlyList<ushort> Languages, bool Complete)
{
    public static readonly UsbDeviceStrings Unread = new(null, null, null, new Dictionary<byte, string>(), [], false);

    public string? this[byte index] => index == 0 ? null : ByIndex.GetValueOrDefault(index);
}

/// <summary>Whether the device declared itself WinUSB-compatible, and how.</summary>
public sealed record UsbMsOsSupport(bool? Os10, byte? Os10VendorCode, UsbMsOs20Platform? Os20, UsbWebUsbPlatform? WebUsb)
{
    public bool DeclaresWinUsbReadiness => Os10 == true || Os20 is not null;
}

/// <summary>Where a device sits: its hub and port, the ports from the root, and what the port itself reports.</summary>
public sealed record UsbPortLocation(string HubInstanceId, int Port, int Bus, IReadOnlyList<int> PortChain, bool? UserConnectable, bool? TypeC)
{
    /// <summary>The spec's "bus-port.port…" form, e.g. "3-10.2".</summary>
    public string PortPath => $"{Bus}-{string.Join('.', PortChain)}";
}

/// <summary>Everything known about one attached USB device — what it says about itself and what Windows did with it.</summary>
public sealed record UsbDeviceNode(
    string InstanceId, UsbPortLocation Location, UsbDeviceDescriptor Descriptor, byte[] RawDeviceDescriptor,
    IReadOnlyList<UsbConfiguration> Configurations, byte ActiveConfigurationValue, UsbBos? Bos, UsbDeviceStrings Strings,
    UsbSpeed Speed, bool? SuperSpeedCapable, bool IsHub, UsbPowerState Power, UsbDevNodeDriver Driver,
    IReadOnlyList<UsbFunctionNode> Functions, IReadOnlyList<UsbChildNode> Auxiliary, UsbMsOsSupport MsOs,
    string? BusReportedName, IReadOnlyList<string> Diagnostics)
{
    public ushort VendorId => Descriptor.VendorId;
    public ushort ProductId => Descriptor.ProductId;

    /// <summary>usb.ids names, filled when a database was supplied to the inventory.</summary>
    public string? VendorName { get; init; }
    public string? ProductName { get; init; }

    public UsbConfiguration? ActiveConfiguration =>
        Configurations.FirstOrDefault(c => c.Value == ActiveConfigurationValue) ?? Configurations.FirstOrDefault();

    /// <summary>The best human name: the device's own product string, then usb.ids, then what the bus reported to Windows.</summary>
    public string DisplayName => Strings.Product ?? ProductName ?? BusReportedName ?? $"{VendorId:X4}:{ProductId:X4}";

    /// <summary>Ejected: Windows has let go of it for safe removal ("safe to remove"), and it is still in its port —
    /// unusable until it is unplugged and plugged in again.</summary>
    public bool IsEjected => Driver.ProblemCode == Browsing.UsbProblemCodes.PreparedForRemoval;

    public UsbFunctionNode? Function(string instanceId) =>
        Functions.FirstOrDefault(f => string.Equals(f.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
}
