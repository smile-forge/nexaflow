using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.IO.Usb.Inventory;

/// <summary>The rules that turn devnode facts into the model's verdicts. Pure, so each is pinned by a test.</summary>
public static class UsbFunctionLayout
{
    static readonly HashSet<string> GenericServices = new(StringComparer.OrdinalIgnoreCase) { "WINUSB", "libusbK", "libusb0" };

    /// <summary>Inbox class-driver services a USB function binds to. A vendor INF that names one of these still yields the class API.</summary>
    static readonly HashSet<string> ClassServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "HidUsb", "usbser", "USBSTOR", "UASPStor", "usbaudio", "usbaudio2", "usbvideo", "BTHUSB", "usbprint", "usbccid",
        "UsbNcm", "usb8023", "RNDISMP", "WpdUsb", "WUDFWpdMtp", "WUDFRd", "USBHUB3", "usbhub", "UsbXhci", "usbmbim", "MbbCx",
    };

    public static UsbFunctionMode Classify(string? service, string? infPath)
    {
        if (string.IsNullOrEmpty(service)) return UsbFunctionMode.NoDriver;
        if (service.Equals("usbccgp", StringComparison.OrdinalIgnoreCase)) return UsbFunctionMode.CompositeParent;
        if (GenericServices.Contains(service)) return UsbFunctionMode.GenericRaw;
        if (ClassServices.Contains(service)) return UsbFunctionMode.WindowsClass;
        // An inbox INF (anything not published as oemNN.inf) is Microsoft's own driver for the class.
        bool thirdParty = infPath is not null && Path.GetFileName(infPath).StartsWith("oem", StringComparison.OrdinalIgnoreCase);
        return thirdParty ? UsbFunctionMode.VendorDriver : UsbFunctionMode.WindowsClass;
    }

    /// <summary>
    /// The interfaces a function owns. A non-composite device owns every interface of its configuration; an
    /// <c>MI_xx</c> function owns interface xx, or the whole association xx belongs to — usually the one xx opens,
    /// but usbccgp may name the function after a later interface (a C930e's audio is MI_03 over IAD 2–3).
    /// </summary>
    public static IReadOnlyList<byte> InterfacesOf(byte? functionInterface, UsbConfiguration? configuration)
    {
        if (configuration is null) return functionInterface is { } only ? [only] : [];
        var numbers = configuration.DefaultSettings.Select(i => i.Number).Distinct().Order().ToList();
        if (functionInterface is not { } first) return numbers;
        if ((configuration.Associations.FirstOrDefault(a => a.FirstInterface == first) ?? configuration.AssociationOf(first)) is { } association)
            return numbers.Where(association.Covers).ToList();
        return [first];
    }

    /// <summary>The <c>xx</c> of an <c>…&amp;MI_xx\…</c> instance id.</summary>
    public static byte? FunctionInterfaceOf(string instanceId)
    {
        int at = instanceId.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && at + 6 <= instanceId.Length && byte.TryParse(instanceId.AsSpan(at + 4, 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var mi)
            ? mi : null;
    }

    /// <summary>
    /// The interface a function devnode stands for under a vendor's own composite driver, which names its functions by
    /// what they are (<c>…&amp;MODEM\8&amp;130557e0&amp;2&amp;0001</c>) rather than <c>MI_xx</c> but numbers their instances as
    /// usbccgp does, the interface last. Taken only where <paramref name="configuration"/> has that interface.
    /// </summary>
    public static byte? TrailingInterfaceOf(string instanceId, UsbConfiguration? configuration)
    {
        int at = instanceId.LastIndexOf('&');
        if (at < 0 || instanceId.Length - at - 1 != 4
            || !ushort.TryParse(instanceId.AsSpan(at + 1), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var number)
            || number > byte.MaxValue)
            return null;
        return configuration?.DefaultSettings.Any(i => i.Number == number) == true ? (byte)number : null;
    }

    /// <summary>
    /// Whether <paramref name="childIds"/> are the functions of a composite device, whatever driver split it: each a USB
    /// devnode of the device's own vendor and product (<c>USB\VID_04E8&amp;PID_6860&amp;…</c>). usbccgp is one such driver;
    /// a phone maker's is another.
    /// </summary>
    public static bool SplitsIntoFunctions(string deviceInstanceId, IEnumerable<string> childIds)
    {
        int slash = deviceInstanceId.LastIndexOf('\\');
        if (slash < 0) return false;
        string own = deviceInstanceId[..slash] + "&";
        return childIds.Any(id => id.StartsWith(own, StringComparison.OrdinalIgnoreCase));
    }
}
