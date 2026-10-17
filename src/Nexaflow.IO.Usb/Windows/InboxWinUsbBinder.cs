using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// Strategy A of ADR-001, run inside the elevated bridge: select Windows' own winusb.inf for one devnode and install
/// it with DiInstallDevice — no generated INF, no certificate, nothing added to the driver store — and undo it with
/// DiRollbackDriver. The ledger is written after every step that changes the system, so a crash at any point
/// leaves a record that <see cref="Restore"/> can finish.
/// </summary>
public static unsafe class InboxWinUsbBinder
{
    const string InterfaceGuidsValue = "DeviceInterfaceGUIDs";
    static readonly Guid UsbDeviceClass = new("88bae032-5a81-49f0-bc3d-a4ff138216d6");
    static readonly string WinUsbInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", "winusb.inf");

    public static UsbBindingRecord Bind(string instanceId, GenericUsbDriverKind kind, UsbBindingLedger ledger)
    {
        if (kind != GenericUsbDriverKind.WinUsb) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only WinUSB is installed by take-over.");
        if (UsbBindingPolicy.RefusalReason(UsbBindingCandidates.Read(instanceId)) is { } reason) throw new UsbBindingRefusedException(reason);
        if (ledger.Active(instanceId) is { } existing) throw new UsbBindingRefusedException($"Nexaflow already holds binding {existing.Id} ({existing.State}) for it — restore that first");

        uint devInst = DevNodes.Locate(instanceId)!.Value;
        var driver = DevNodes.Driver(devInst);
        var record = new UsbBindingRecord(Guid.NewGuid(), instanceId, driver.HardwareIds.FirstOrDefault() ?? instanceId,
            driver.Service, driver.InfPath, driver.InfSection, driver.MatchingDeviceId, driver.ClassGuid,
            UsbBindingRecord.InboxWinUsbStrategy, Guid.NewGuid().ToString("B"), DateTimeOffset.Now)
        { State = UsbBindingState.BindIntent };
        record.Log.Add($"driver store holds {OemInfCount()} oem*.inf");
        ledger.Save(record);

        try
        {
            WriteInterfaceGuid(devInst, record.AddedInterfaceGuid, record.Log);
            ledger.Save(record);
            Install(instanceId, record, ledger);
            record.State = Verify(instanceId, "WINUSB", UsbDeviceClass, record.Log) ? UsbBindingState.Bound : UsbBindingState.BindFailed;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            record.Log.Add($"failed: {e.Message}");
            record.State = UsbBindingState.BindFailed;
        }

        if (record.State == UsbBindingState.BindFailed)
        {
            ledger.Save(record);
            Undo(instanceId, record);
            if (Verify(instanceId, record.OriginalService, record.OriginalClassGuid, record.Log))
            {
                record.State = UsbBindingState.Restored;
                record.RestoredAt = DateTimeOffset.Now;
                record.Log.Add("bind failed and was undone; the device has its original driver");
            }
        }
        record.Log.Add($"driver store holds {OemInfCount()} oem*.inf");
        ledger.Save(record);
        return record;
    }

    /// <summary>Puts back the original driver for an interface's active record, whatever state a crash left it in.</summary>
    public static UsbBindingRecord Restore(string instanceId, UsbBindingLedger ledger)
    {
        var record = ledger.Active(instanceId) ?? throw new UsbBindingRefusedException("Nexaflow holds no binding for it");
        return Restore(record, ledger);
    }

    public static IReadOnlyList<UsbBindingRecord> RestoreAll(UsbBindingLedger ledger) =>
        ledger.Read().Where(r => r.IsActive).Select(r => Restore(r, ledger)).ToList();

    static UsbBindingRecord Restore(UsbBindingRecord record, UsbBindingLedger ledger)
    {
        if (DevNodes.Locate(record.InterfaceInstanceId) is null)
        {
            record.Log.Add("not plugged in — restore runs when it is");
            record.State = UsbBindingState.RestoreFailed;
            ledger.Save(record);
            return record;
        }

        record.State = UsbBindingState.RestoreIntent;
        ledger.Save(record);
        try
        {
            Undo(record.InterfaceInstanceId, record);
            bool restored = Verify(record.InterfaceInstanceId, record.OriginalService, record.OriginalClassGuid, record.Log);
            record.State = restored ? UsbBindingState.Restored : UsbBindingState.RestoreFailed;
            if (restored) record.RestoredAt = DateTimeOffset.Now;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            record.Log.Add($"restore failed: {e.Message}");
            record.State = UsbBindingState.RestoreFailed;
        }
        record.Log.Add($"driver store holds {OemInfCount()} oem*.inf");
        ledger.Save(record);
        return record;
    }

    static void Install(string instanceId, UsbBindingRecord record, UsbBindingLedger ledger)
    {
        var set = OpenSet(instanceId, out var data);
        try
        {
            if (!TrySelectWinUsb(set, ref data, SetupApi.CompatibleDriver, record.Log, out var driver)
                && !TrySelectWinUsb(set, ref data, SetupApi.ClassDriver, record.Log, out driver))
            {
                // winusb.inf is Class=USBDevice, so a device in any other class never sees it in its class list.
                SetClass(set, ref data, UsbDeviceClass, record.Log);
                record.ClassChanged = true;
                ledger.Save(record);
                SetupApi.SetupDiDestroyDeviceInfoList(set);
                set = OpenSet(instanceId, out data);
                if (!TrySelectWinUsb(set, ref data, SetupApi.ClassDriver, record.Log, out driver))
                    throw new InvalidOperationException("winusb.inf offers no driver node even with the device in class USBDevice");
            }

            if (!SetupApi.SetupDiSetSelectedDriver(set, ref data, ref driver)) throw LastWin32("SetupDiSetSelectedDriver");
            long started = Environment.TickCount64;
            if (!NewDev.DiInstallDevice(0, set, ref data, ref driver, NewDev.InstallNoFinishUi, out int reboot)) throw LastWin32("DiInstallDevice");
            record.Log.Add($"DiInstallDevice: ok in {Environment.TickCount64 - started} ms{(reboot != 0 ? ", reboot required" : "")}");
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Rollback first; if the original service is still not back, uninstall and rescan so PnP ranks drivers afresh.</summary>
    static void Undo(string instanceId, UsbBindingRecord record)
    {
        if (!HasService(instanceId, record.OriginalService))
        {
            var set = OpenSet(instanceId, out var data);
            try
            {
                record.Log.Add(NewDev.DiRollbackDriver(set, ref data, 0, NewDev.RollbackNoUi, out int reboot)
                    ? $"DiRollbackDriver: ok{(reboot != 0 ? ", reboot required" : "")}"
                    : $"DiRollbackDriver: {LastWin32("rollback").Message}");
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(set);
            }
        }

        if (!HasService(instanceId, record.OriginalService) && DevNodes.Locate(instanceId) is { } still)
        {
            uint? parent = DevNodes.String(still, DevPropKeys.Parent) is { } parentId ? DevNodes.Locate(parentId) : null;
            var set = OpenSet(instanceId, out var data);
            try
            {
                if (!NewDev.DiUninstallDevice(0, set, ref data, 0, out int reboot)) throw LastWin32("DiUninstallDevice");
                record.Log.Add($"DiUninstallDevice: ok{(reboot != 0 ? ", reboot required" : "")}");
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(set);
            }
            if (parent is { } p) record.Log.Add($"rescan of the parent: CR 0x{CfgMgr32.CM_Reenumerate_DevNode(p, CfgMgr32.ReenumerateSynchronous):X2}");
            for (int i = 0; i < 50 && DevNodes.Locate(instanceId) is null; i++) Thread.Sleep(100);
        }

        if (DevNodes.Locate(instanceId) is not { } devInst) return;
        if (record.ClassChanged && record.OriginalClassGuid is { } originalClass && DevNodes.Guid(devInst, DevPropKeys.ClassGuid) != originalClass)
        {
            var set = OpenSet(instanceId, out var data);
            try { SetClass(set, ref data, originalClass, record.Log); }
            finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
        }
        RemoveInterfaceGuid(devInst, record.AddedInterfaceGuid, record.Log);
    }

    static bool HasService(string instanceId, string? service) =>
        DevNodes.Locate(instanceId) is { } devInst && string.Equals(DevNodes.String(devInst, DevPropKeys.Service), service, StringComparison.OrdinalIgnoreCase);

    static bool Verify(string instanceId, string? expectedService, Guid? expectedClass, List<string> log)
    {
        if (DevNodes.Locate(instanceId) is not { } devInst)
        {
            log.Add("devnode gone");
            return false;
        }
        var service = DevNodes.String(devInst, DevPropKeys.Service);
        var classGuid = DevNodes.Guid(devInst, DevPropKeys.ClassGuid);
        uint problem = DevNodes.ProblemCode(devInst);
        log.Add($"now: service {service ?? "(none)"}, class {classGuid:B}, problem {problem}");
        return string.Equals(service, expectedService, StringComparison.OrdinalIgnoreCase) && problem == 0 && (expectedClass is null || classGuid == expectedClass);
    }

    static bool TrySelectWinUsb(nint set, ref SpDevInfoData data, uint driverType, List<string> log, out SpDrvInfoData driver)
    {
        driver = default;
        var parameters = new SpDevInstallParams { Size = (uint)sizeof(SpDevInstallParams) };
        if (!SetupApi.SetupDiGetDeviceInstallParams(set, ref data, ref parameters)) throw LastWin32("SetupDiGetDeviceInstallParams");
        parameters.Flags |= SetupApi.DiEnumSingleInf;
        parameters.FlagsEx |= SetupApi.DiFlagsExAllowExcludedDrivers;
        WinUsbInf.AsSpan().CopyTo(new Span<char>(parameters.DriverPath, 259));
        parameters.DriverPath[WinUsbInf.Length] = '\0';
        if (!SetupApi.SetupDiSetDeviceInstallParams(set, ref data, ref parameters)) throw LastWin32("SetupDiSetDeviceInstallParams");

        string list = driverType == SetupApi.ClassDriver ? "class" : "compatible";
        if (!SetupApi.SetupDiBuildDriverInfoList(set, ref data, driverType))
        {
            log.Add($"{list} list from winusb.inf: {LastWin32("SetupDiBuildDriverInfoList").Message}");
            return false;
        }

        var nodes = new List<string>();
        for (uint i = 0; ; i++)
        {
            var candidate = new SpDrvInfoData { Size = (uint)sizeof(SpDrvInfoData) };
            if (!SetupApi.SetupDiEnumDriverInfo(set, ref data, driverType, i, ref candidate)) break;
            var (inf, section) = Detail(set, ref data, ref candidate);
            nodes.Add($"{new string(candidate.Description)} [{Path.GetFileName(inf)} {section}]");
            // The plain WINUSB section; the same INF also offers Billboard and ADB nodes.
            if (driver.Size == 0 && inf.EndsWith("winusb.inf", StringComparison.OrdinalIgnoreCase) && section.Equals("WINUSB", StringComparison.OrdinalIgnoreCase))
                driver = candidate;
        }
        log.Add($"{list} list from winusb.inf, device class {data.ClassGuid:B}: {(nodes.Count == 0 ? "empty" : string.Join("; ", nodes))}");
        return driver.Size != 0;
    }

    static (string Inf, string Section) Detail(nint set, ref SpDevInfoData data, ref SpDrvInfoData driver)
    {
        // SP_DRVINFO_DETAIL_DATA_W: cbSize names the fixed part; the hardware-id tail makes the whole larger.
        var buffer = new byte[8192];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, SetupApi.DetailDataSize);
        bool ok;
        fixed (byte* p = buffer) ok = SetupApi.SetupDiGetDriverInfoDetail(set, ref data, ref driver, p, (uint)buffer.Length, out _);
        return ok ? (WideString(buffer, SetupApi.DetailInfFileOffset), WideString(buffer, SetupApi.DetailSectionOffset)) : ("", "");
    }

    static string WideString(byte[] buffer, int offset)
    {
        var chars = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(offset));
        int end = chars.IndexOf('\0');
        return (end < 0 ? chars : chars[..end]).ToString();
    }

    static void SetClass(nint set, ref SpDevInfoData data, Guid classGuid, List<string> log)
    {
        var value = Encoding.Unicode.GetBytes(classGuid.ToString("B") + "\0");
        fixed (byte* p = value)
            if (!SetupApi.SetupDiSetDeviceRegistryProperty(set, ref data, SetupApi.RegistryClassGuid, p, (uint)value.Length)) throw LastWin32("SetupDiSetDeviceRegistryProperty(CLASSGUID)");
        log.Add($"class set to {classGuid:B}");
    }

    /// <summary>winusb.sys registers a device interface only for the GUIDs it finds here — without one neither CreateFile nor libusb can reach the device.</summary>
    static void WriteInterfaceGuid(uint devInst, string guid, List<string> log)
    {
        using var key = DevNodes.HardwareKey(devInst, writable: true) ?? throw new InvalidOperationException("the devnode has no Device Parameters key");
        if (key.GetValue(InterfaceGuidsValue) is string[] existing && existing.Length > 0)
            throw new InvalidOperationException($"{InterfaceGuidsValue} is already set ({string.Join(", ", existing)}) — not overwriting another owner's value");
        key.SetValue(InterfaceGuidsValue, new[] { guid }, RegistryValueKind.MultiString);
        log.Add($"{InterfaceGuidsValue} = {guid}");
    }

    static void RemoveInterfaceGuid(uint devInst, string guid, List<string> log)
    {
        using var key = DevNodes.HardwareKey(devInst, writable: true);
        if (key?.GetValue(InterfaceGuidsValue) is string[] values && values.Contains(guid, StringComparer.OrdinalIgnoreCase))
        {
            key.DeleteValue(InterfaceGuidsValue);
            log.Add($"{InterfaceGuidsValue} removed");
        }
    }

    static nint OpenSet(string instanceId, out SpDevInfoData data)
    {
        var set = SetupApi.SetupDiCreateDeviceInfoList(0, 0);
        if (set == -1) throw LastWin32("SetupDiCreateDeviceInfoList");
        data = SpDevInfoData.Create();
        if (SetupApi.SetupDiOpenDeviceInfo(set, instanceId, 0, 0, ref data)) return set;
        var error = LastWin32("SetupDiOpenDeviceInfo");
        SetupApi.SetupDiDestroyDeviceInfoList(set);
        throw error;
    }

    static int OemInfCount() =>
        Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"), "oem*.inf").Length;

    static Win32Exception LastWin32(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{what}: Win32 {error} (0x{error:X8}) {new Win32Exception(error).Message}");
    }
}
