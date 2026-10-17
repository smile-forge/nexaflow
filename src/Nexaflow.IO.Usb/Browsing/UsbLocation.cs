using Nexaflow.IO.Usb.Descriptors.Layout;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Browsing;

/// <summary>What kind of place in the USB tree a <see cref="UsbLocation"/> is.</summary>
public enum UsbLocationKind
{
    /// <summary>Every bus on the machine.</summary>
    Root,
    /// <summary>One host controller and the root hub on it.</summary>
    Bus,
    /// <summary>A hub port with nothing plugged in. An occupied port is the <see cref="Device"/> in it.</summary>
    Port,
    Device,
    Configuration,
    /// <summary>The device's BOS descriptor set: what it declares beyond USB 2.0 (LPM, SuperSpeed, platform capabilities).</summary>
    Bos,
    /// <summary>One descriptor inside a configuration or the BOS: an interface, an endpoint, a class-specific descriptor.</summary>
    Descriptor,
}

/// <summary>
/// One place in the USB tree, addressed by a path a person can type and a tool can pass back:
/// <c>3</c> is bus 3, <c>3-10.2</c> the device (or empty port) on port 2 of the hub on port 10 of bus 3, and
/// <c>3-10.2/config1/if0.0/ep0x81</c> an endpoint inside it.
/// </summary>
/// <remarks>
/// A location is a view of one <see cref="UsbInventorySnapshot"/>: the next snapshot resolves the same path to a
/// new location, or to nothing once the device has gone.
/// </remarks>
public sealed record UsbLocation(string Path, UsbLocationKind Kind, string Name, string Summary)
{
    /// <summary>The bus's root hub, or the hub a port belongs to.</summary>
    public UsbHubInfo? Hub { get; init; }

    /// <summary>The port, for an empty port and for a device (the port it is plugged into).</summary>
    public UsbHubPort? Port { get; init; }

    /// <summary>The device this location is, or is inside.</summary>
    public UsbDeviceNode? Device { get; init; }

    /// <summary>The descriptor blob this location is (configuration, BOS, the device descriptor of a device) or is inside.</summary>
    public UsbDescriptorMap? Map { get; init; }

    /// <summary>The descriptor this location is: the first entry of its map for a configuration or BOS.</summary>
    public UsbDescriptorEntry? Entry { get; init; }

    /// <summary>The Windows function (devnode) that owns an interface or interface association.</summary>
    public UsbFunctionNode? Function { get; init; }

    public bool IsDescriptorBacked => Map is not null;
}
