using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Nexaflow.Spikes.UsbDriverBinding.Native;

namespace Nexaflow.Spikes.UsbDriverBinding.Binding;

/// <summary>
/// Strategy A: select Windows' own winusb.inf for one interface and install it with DiInstallDevice — no generated
/// INF, no certificate, nothing added to the driver store. Needs an elevated token; every step is logged into the record.
/// </summary>
public static unsafe class InboxWinUsbBinder
{
    static readonly Guid UsbDeviceClass = new("88bae032-5a81-49f0-bc3d-a4ff138216d6");
    static readonly string WinUsbInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", "winusb.inf");

    static readonly Dictionary<string, string> DeniedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usbccgp"] = "composite parent — take over one MI_xx interface, not the whole device",
        ["USBHUB3"] = "hub", ["usbhub"] = "hub",
        ["USBSTOR"] = "mass storage", ["UASPStor"] = "mass storage",
        ["BTHUSB"] = "Bluetooth radio",
        ["WINUSB"] = "already WinUSB — nothing to take over",
    };

    public static BindingRecord Bind(string instanceId, BindingLedger ledger)
    {
        var devInst = DevNodes.Locate(instanceId) ?? throw new InvalidOperationException($"{instanceId} is not present");
        if (Denied(instanceId, devInst) is { } reason) throw new InvalidOperationException($"refused: {reason}");
        if (ledger.Active(instanceId) is { } existing) throw new InvalidOperationException($"ledger already holds binding {existing.Id} ({existing.State}) for this interface");

        var record = new BindingRecord(Guid.NewGuid(), instanceId, DevNodes.Strings(devInst, DevPropKeys.HardwareIds).FirstOrDefault() ?? instanceId,
            DevNodes.String(devInst, DevPropKeys.Service), DevNodes.String(devInst, DevPropKeys.DriverInfPath), DevNodes.String(devInst, DevPropKeys.DriverInfSection),
            DevNodes.Guid(devInst, DevPropKeys.ClassGuid)?.ToString("B"), DevNodes.String(devInst, DevPropKeys.MatchingDeviceId),
            "InboxWinUsb", null, null, Guid.NewGuid().ToString("B"), DateTimeOffset.Now) { State = BindingState.BindIntent };
        record.Log.Add($"driver store before: {OemInfCount()} oem*.inf");
        ledger.Save(record);

        try
        {
            SetInterfaceGuid(devInst, record.AddedInterfaceGuid, record.Log);
            Install(instanceId, record);
            var now = DevNodes.Locate(instanceId);
            var service = now is { } n ? DevNodes.String(n, DevPropKeys.Service) : null;
            var problem = now is { } p ? DevNodes.Status(p)?.Problem : null;
            record.Log.Add($"after install: service {service ?? "(none)"}, problem {problem}, class {(now is { } c ? DevNodes.String(c, DevPropKeys.Class) : "?")}");
            record.State = string.Equals(service, "WINUSB", StringComparison.OrdinalIgnoreCase) && problem == 0 ? BindingState.Bound : BindingState.BindFailed;
        }
        catch (Exception e)
        {
            record.Log.Add($"FAILED: {e.Message}");
            record.State = BindingState.BindFailed;
        }

        if (record.State == BindingState.BindFailed)
            Undo(instanceId, record);
        record.Log.Add($"driver store after: {OemInfCount()} oem*.inf");
        ledger.Save(record);
        return record;
    }

    public static BindingRecord Restore(string instanceId, BindingLedger ledger)
    {
        var record = ledger.Active(instanceId) ?? throw new InvalidOperationException("no active binding in the ledger for this interface");
        record.State = BindingState.RestoreIntent;
        ledger.Save(record);
        try
        {
            Undo(instanceId, record);
            var devInst = DevNodes.Locate(instanceId);
            var service = devInst is { } d ? DevNodes.String(d, DevPropKeys.Service) : null;
            var problem = devInst is { } p ? DevNodes.Status(p)?.Problem : null;
            record.Log.Add($"after restore: service {service ?? "(none)"} (original {record.OriginalService}), problem {problem}, class {(devInst is { } c ? DevNodes.Guid(c, DevPropKeys.ClassGuid)?.ToString("B") : "?")} (original {record.OriginalClassGuid})");
            record.Log.Add($"driver store after restore: {OemInfCount()} oem*.inf");
            record.State = string.Equals(service, record.OriginalService, StringComparison.OrdinalIgnoreCase) && problem == 0 ? BindingState.Restored : BindingState.RestoreFailed;
        }
        catch (Exception e)
        {
            record.Log.Add($"RESTORE FAILED: {e.Message}");
            record.State = BindingState.RestoreFailed;
        }
        ledger.Save(record);
        return record;
    }

    static string? Denied(string instanceId, uint devInst)
    {
        if (!instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return "not a USB devnode";
        if (instanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase)) return "root hub";
        if (DevNodes.String(devInst, DevPropKeys.Service) is { } service && DeniedServices.TryGetValue(service, out var why)) return why;
        foreach (var child in DevNodes.Strings(devInst, DevPropKeys.Children))
            if (DevNodes.Locate(child) is { } c && DevNodes.String(c, DevPropKeys.Class) is "Keyboard" or "Mouse" or "DiskDrive")
                return $"backs a {DevNodes.String(c, DevPropKeys.Class)} ({child})";
        return null;
    }

    static void Install(string instanceId, BindingRecord record)
    {
        var set = OpenSet(instanceId, out var data);
        try
        {
            if (!TrySelectWinUsb(set, ref data, SetupApi.CompatibleDriver, record.Log, out var driver)
                && !TrySelectWinUsb(set, ref data, SetupApi.ClassDriver, record.Log, out driver))
            {
                // winusb.inf is Class=USBDevice: a device of another class never sees it in its class list.
                SetClass(set, ref data, UsbDeviceClass, record.Log);
                record.ClassChanged = true;
                SetupApi.SetupDiDestroyDeviceInfoList(set);
                set = OpenSet(instanceId, out data);
                if (!TrySelectWinUsb(set, ref data, SetupApi.ClassDriver, record.Log, out driver))
                    throw new InvalidOperationException("winusb.inf offers no driver node even with the device in class USBDevice");
            }

            if (!SetupApi.SetupDiSetSelectedDriver(set, ref data, ref driver)) throw Win32("SetupDiSetSelectedDriver");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!NewDev.DiInstallDevice(0, set, ref data, ref driver, NewDev.InstallNoFinishUi, out int reboot)) throw Win32("DiInstallDevice");
            record.Log.Add($"DiInstallDevice: ok in {clock.ElapsedMilliseconds} ms, reboot needed: {reboot != 0}");
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Puts the original driver back: rollback first, then uninstall + rescan so PnP ranks drivers afresh.</summary>
    static void Undo(string instanceId, BindingRecord record)
    {
        if (DevNodes.Locate(instanceId) is { } current && !string.Equals(DevNodes.String(current, DevPropKeys.Service), record.OriginalService, StringComparison.OrdinalIgnoreCase))
        {
            var set = OpenSet(instanceId, out var data);
            try
            {
                bool rolledBack = NewDev.DiRollbackDriver(set, ref data, 0, NewDev.RollbackNoUi, out int reboot);
                record.Log.Add(rolledBack ? $"DiRollbackDriver: ok, reboot needed: {reboot != 0}" : $"DiRollbackDriver: {Win32("").Message}");
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(set);
            }
        }

        if (DevNodes.Locate(instanceId) is { } still && !string.Equals(DevNodes.String(still, DevPropKeys.Service), record.OriginalService, StringComparison.OrdinalIgnoreCase))
        {
            var parent = DevNodes.String(still, DevPropKeys.Parent) is { } parentId ? DevNodes.Locate(parentId) : null;
            var set = OpenSet(instanceId, out var data);
            try
            {
                if (!NewDev.DiUninstallDevice(0, set, ref data, 0, out int reboot)) throw Win32("DiUninstallDevice");
                record.Log.Add($"DiUninstallDevice: ok, reboot needed: {reboot != 0}");
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(set);
            }
            if (parent is { } p) record.Log.Add($"CM_Reenumerate_DevNode(parent): CR 0x{CfgMgr32.CM_Reenumerate_DevNode(p, 0x1):X2}");
            for (int i = 0; i < 50 && DevNodes.Locate(instanceId) is null; i++) Thread.Sleep(100);
        }

        if (DevNodes.Locate(instanceId) is { } devInst)
            ClearInterfaceGuid(devInst, record.AddedInterfaceGuid, record.Log);
    }

    static bool TrySelectWinUsb(nint set, ref SpDevInfoData data, uint driverType, List<string> log, out SpDrvInfoData driver)
    {
        driver = default;
        var parameters = new SpDevInstallParams { Size = (uint)sizeof(SpDevInstallParams) };
        if (!SetupApi.SetupDiGetDeviceInstallParams(set, ref data, ref parameters)) throw Win32("SetupDiGetDeviceInstallParams");
        parameters.Flags |= SetupApi.DiEnumSingleInf;
        parameters.FlagsEx |= SetupApi.DiFlagsExAllowExcludedDrivers;
        var path = WinUsbInf.AsSpan();
        path.CopyTo(new Span<char>(parameters.DriverPath, 259));
        parameters.DriverPath[path.Length] = '\0';
        if (!SetupApi.SetupDiSetDeviceInstallParams(set, ref data, ref parameters)) throw Win32("SetupDiSetDeviceInstallParams");

        string kind = driverType == SetupApi.ClassDriver ? "class" : "compatible";
        if (!SetupApi.SetupDiBuildDriverInfoList(set, ref data, driverType))
        {
            log.Add($"{kind} list from winusb.inf: {Win32("SetupDiBuildDriverInfoList").Message}");
            return false;
        }

        var nodes = new List<string>();
        for (uint i = 0; ; i++)
        {
            var candidate = new SpDrvInfoData { Size = (uint)sizeof(SpDrvInfoData) };
            if (!SetupApi.SetupDiEnumDriverInfo(set, ref data, driverType, i, ref candidate)) break;
            var (inf, section) = Detail(set, ref data, ref candidate);
            nodes.Add($"{new string(candidate.Description)} [{Path.GetFileName(inf)} {section}]");
            if (driver.Size == 0 && inf.EndsWith("winusb.inf", StringComparison.OrdinalIgnoreCase)) driver = candidate;
        }
        log.Add($"{kind} list from winusb.inf (device class {data.ClassGuid:B}): {(nodes.Count == 0 ? "empty" : string.Join("; ", nodes))}");
        return driver.Size != 0;
    }

    static (string Inf, string Section) Detail(nint set, ref SpDevInfoData data, ref SpDrvInfoData driver)
    {
        var buffer = new byte[8192];
        fixed (byte* p = buffer)
        {
            *(uint*)p = SetupApi.DetailDataSize;
            if (!SetupApi.SetupDiGetDriverInfoDetail(set, ref data, ref driver, p, (uint)buffer.Length, out _)) return ("?", "?");
            return (new string((char*)(p + SetupApi.DetailInfFileOffset)), new string((char*)(p + SetupApi.DetailSectionOffset)));
        }
    }

    static void SetClass(nint set, ref SpDevInfoData data, Guid classGuid, List<string> log)
    {
        var value = Encoding.Unicode.GetBytes(classGuid.ToString("B") + "\0");
        fixed (byte* p = value)
            if (!SetupApi.SetupDiSetDeviceRegistryProperty(set, ref data, SetupApi.RegistryClassGuid, p, (uint)value.Length)) throw Win32("SetupDiSetDeviceRegistryProperty(CLASSGUID)");
        log.Add($"class GUID set to {classGuid:B} so winusb.inf's class list applies");
    }

    /// <summary>winusb.sys registers a device interface only for the GUIDs it finds here; libusb and CreateFile need one.</summary>
    static void SetInterfaceGuid(uint devInst, string guid, List<string> log)
    {
        using var key = HardwareKey(devInst);
        key.SetValue("DeviceInterfaceGUIDs", new[] { guid }, RegistryValueKind.MultiString);
        log.Add($"DeviceInterfaceGUIDs = {guid}");
    }

    static void ClearInterfaceGuid(uint devInst, string guid, List<string> log)
    {
        using var key = HardwareKey(devInst);
        if (key.GetValue("DeviceInterfaceGUIDs") is string[] values && values.Contains(guid, StringComparer.OrdinalIgnoreCase))
        {
            key.DeleteValue("DeviceInterfaceGUIDs");
            log.Add("DeviceInterfaceGUIDs removed");
        }
    }

    static RegistryKey HardwareKey(uint devInst)
    {
        const uint keyReadWrite = 0x2001F;
        uint cr = CfgMgr32.CM_Open_DevNode_Key(devInst, keyReadWrite, 0, 0, out nint handle, 0);
        if (cr != CfgMgr32.CrSuccess) throw new InvalidOperationException($"CM_Open_DevNode_Key: CR 0x{cr:X2}");
        return RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
    }

    static nint OpenSet(string instanceId, out SpDevInfoData data)
    {
        var set = SetupApi.SetupDiCreateDeviceInfoList(0, 0);
        if (set == -1) throw Win32("SetupDiCreateDeviceInfoList");
        data = SpDevInfoData.Create();
        if (!SetupApi.SetupDiOpenDeviceInfo(set, instanceId, 0, 0, ref data))
        {
            var error = Win32("SetupDiOpenDeviceInfo");
            SetupApi.SetupDiDestroyDeviceInfoList(set);
            throw error;
        }
        return set;
    }

    public static int OemInfCount() =>
        Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"), "oem*.inf").Length;

    static Win32Exception Win32(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{what}: Win32 {error} (0x{error:X8}) {new Win32Exception(error).Message}");
    }
}
