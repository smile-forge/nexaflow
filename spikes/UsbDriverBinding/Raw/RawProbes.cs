using System.Buffers.Binary;
using System.Diagnostics;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Nexaflow.Spikes.UsbDriverBinding.Inventory;
using Nexaflow.Spikes.UsbDriverBinding.Native;

namespace Nexaflow.Spikes.UsbDriverBinding.Raw;

public sealed record RawProbeResult(string Backend, string Device, string Target, bool Opened, bool TransferOk, List<string> Steps, double ElapsedMs);

/// <summary>
/// Strategy D: open what is already generic (WinUSB) without changing anything, through both candidate
/// transports. Only standard requests leave the host: GET_DESCRIPTOR(device) and GET_STATUS(device).
/// </summary>
public static unsafe class WinUsbDirect
{
    public static RawProbeResult Probe(DeviceReport device, FunctionNode function)
    {
        var clock = Stopwatch.StartNew();
        var steps = new List<string>();
        string label = $"{device.Vid}:{device.Pid} {device.Strings["product"]}";
        if (function.DeviceInterfaceGuids.Length == 0)
        {
            steps.Add("no DeviceInterfaceGUIDs under Device Parameters — winusb.sys registers no interface, nothing to CreateFile");
            return new("WinUSB (P/Invoke)", label, function.InstanceId, false, false, steps, clock.Elapsed.TotalMilliseconds);
        }

        foreach (var guidText in function.DeviceInterfaceGuids)
        {
            var paths = DevNodes.Interfaces(Guid.Parse(guidText), function.InstanceId);
            steps.Add($"interface {guidText}: {paths.Count} path(s)");
            foreach (var path in paths)
            {
                using var file = Kernel32.CreateFile(path, Kernel32.GenericRead | Kernel32.GenericWrite, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, Kernel32.FileFlagOverlapped, 0);
                if (file.IsInvalid)
                {
                    steps.Add(HubWalker.Win32Error("CreateFile"));
                    continue;
                }
                if (!WinUsb.WinUsb_Initialize(file, out nint handle))
                {
                    steps.Add(HubWalker.Win32Error("WinUsb_Initialize"));
                    continue;
                }
                try
                {
                    bool ok = Exercise(handle, device, steps);
                    return new("WinUSB (P/Invoke)", label, function.InstanceId, true, ok, steps, clock.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    WinUsb.WinUsb_Free(handle);
                }
            }
        }
        return new("WinUSB (P/Invoke)", label, function.InstanceId, false, false, steps, clock.Elapsed.TotalMilliseconds);
    }

    static bool Exercise(nint handle, DeviceReport device, List<string> steps)
    {
        byte speed = 0;
        uint length = 1;
        if (WinUsb.WinUsb_QueryDeviceInformation(handle, 1, ref length, &speed))
            steps.Add($"device speed code {speed} (1 low/full, 3 high+)");

        if (WinUsb.WinUsb_QueryInterfaceSettings(handle, 0, out var iface))
        {
            var pipes = new List<string>();
            for (byte i = 0; i < iface.NumEndpoints; i++)
                if (WinUsb.WinUsb_QueryPipe(handle, 0, i, out var pipe))
                    pipes.Add($"0x{pipe.PipeId:X2} {(pipe.PipeType switch { 1 => "iso", 2 => "bulk", 3 => "interrupt", _ => "control" })} {pipe.MaximumPacketSize}");
            steps.Add($"interface {iface.InterfaceNumber} class 0x{iface.InterfaceClass:X2}, pipes: {(pipes.Count == 0 ? "none" : string.Join(", ", pipes))}");
        }

        var descriptor = new byte[18];
        bool descriptorOk;
        fixed (byte* p = descriptor)
            descriptorOk = WinUsb.WinUsb_GetDescriptor(handle, 0x01, 0, 0, p, 18, out uint got) && got == 18;
        if (!descriptorOk) steps.Add(HubWalker.Win32Error("WinUsb_GetDescriptor(device)"));
        else
        {
            bool same = descriptor.AsSpan().SequenceEqual(Convert.FromHexString(device.RawDescriptorsHex["device"].Replace(" ", "")));
            steps.Add(same ? "GET_DESCRIPTOR(device): 18 bytes, identical to hub IOCTL copy"
                : $"GET_DESCRIPTOR(device): 18 bytes {DescriptorParser.Hex(descriptor)} — hub IOCTL copy is {device.RawDescriptorsHex["device"]}");
        }

        var status = new byte[2];
        var setup = new WinUsbSetupPacket { RequestType = 0x80, Request = 0x00, Value = 0, Index = 0, Length = 2 };
        bool statusOk;
        fixed (byte* p = status)
            statusOk = WinUsb.WinUsb_ControlTransfer(handle, setup, p, 2, out uint got, 0) && got == 2;
        steps.Add(statusOk ? $"GET_STATUS(device): 0x{BinaryPrimitives.ReadUInt16LittleEndian(status):X4}" : HubWalker.Win32Error("WinUsb_ControlTransfer(GET_STATUS)"));
        return descriptorOk && statusOk;
    }
}

public static class LibUsbProbe
{
    public sealed record Seen(string VidPid, byte Bus, string Ports, string Speed);

    public static (List<Seen> Enumerated, List<RawProbeResult> Results) Probe(IReadOnlyList<DeviceReport> devices, bool verbose)
    {
        var results = new List<RawProbeResult>();
        var seen = new List<Seen>();
        using var context = new UsbContext();
        if (verbose) context.SetDebugLevel(LogLevel.Info);
        var list = context.List();
        var libusbDevices = new List<UsbDevice>();
        foreach (var d in list.OfType<UsbDevice>())
        {
            // LibUsbDotNet's VendorId reads and parses every config descriptor; one unreadable device throws here.
            try
            {
                seen.Add(new($"0x{d.VendorId:X4}:0x{d.ProductId:X4}", d.BusNumber, string.Join('.', d.PortNumbers), d.Speed.ToString()));
                libusbDevices.Add(d);
            }
            catch (Exception e)
            {
                seen.Add(new($"unreadable ({e.Message})", d.BusNumber, string.Join('.', d.PortNumbers), d.Speed.ToString()));
            }
        }

        foreach (var device in devices.Where(d => !d.IsHub))
        {
            var ports = device.PortPath[(device.PortPath.LastIndexOf('-') + 1)..];
            var match = libusbDevices.FirstOrDefault(d => $"0x{d.VendorId:X4}" == device.Vid && $"0x{d.ProductId:X4}" == device.Pid && string.Join('.', d.PortNumbers) == ports)
                ?? libusbDevices.FirstOrDefault(d => $"0x{d.VendorId:X4}" == device.Vid && $"0x{d.ProductId:X4}" == device.Pid);
            string label = $"{device.Vid}:{device.Pid} {device.Strings["product"]}";
            if (match is null)
            {
                results.Add(new("libusb-1.0", label, device.InstanceId, false, false, ["not enumerated by libusb"], 0));
                continue;
            }
            var generic = device.Functions.Where(f => f.Mode == InterfaceMode.GenericRaw).ToList();
            results.Add(Exercise(match, label, device, generic));
        }
        return (seen, results);
    }

    static RawProbeResult Exercise(UsbDevice usb, string label, DeviceReport device, List<FunctionNode> generic)
    {
        var clock = Stopwatch.StartNew();
        var steps = new List<string>();
        var modes = string.Join(", ", device.Functions.Select(f => $"{(f.InterfaceNumber is { } n ? $"MI_{n:X2}" : "dev")}={f.Driver.Service ?? "none"}"));
        steps.Add($"drivers: {modes}");
        try
        {
            usb.Open();
        }
        catch (Exception e)
        {
            steps.Add($"open: {e.GetType().Name}: {e.Message}");
            return new("libusb-1.0", label, device.InstanceId, false, false, steps, clock.Elapsed.TotalMilliseconds);
        }

        bool transferOk = false;
        try
        {
            steps.Add("open: ok");
            transferOk = Transfer(usb, steps, "GET_DESCRIPTOR(device)", new UsbSetupPacket(0x80, 0x06, 0x0100, 0, 18), 18);
            foreach (var function in generic)
            {
                int number = function.InterfaceNumber ?? 0;
                bool claimed = usb.ClaimInterface(number);
                steps.Add($"claim interface {number} ({function.Driver.Service}): {(claimed ? "ok" : "failed")}");
                if (!claimed) continue;
                transferOk &= Transfer(usb, steps, $"GET_STATUS via interface {number}", new UsbSetupPacket(0x80, 0x00, 0, 0, 2), 2);
                usb.ReleaseInterface(number);
            }
        }
        finally
        {
            usb.Close();
        }
        return new("libusb-1.0", label, device.InstanceId, true, transferOk, steps, clock.Elapsed.TotalMilliseconds);
    }

    static bool Transfer(UsbDevice usb, List<string> steps, string what, UsbSetupPacket setup, int length)
    {
        try
        {
            var buffer = new byte[length];
            int got = usb.ControlTransfer(setup, buffer, 0, length);
            steps.Add($"{what}: {got} bytes {DescriptorParser.Hex(buffer.AsSpan(0, Math.Max(0, got)))}");
            return got == length;
        }
        catch (Exception e)
        {
            steps.Add($"{what}: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }
}
