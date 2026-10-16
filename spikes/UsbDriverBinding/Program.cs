using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Spikes.UsbDriverBinding;
using Nexaflow.Spikes.UsbDriverBinding.Binding;
using Nexaflow.Spikes.UsbDriverBinding.Inventory;
using Nexaflow.Spikes.UsbDriverBinding.Raw;

var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
string outDir = Option("--out") ?? Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(outDir);

void Save(string name, object value)
{
    var path = Path.Combine(outDir, name);
    File.WriteAllText(path, JsonSerializer.Serialize(value, options));
    Console.WriteLine($"  → {path}");
}

switch (args.FirstOrDefault())
{
    case "env":
        Save("env.json", EnvironmentProbe.Capture());
        break;

    case "inventory":
    case "raw":
    case "all":
    {
        var env = EnvironmentProbe.Capture();
        Console.WriteLine($"{env.Windows} | elevated={env.Elevated} ({env.TokenElevationType}) | SecureBoot={env.SecureBoot} HVCI={env.MemoryIntegrity} SAC={env.SmartAppControl}");
        Save("env.json", env);

        var names = Option("--usb-ids") is { } idsPath ? UsbIds.Load(idsPath) : null;
        var clock = Stopwatch.StartNew();
        var inventory = DeviceInventory.Capture(names);
        Console.WriteLine($"\ninventory: {inventory.Hubs.Count} hubs, {inventory.Devices.Count} devices in {clock.ElapsedMilliseconds} ms (unprivileged)");
        foreach (var hub in inventory.Hubs.Where(h => h.OpenError is not null)) Console.WriteLine($"  hub {hub.InstanceId}: {hub.OpenError}");
        foreach (var u in inventory.Unmatched) Console.WriteLine($"  unmatched: {u}");
        foreach (var d in inventory.Devices)
        {
            Console.WriteLine($"\n[{d.PortPath}] {d.Vid}:{d.Pid} {d.Strings["manufacturer"]} / {d.Strings["product"]}  ({d.VendorName ?? "-"} / {d.ProductName ?? "-"})");
            Console.WriteLine($"   power {d.PowerState} [{string.Join(' ', d.Functions.Select(f => $"{(f.InterfaceNumber is { } n ? $"MI_{n:X2}" : "dev")}={f.PowerState}"))}]");
            Console.WriteLine($"   USB {d.Descriptor.BcdUsb} {d.Speed}{(d.SuperSpeedCapable == true && !d.Speed.StartsWith("Super") ? " (SuperSpeed-capable, running slower)" : "")}, {d.Configurations.FirstOrDefault()?.MaxPowerMa} mA, descriptors read in {d.DescriptorReadMs:F1} ms");
            foreach (var line in d.Summary) Console.WriteLine($"   - {line}");
            foreach (var h in d.HidReports) Console.WriteLine($"   HID report descriptor via hub IOCTL, interface {h.Interface}: {h.Outcome}");
            foreach (var e in d.Errors) Console.WriteLine($"   ! {e}");
        }
        Save("inventory.json", inventory);
        if (args[0] == "inventory") break;

        Console.WriteLine("\n--- Strategy D: open what is already generic, no driver change ---");
        var raw = new List<RawProbeResult>();
        foreach (var device in inventory.Devices)
            foreach (var function in device.Functions.Where(f => f.Mode == InterfaceMode.GenericRaw))
                raw.Add(WinUsbDirect.Probe(device, function));

        var (seen, libusb) = LibUsbProbe.Probe(inventory.Devices, args.Contains("--libusb-debug"));
        raw.AddRange(libusb);
        foreach (var r in raw)
        {
            Console.WriteLine($"\n{r.Backend} | {r.Device} | opened={r.Opened} transfers={r.TransferOk} ({r.ElapsedMs:F1} ms)");
            foreach (var s in r.Steps) Console.WriteLine($"   {s}");
        }
        Save("raw.json", new { libusbEnumerated = seen, results = raw });
        break;
    }

    case "bind-inbox":
    case "restore":
    {
        string instanceId = args.Length > 1 ? args[1] : throw new ArgumentException("instance id required");
        var ledger = new BindingLedger(Option("--ledger") ?? BindingLedger.DefaultPath);
        string resultFile = Path.Combine(outDir, $"{args[0]}-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        if (!EnvironmentProbe.Capture().Elevated)
        {
            // P1: one runas per operation, as Nexaflow's PrivilegeBridge does.
            var clock = Stopwatch.StartNew();
            var child = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var a in new[] { args[0], instanceId, "--out", outDir, "--result", resultFile, "--ledger", ledger.FilePath }) child.ArgumentList.Add(a);
            try
            {
                using var process = Process.Start(child)!;
                process.WaitForExit();
                Console.WriteLine($"elevated child exited {process.ExitCode} after {clock.ElapsedMilliseconds} ms (includes the UAC prompt)");
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                Console.WriteLine("UAC declined — nothing changed.");
                return 1;
            }
            if (File.Exists(resultFile)) Console.WriteLine(File.ReadAllText(resultFile));

            Console.WriteLine("\n--- verification from the unelevated token ---");
            var inventory = DeviceInventory.Capture(null);
            var device = inventory.Devices.FirstOrDefault(d => d.Functions.Any(f => string.Equals(f.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)));
            if (device is null)
            {
                Console.WriteLine("device not found after the operation");
                return 1;
            }
            foreach (var f in device.Functions)
                Console.WriteLine($"   {f.InstanceId}: {f.Driver.Service ?? "(none)"} problem {f.Driver.Problem} {f.Mode} {f.ComPort} {string.Join(',', f.DeviceInterfaceGuids)}");
            var target = device.Functions.First(f => string.Equals(f.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            var checks = new List<RawProbeResult>();
            if (target.Mode == InterfaceMode.GenericRaw) checks.Add(WinUsbDirect.Probe(device, target));
            checks.AddRange(LibUsbProbe.Probe([device], false).Results);
            foreach (var r in checks)
            {
                Console.WriteLine($"   {r.Backend}: opened={r.Opened} transfers={r.TransferOk}");
                foreach (var s in r.Steps) Console.WriteLine($"      {s}");
            }
            Save($"{args[0]}-verify-{DateTime.Now:yyyyMMdd-HHmmss}.json", new { device, checks });
            return 0;
        }

        BindingRecord record;
        try
        {
            record = args[0] == "bind-inbox" ? InboxWinUsbBinder.Bind(instanceId, ledger) : InboxWinUsbBinder.Restore(instanceId, ledger);
        }
        catch (Exception e)
        {
            File.WriteAllText(Option("--result") ?? resultFile, JsonSerializer.Serialize(new { error = e.Message }, options));
            return 2;
        }
        File.WriteAllText(Option("--result") ?? resultFile, JsonSerializer.Serialize(record, options));
        return record.State is BindingState.Bound or BindingState.Restored ? 0 : 3;
    }

    case "bindings":
        foreach (var r in new BindingLedger(Option("--ledger") ?? BindingLedger.DefaultPath).Read())
            Console.WriteLine($"{r.BoundAt:u} {r.State,-14} {r.InterfaceInstanceId} (was {r.OriginalService}, {r.Strategy})");
        break;

    default:
        Console.WriteLine("""
            UsbDriverBinding spike — Phase 0 of docs/usb-probe-spec.md

              env                      Windows build, elevation, Secure Boot, HVCI, Smart App Control, generic drivers present
              inventory [--usb-ids f]  unprivileged hub-IOCTL + CfgMgr32 inventory → inventory.json
              all [--usb-ids f]        inventory, then Strategy D raw-open through WinUSB P/Invoke and libusb-1.0 → raw.json
                  [--libusb-debug]     libusb's own log on stderr
              bind-inbox <instanceId>  Strategy A: force-select inbox winusb.inf for one MI_xx interface (one UAC prompt)
              restore <instanceId>     put the original driver back from the ledger (one UAC prompt)
              bindings                 list the ledger (%ProgramData%\Nexaflow\Devices\usb-bindings.spike.json)
              --out <dir>              where the JSON goes (default: ./results next to the exe)
            """);
        break;
}
return 0;
