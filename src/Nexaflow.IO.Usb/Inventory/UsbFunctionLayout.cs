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
}
