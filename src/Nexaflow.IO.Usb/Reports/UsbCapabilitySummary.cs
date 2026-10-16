using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Reports;

/// <summary>
/// The plain-English verdict (spec D7), from rules rather than the AI: one line per function saying what it is,
/// who owns it and what can be done with it, then whatever the device declares about itself.
/// </summary>
public static class UsbCapabilitySummary
{
    public static IReadOnlyList<string> Describe(UsbDeviceNode device)
    {
        var lines = new List<string>();
        if (device.IsHub)
        {
            lines.Add("Hub — part of the tree, never a take-over target.");
            return lines;
        }
        // Every alternate setting, not just the default: streaming endpoints usually exist only in the others.
        var settings = device.ActiveConfiguration?.Interfaces ?? [];
        foreach (var function in device.Functions)
        {
            var owned = settings.Where(s => function.InterfaceNumbers.Contains(s.Number)).ToList();
            string what = owned.Count == 0 ? "unknown interface" : string.Join(" + ", owned.Where(s => s.AlternateSetting == 0).Select(s => UsbClassCodes.Name(s.Class, s.SubClass, s.Protocol)).Distinct());
            string label = function.FunctionInterface is { } mi ? $"Interface {mi}" : "Device";
            lines.Add($"{label} — {what}: {Verdict(function, owned)}");
        }
        foreach (var aux in device.Auxiliary)
            lines.Add($"Also exposes {aux.Name ?? aux.InstanceId} ({aux.ClassName ?? "no class"}).");
        if (device.MsOs.DeclaresWinUsbReadiness)
            lines.Add("Declares Microsoft OS descriptors — WinUSB-ready: raw access needs no driver change.");
        if (device.MsOs.WebUsb is not null)
            lines.Add("Advertises WebUSB.");
        if (device.Configurations.SelectMany(c => c.Interfaces).Any(i => i is { Class: UsbClassCodes.ApplicationSpecific, SubClass: 1 }))
            lines.Add("Has a DFU interface — a firmware read/update path exists.");
        if (!device.Strings.Complete && device.Power is UsbPowerState.D2 or UsbPowerState.D3)
            lines.Add("Asleep — its own strings were not read; look again to wake it.");
        return lines;
    }

    static string Verdict(UsbFunctionNode function, List<UsbInterfaceSetting> owned)
    {
        var service = function.Driver.Service ?? "";
        return function.Mode switch
        {
            UsbFunctionMode.GenericRaw => $"raw access now through {service} — control, bulk and interrupt transfers with no driver change.",
            UsbFunctionMode.NoDriver => function.Driver.ProblemCode == 0
                ? "no driver — descriptors only until taken over."
                : $"no driver (problem {function.Driver.ProblemCode}) — descriptors only until taken over.",
            UsbFunctionMode.VendorDriver => $"vendor driver {service}{(function.Driver.Provider is { } p ? $" from {p}" : "")} — its own API only; raw access needs a take-over.",
            UsbFunctionMode.WindowsClass => ClassVerdict(service, function, owned),
            _ => service,
        };
    }

    static string ClassVerdict(string service, UsbFunctionNode function, List<UsbInterfaceSetting> owned) => service.ToUpperInvariant() switch
    {
        "HIDUSB" when function.Children.Any(c => c.ClassName is "Keyboard" or "Mouse") =>
            $"HID ({string.Join(", ", function.Children.Select(c => c.ClassName).OfType<string>().Distinct())}) — watch its input; never taken over while it is a keyboard or pointer.",
        "HIDUSB" => "HID — decode its reports, watch input, read feature reports.",
        "USBSER" => $"serial port {function.ComPort ?? "(no COM port assigned)"} — open a terminal.",
        "USBSTOR" or "UASPSTOR" => "mass storage — browse and copy files with verification.",
        "USBAUDIO" or "USBAUDIO2" => owned.Any(s => s.Endpoints.Any(e => e is { Direction: UsbDirection.In, Type: UsbEndpointType.Isochronous }))
            ? "audio input — level meter and recording."
            : "audio — play a test tone.",
        "USBVIDEO" => "camera (UVC) — preview.",
        "BTHUSB" => "Bluetooth radio — never a take-over target.",
        "USBPRINT" => "printer — status and raw print.",
        "USBCCID" => "smart-card reader — PC/SC.",
        _ => $"Windows class driver {service}.",
    };
}
