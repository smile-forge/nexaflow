namespace Nexaflow.IO.Usb.Binding;

/// <summary>The facts the take-over policy decides on, read from the devnode at the moment of asking.</summary>
public sealed record UsbBindingCandidate(
    string InstanceId, bool Present, string? Service, string? ClassName, string? ParentService, IReadOnlyList<string> ChildClasses);

/// <summary>
/// Which devnodes may never be taken over (spec §9.1). Enforced twice: by the client, so a refused target never
/// costs a UAC prompt, and by the bridge, which trusts nothing the client says.
/// </summary>
public static class UsbBindingPolicy
{
    static readonly Dictionary<string, string> RefusedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usbccgp"] = "it is a composite device — take over one of its interfaces, not the whole device",
        ["USBHUB3"] = "it is a hub", ["usbhub"] = "it is a hub", ["UsbXhci"] = "it is a host controller", ["usbehci"] = "it is a host controller",
        ["USBSTOR"] = "it is mass storage", ["UASPStor"] = "it is mass storage",
        ["BTHUSB"] = "it is a Bluetooth radio",
        ["WINUSB"] = "it is already on WinUSB — raw access needs no take-over",
        ["libusbK"] = "it is already on a generic driver — raw access needs no take-over",
        ["libusb0"] = "it is already on a generic driver — raw access needs no take-over",
    };

    static readonly HashSet<string> RefusedChildClasses = new(StringComparer.OrdinalIgnoreCase) { "Keyboard", "Mouse", "DiskDrive", "Volume", "CDROM" };

    /// <summary>Why this devnode must not be taken over, or null when it may be.</summary>
    public static string? RefusalReason(UsbBindingCandidate candidate)
    {
        if (!candidate.InstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return "it is not a USB device";
        if (!candidate.Present) return "it is not plugged in";
        if (candidate.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase)) return "it is a root hub";
        if (candidate.Service is { } service && RefusedServices.TryGetValue(service, out var why)) return why;
        if (string.Equals(candidate.ClassName, "Bluetooth", StringComparison.OrdinalIgnoreCase)) return "it is a Bluetooth radio";
        if (candidate.ChildClasses.FirstOrDefault(RefusedChildClasses.Contains) is { } child)
            return child is "Keyboard" or "Mouse" ? $"it is a {child.ToLowerInvariant()} in use" : "it backs a disk";
        return null;
    }
}
