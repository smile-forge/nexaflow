using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Browsing;

/// <summary>
/// The plain-language verdict (spec D7), from rules rather than the AI: one line per function saying what it is, what
/// Windows does with it and what can be done with it here, then whatever the device declares about itself. Written
/// for someone who knows what a USB device is and not what a function devnode is.
/// </summary>
public static class UsbCapabilitySummary
{
    public static IReadOnlyList<string> Describe(UsbDeviceNode device)
    {
        var lines = new List<string>();
        if (device.IsHub)
        {
            lines.Add("A USB hub: it gives the devices plugged into it their ports. Each of those devices has a page of its own; "
                    + "the hub itself has nothing to try out.");
            return lines;
        }
        // Every alternate setting, not just the default: streaming endpoints usually exist only in the others.
        var settings = device.ActiveConfiguration?.Interfaces ?? [];
        foreach (var function in device.Functions)
        {
            var owned = settings.Where(s => function.InterfaceNumbers.Contains(s.Number)).ToList();
            string what = owned.Count == 0 ? "an interface its descriptors do not describe" : string.Join(" + ", owned.Where(s => s.AlternateSetting == 0).Select(s => UsbClassCodes.Name(s.Class, s.SubClass, s.Protocol)).Distinct());
            string label = function.FunctionInterface is { } mi ? $"Interface {mi}" : "The device";
            lines.Add($"{label} ({what}): {Verdict(function, owned)}");
        }
        foreach (var aux in device.Auxiliary)
            lines.Add($"Windows also lists \u201C{aux.Name ?? aux.InstanceId}\u201D as part of it.");
        if (device.MsOs.DeclaresWinUsbReadiness)
            lines.Add("It tells Windows to use the generic USB driver, so a program can talk to it directly with nothing to install.");
        if (device.MsOs.WebUsb is not null)
            lines.Add("A web page can talk to it from a browser (it advertises WebUSB).");
        if (device.Configurations.SelectMany(c => c.Interfaces).Any(i => i is { Class: UsbClassCodes.ApplicationSpecific, SubClass: 1 }))
            lines.Add("It has a firmware-update interface (DFU).");
        if (!device.Strings.Complete && device.Power is UsbPowerState.D2 or UsbPowerState.D3)
            lines.Add("It was asleep, so its own name was not read; Read device again wakes it.");
        return lines;
    }

    static string Verdict(UsbFunctionNode function, List<UsbInterfaceSetting> owned)
    {
        var service = function.Driver.Service ?? "";
        return function.Mode switch
        {
            UsbFunctionMode.GenericRaw => $"on Windows' generic USB driver ({service}), so a program can talk to it directly.",
            UsbFunctionMode.NoDriver => function.Driver.ProblemCode == 0
                ? "Windows has no driver for it, so nothing can use it yet; only its descriptors can be read."
                : $"Windows has no working driver for it (problem {function.Driver.ProblemCode}), so nothing can use it yet; only its descriptors can be read.",
            UsbFunctionMode.VendorDriver => $"its maker's own driver ({service}{(function.Driver.Provider is { } p ? $", from {p}" : "")}) — used through the maker's software.",
            UsbFunctionMode.WindowsClass => ClassVerdict(service, function, owned),
            _ => service,
        };
    }

    static string ClassVerdict(string service, UsbFunctionNode function, List<UsbInterfaceSetting> owned) => service.ToUpperInvariant() switch
    {
        "HIDUSB" when function.Children.Any(c => c.ClassName is "Keyboard" or "Mouse") =>
            $"a {string.Join(" and ", function.Children.Select(c => c.ClassName?.ToLowerInvariant()).Where(c => c is "keyboard" or "mouse").Distinct())}. "
          + "Windows reads its input for the desktop; its report layout can be shown here.",
        "HIDUSB" => "an input device (HID): its reports can be decoded, its input watched and its settings read.",
        "USBSER" => $"a serial port{(function.ComPort is { } com ? $", {com}" : " with no COM number yet")} — open it in the serial terminal.",
        "USBSTOR" or "UASPSTOR" => "a disk: browse it and copy files to and from it, each copy checked.",
        "USBAUDIO" or "USBAUDIO2" => owned.Any(s => s.Endpoints.Any(e => e is { Direction: UsbDirection.In, Type: UsbEndpointType.Isochronous }))
            ? "a microphone or audio input: watch its levels and record from it."
            : "speakers or an audio output: play test sounds through it.",
        "USBVIDEO" => "a camera: Windows uses it as a webcam.",
        "BTHUSB" => "a Bluetooth radio: Windows' Bluetooth uses it.",
        "USBPRINT" => "a printer: Windows prints through it.",
        "USBCCID" => "a smart-card reader.",
        _ => $"Windows drives it with its {service} driver.",
    };
}
