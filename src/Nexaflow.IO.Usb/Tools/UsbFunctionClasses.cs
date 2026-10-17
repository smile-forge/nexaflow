using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Tools;

/// <summary>A function's class triple — its association's when it has one, otherwise its first interface's.</summary>
public readonly record struct UsbClassTriple(byte Class, byte SubClass, byte Protocol)
{
    public string Name => UsbClassCodes.Name(Class, SubClass, Protocol);
}

/// <summary>What a function is, read from the device's active configuration — the questions every tool provider asks
/// first.</summary>
public static class UsbFunctionClasses
{
    /// <summary>The default setting of each interface the function spans, in interface order.</summary>
    public static IReadOnlyList<UsbInterfaceSetting> InterfacesOf(this UsbDeviceNode device, UsbFunctionNode function)
    {
        if (device.ActiveConfiguration is not { } configuration) return [];
        return [.. configuration.DefaultSettings.Where(s => function.InterfaceNumbers.Contains(s.Number)).OrderBy(s => s.Number)];
    }

    /// <summary>The function's class: its interface association's when it has one (a UVC camera is "Video", not its
    /// control interface), its first interface's otherwise, and the device's own for a non-composite device that
    /// declares one.</summary>
    public static UsbClassTriple ClassOf(this UsbDeviceNode device, UsbFunctionNode function)
    {
        var interfaces = device.InterfacesOf(function);
        if (interfaces.Count > 0 && device.ActiveConfiguration?.AssociationOf(interfaces[0].Number) is { } association)
            return new(association.Class, association.SubClass, association.Protocol);
        if (interfaces.Count > 0) return new(interfaces[0].Class, interfaces[0].SubClass, interfaces[0].Protocol);
        var d = device.Descriptor;
        return new(d.Class, d.SubClass, d.Protocol);
    }

    /// <summary>Whether any interface of the function has class <paramref name="cls"/>.</summary>
    public static bool Speaks(this UsbDeviceNode device, UsbFunctionNode function, byte cls) =>
        device.InterfacesOf(function).Any(s => s.Class == cls);
}

/// <summary>
/// What to say about a function no live tool covers: what it is, why nothing here talks to it, and what a tool for it
/// would use. Phase 2 covers storage, audio, HID and serial; this is the honest answer for the rest.
/// </summary>
public static class UsbClassGuidance
{
    public static string For(UsbClassTriple triple, UsbFunctionMode mode)
    {
        if (mode == UsbFunctionMode.NoDriver)
            return "Windows has no driver for this function, so nothing on this machine can use it yet. Installing its driver, "
                 + "or taking it over with a generic driver for raw access, would change that.";
        if (mode == UsbFunctionMode.GenericRaw)
            return "It is on a generic driver (WinUSB or libusb), so raw access reaches it: control, bulk and interrupt transfers "
                 + "with no driver change.";

        return (triple.Class, triple.SubClass, triple.Protocol) switch
        {
            (UsbClassCodes.Image, _, _) =>
                "A camera or media player speaking PTP/MTP. Windows exposes it as a portable device; a tool for it would browse "
              + "it through Windows Portable Devices.",
            (UsbClassCodes.Printer, _, _) =>
                "A printer. Print jobs belong to the Windows print system; a tool for it would read its IEEE 1284 device ID and "
              + "status through the printer class driver.",
            (UsbClassCodes.Video, _, _) or (UsbClassCodes.AudioVideo, _, _) =>
                "A camera or capture device (UVC). A tool for it would preview it through Media Foundation's capture source; its "
              + "formats and frame sizes are on the Descriptor tab.",
            (UsbClassCodes.SmartCard, _, _) =>
                "A smart-card reader (CCID). A tool for it would talk to the card through the PC/SC smart-card API.",
            (UsbClassCodes.CdcControl, 0x06 or 0x0D or 0x0E, _) or (UsbClassCodes.WirelessController, 1, 3) or (UsbClassCodes.Miscellaneous, 4, _) =>
                "A network adapter. Windows uses it as one; the Network page shows what is reachable through it.",
            (UsbClassCodes.WirelessController, 1, 1) =>
                "A Bluetooth radio. The Windows Bluetooth stack owns it; a tool for it would list the radio and its paired devices.",
            (UsbClassCodes.Billboard, _, _) =>
                "A Billboard: a Type-C device saying which alternate modes it wanted and whether it got them. Its descriptor says which.",
            (UsbClassCodes.ApplicationSpecific, 1, _) =>
                "A firmware-update (DFU) interface. Updating takes DFU requests through a generic driver — raw access.",
            (UsbClassCodes.ApplicationSpecific, 3, _) =>
                "A test-and-measurement (USBTMC) interface. A tool for it would send SCPI commands over USBTMC.",
            (UsbClassCodes.VendorSpecific, _, _) or (UsbClassCodes.Diagnostic, _, _) =>
                "Vendor-defined: there is no class to speak to it through. Raw access — control and bulk transfers through a "
              + "generic driver — is how to reach it.",
            (UsbClassCodes.CdcData, _, _) =>
                "The data half of a CDC function; its control interface says what it carries.",
            _ => "No live tool here covers this class yet.",
        };
    }
}
