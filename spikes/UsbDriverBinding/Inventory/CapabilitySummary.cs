namespace Nexaflow.Spikes.UsbDriverBinding.Inventory;

/// <summary>D7 in rules: one line per function saying what it is, who owns it, and what Nexaflow could do with it.</summary>
public static class CapabilitySummary
{
    public static IEnumerable<string> Describe(DeviceReport device)
    {
        if (device.IsHub)
        {
            yield return "Hub — inventory only, never a binding target.";
            yield break;
        }
        var interfaces = device.Configurations.FirstOrDefault()?.Interfaces.Where(i => i.Alternate == 0).ToList() ?? [];
        bool composite = device.Functions.Any(f => f.InterfaceNumber is not null);
        foreach (var function in device.Functions)
        {
            if (composite && function.InterfaceNumber is null)
            {
                // usbccgp children that are not an interface: LampArray, software components and the like
                yield return $"aux {function.Name ?? function.InstanceId}: {function.Driver.Service ?? "no service"} ({function.Driver.Class})";
                continue;
            }
            var owned = function.InterfaceNumber is { } n ? interfaces.Where(i => i.Number == n).ToList() : interfaces;
            string what = owned.Count == 0 ? "?" : string.Join(" + ", owned.Select(i => i.ClassName).Distinct());
            string label = function.InterfaceNumber is { } m ? $"MI_{m:X2}" : "device";
            yield return $"{label} {what}: {Verdict(function, owned)}";
        }
        if (device.MsOs.Os20 is not null || device.MsOs.Os10FromRegistry == true)
            yield return "Declares Microsoft OS descriptors — WinUSB-ready where it asks for it, no driver change needed.";
        if (device.MsOs.WebUsb is not null)
            yield return "Advertises WebUSB.";
        if (device.Configurations.SelectMany(c => c.Interfaces).Any(i => i.ClassName == "DFU"))
            yield return "Exposes a DFU interface — firmware read/update path exists (out of prototype scope).";
    }

    static string Verdict(FunctionNode function, List<InterfaceInfo> owned)
    {
        var service = function.Driver.Service ?? "";
        return function.Mode switch
        {
            InterfaceMode.GenericRaw => $"raw access now via {service} — control/bulk/interrupt with no driver change",
            InterfaceMode.NoDriver => $"no driver (problem {function.Driver.Problem}) — descriptors only until taken over",
            InterfaceMode.VendorDriver => $"vendor driver {service} ({function.Driver.Provider}) — vendor API only; raw needs take-over",
            InterfaceMode.WindowsClass => ClassAction(service, function, owned),
            _ => service,
        };
    }

    static string ClassAction(string service, FunctionNode function, List<InterfaceInfo> owned) => service.ToUpperInvariant() switch
    {
        "HIDUSB" => function.Children.Any(c => c.Class is "Keyboard" or "Mouse")
            ? $"HID ({string.Join(", ", function.Children.Select(c => c.Class).Distinct())}) — monitor input; never take over a pointer/keyboard"
            : "HID — decode report descriptor, monitor input, read feature reports",
        "USBSER" => $"CDC serial {function.ComPort ?? "(no COM port)"} — open a terminal",
        "USBSTOR" or "UASPSTOR" => "mass storage — browse, copy with verification",
        "USBAUDIO" or "USBAUDIO2" => owned.Any(i => i.Endpoints.Any(e => e.Direction == "In" && e.Type == "Isochronous")) ? "audio — level meter / record, test tone" : "audio — test tone",
        "USBVIDEO" => "UVC camera — preview (future adapter)",
        "BTHUSB" => "Bluetooth radio — deny-listed for take-over",
        "USBPRINT" => "printer — status / raw print (future adapter)",
        "USBCCID" => "smart card reader — PC/SC (future adapter)",
        _ => $"Windows class driver {service}",
    };
}
