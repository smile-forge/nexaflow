using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Gathers a full device summary (OS, hardware, Windows security features, storage) from WMI, the
/// registry and a couple of native calls. Pure and synchronous — call <see cref="Collect"/> from a
/// background thread (it does blocking WMI work). Every probe is independently fault-tolerant, so a
/// section degrades to "—"/"Unknown" rather than failing the whole snapshot.
/// </summary>
public sealed class SystemInfoCollector
{
    public SystemInfoSnapshot Collect()
    {
        var snapshot = new SystemInfoSnapshot();
        snapshot.Sections.Add(CollectOperatingSystem());
        snapshot.Sections.Add(CollectHardware());
        snapshot.Sections.Add(CollectDisplay());
        snapshot.Sections.Add(CollectStorage());
        snapshot.Sections.Add(CollectSecurity());
        return snapshot;
    }

    // ── Operating System ──────────────────────────────────────────────────────
    private static SystemInfoSection CollectOperatingSystem()
    {
        var s  = new SystemInfoSection("Operating System", "🪟");
        var os = Wmi.First("Win32_OperatingSystem");

        s.Add("Name", os?.Str("Caption") ?? RuntimeInformation.OSDescription);

        // DisplayVersion (e.g. 24H2) lives only in the registry; build + UBR give the precise number.
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var display = cv?.GetValue("DisplayVersion") as string;
        var build   = cv?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
        var ubr     = cv?.GetValue("UBR") as int?;
        var version = display is null ? $"Build {build}" : $"{display} (Build {build}{(ubr is { } u ? $".{u}" : "")})";
        s.Add("Version", version);

        s.Add("Architecture", $"{RuntimeInformation.OSArchitecture}");
        s.Add("Computer Name", Environment.MachineName);
        s.Add("Logged-in User", $"{Environment.UserDomainName}\\{Environment.UserName}");
        s.Add("Registered Owner", cv?.GetValue("RegisteredOwner") as string);

        if (os?.CimDate("InstallDate") is { } installed)
            s.Add("Installed", installed.ToString("yyyy-MM-dd"));

        if (os?.CimDate("LastBootUpTime") is { } boot)
        {
            s.Add("Last Boot", boot.ToString("yyyy-MM-dd HH:mm"));
            s.Add("Uptime", DurationFormatter.FormatUptime(DateTime.Now - boot));
        }

        s.Add("Time Zone", TimeZoneInfo.Local.DisplayName);
        s.Add("System Locale", System.Globalization.CultureInfo.InstalledUICulture.DisplayName);
        return s;
    }

    // ── Hardware ──────────────────────────────────────────────────────────────
    private static SystemInfoSection CollectHardware()
    {
        var s    = new SystemInfoSection("Hardware", "🖥️");
        var cs   = Wmi.First("Win32_ComputerSystem");
        var bios = Wmi.First("Win32_BIOS");

        var maker = cs?.Str("Manufacturer");
        var model = cs?.Str("Model");
        s.Add("System", string.Join(" ", new[] { maker, model }.Where(x => x is not null)));

        // Motherboard (Win32_BaseBoard) — manufacturer / product / version.
        var board = Wmi.First("Win32_BaseBoard");
        s.Add("Motherboard", string.Join(" ", new[] { board?.Str("Manufacturer"), board?.Str("Product") }
                                               .Where(x => x is not null)));
        s.Add("Board Version", board?.Str("Version"));

        // A machine can report several sockets; Win32_Processor yields one row per physical CPU.
        var cpus = Wmi.Query("Win32_Processor").ToList();
        if (cpus.FirstOrDefault() is { } cpu0)
        {
            s.Add("Processor", cpu0.Str("Name"));
            var cores   = cpus.Sum(c => (int)(c.Val<uint>("NumberOfCores") ?? 0));
            var logical = cpus.Sum(c => (int)(c.Val<uint>("NumberOfLogicalProcessors") ?? 0));
            var sockets = cpus.Count;
            s.Add("Cores / Threads",
                $"{cores} cores, {(logical > 0 ? logical : Environment.ProcessorCount)} threads"
                + (sockets > 1 ? $" ({sockets} sockets)" : ""));
            if (cpu0.Val<uint>("MaxClockSpeed") is { } mhz)
                s.Add("Max Clock", $"{mhz / 1000.0:0.0} GHz");
        }
        else
        {
            s.Add("Logical Processors", Environment.ProcessorCount.ToString());
        }

        var total = cs?.Val<ulong>("TotalPhysicalMemory");
        var freeKb = Wmi.First("Win32_OperatingSystem")?.Val<ulong>("FreePhysicalMemory");
        if (total is { } t)
        {
            var used = freeKb is { } f ? $" ({SizeFormatter.FormatBytes(t - f * 1024)} in use)" : "";
            s.Add("Memory", $"{SizeFormatter.FormatBytes(t)}{used}");
        }

        // Per-DIMM detail: speed + module count (common on the original psinfo-style report).
        var dimms = Wmi.Query("Win32_PhysicalMemory").ToList();
        if (dimms.Count > 0)
        {
            var speed = dimms.Select(d => d.Val<uint>("Speed")).FirstOrDefault(v => v is not null);
            s.Add("Memory Modules", $"{dimms.Count}" + (speed is { } sp ? $" @ {sp} MHz" : ""));
        }

        s.Add("BIOS", string.Join(" ", new[] { bios?.Str("Manufacturer"), bios?.Str("SMBIOSBIOSVersion") }
                                          .Where(x => x is not null)));
        if (bios?.CimDate("ReleaseDate") is { } rel)
            s.Add("BIOS Date", rel.ToString("yyyy-MM-dd"));
        s.Add("Firmware Type", FirmwareType());
        s.Add("Serial Number", bios?.Str("SerialNumber"));
        return s;
    }

    // ── Windows security features ─────────────────────────────────────────────
    private static SystemInfoSection CollectSecurity()
    {
        var s = new SystemInfoSection("Windows Security", "🛡️");

        // Secure Boot — registry is the reliable, non-elevated source.
        var secureBoot = SecureBootState();
        s.Add("Secure Boot",
            secureBoot switch { true => "Enabled", false => "Disabled", null => "Not supported" },
            secureBoot == true ? SystemInfoStatus.Good : SystemInfoStatus.Warning);

        // Device Guard / VBS — root\Microsoft\Windows\DeviceGuard, class Win32_DeviceGuard.
        var dg = Wmi.Query(@"root\Microsoft\Windows\DeviceGuard",
                           "SELECT * FROM Win32_DeviceGuard").FirstOrDefault();
        if (dg is not null)
        {
            // VirtualizationBasedSecurityStatus: 0 off, 1 enabled-not-running, 2 enabled-and-running.
            var vbs = dg.Val<uint>("VirtualizationBasedSecurityStatus") ?? 0;
            s.Add("Virtualization-based Security (VBS)",
                vbs switch { 2 => "Running", 1 => "Enabled (not running)", _ => "Off" },
                vbs == 2 ? SystemInfoStatus.Good : SystemInfoStatus.Warning);

            var running   = dg.IntSet("SecurityServicesRunning");
            var available = dg.IntSet("AvailableSecurityProperties");

            // SecurityServices codes: 1 = Credential Guard, 2 = HVCI (memory integrity).
            s.Add("Memory Integrity (HVCI)", running.Contains(2) ? "Running" : "Off",
                  running.Contains(2) ? SystemInfoStatus.Good : SystemInfoStatus.Warning);
            s.Add("Credential Guard", running.Contains(1) ? "Running" : "Off",
                  running.Contains(1) ? SystemInfoStatus.Good : SystemInfoStatus.Neutral);

            // AvailableSecurityProperties code 3 = DMA protection capability.
            s.Add("Kernel DMA Protection", available.Contains(3) ? "Available" : "Not available",
                  available.Contains(3) ? SystemInfoStatus.Good : SystemInfoStatus.Warning);
        }
        else
        {
            s.Add("Virtualization-based Security (VBS)", "Unknown");
            s.Add("Kernel DMA Protection", "Unknown");
        }

        // TPM: Win32_Tpm requires elevation, so query the TPM Base Services (TBS) API instead — it
        // reports presence + spec version without admin. Fall back to WMI only for the enabled flag.
        var tpmVersion = TpmVersionViaTbs();
        if (tpmVersion is not null)
        {
            var enabled = Wmi.Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm")
                             .FirstOrDefault()?.Val<bool>("IsEnabled_InitialValue");
            var enabledNote = enabled switch { true => ", enabled", false => ", disabled", _ => "" };
            s.Add("TPM", $"Present (v{tpmVersion}){enabledNote}", SystemInfoStatus.Good);
        }
        else
        {
            s.Add("TPM", "Not detected", SystemInfoStatus.Warning);
        }

        return s;
    }

    // ── Storage ───────────────────────────────────────────────────────────────
    private static SystemInfoSection CollectStorage()
    {
        var s = new SystemInfoSection("Storage", "💾", width: 580);

        // Physical disks first (model + media type), then the logical volumes users recognise.
        foreach (var disk in Wmi.Query("Win32_DiskDrive"))
        {
            var model = disk.Str("Model") ?? "Disk";
            var size  = disk.Val<ulong>("Size");
            var media = NormaliseMedia(disk.Str("MediaType"));
            var detail = string.Join(", ", new[]
            {
                size is { } sz ? SizeFormatter.FormatBytes(sz) : null,
                media,
                disk.Str("InterfaceType"),
            }.Where(x => x is not null));
            s.Add(model, detail);
        }

        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            string detail;
            try
            {
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel;
                var freePct = d.TotalSize > 0 ? d.AvailableFreeSpace * 100.0 / d.TotalSize : 0;
                detail = $"{label} — {SizeFormatter.FormatBytes((ulong)d.AvailableFreeSpace)} free of {SizeFormatter.FormatBytes((ulong)d.TotalSize)} "
                       + $"({freePct:0}% free, {d.DriveFormat})";
                var status = freePct < 10 ? SystemInfoStatus.Warning : SystemInfoStatus.Neutral;
                s.Add($"Volume {d.Name.TrimEnd('\\')}", detail, status);
            }
            catch { /* a drive can drop offline mid-enumeration */ }
        }

        return s;
    }

    // ── Display (graphics + monitors) ──────────────────────────────────────────
    private static SystemInfoSection CollectDisplay()
    {
        var s = new SystemInfoSection("Display", "🖼️");

        foreach (var gpu in Wmi.Query("Win32_VideoController"))
        {
            var name = gpu.Str("Name") ?? "Display adapter";

            // Win32_VideoController.AdapterRAM is a uint32 — it wraps/caps at 4 GB on modern cards.
            // The driver's registry key holds the true size as a 64-bit value.
            var vram = VideoMemoryFromRegistry(name) ?? gpu.Val<uint>("AdapterRAM");
            var driver = gpu.Str("DriverVersion");
            var hres = gpu.Val<uint>("CurrentHorizontalResolution");
            var vres = gpu.Val<uint>("CurrentVerticalResolution");
            var hz   = gpu.Val<uint>("CurrentRefreshRate");

            var detail = string.Join(", ", new[]
            {
                vram is { } v and > 0 ? SizeFormatter.FormatBytes(v) : null,
                hres is { } w && vres is { } h && w > 0 ? $"{w}×{h}{(hz is { } r and > 0 ? $" @ {r} Hz" : "")}" : null,
                driver is not null ? $"driver {driver}" : null,
            }.Where(x => x is not null));
            s.Add(name, detail);
        }

        // Friendly monitor names live in root\wmi (WmiMonitorID) as null-terminated uint16 arrays.
        foreach (var mon in Wmi.Query(@"root\wmi", "SELECT * FROM WmiMonitorID"))
        {
            var friendly = DecodeWmiString(mon, "UserFriendlyName");
            var maker    = DecodeWmiString(mon, "ManufacturerName");
            var label    = friendly ?? "Monitor";
            s.Add($"Monitor — {label}", string.IsNullOrWhiteSpace(maker) ? "Connected" : maker);
        }

        if (s.Items.Count == 0) s.Add("Display", "No adapters detected");
        return s;
    }

    /// <summary>Decodes a WmiMonitor* uint16[] property (null-terminated char codes) to a string.</summary>
    private static string? DecodeWmiString(System.Management.ManagementObject mo, string prop)
    {
        try
        {
            if (mo[prop] is not System.Collections.IEnumerable seq) return null;
            var chars = seq.Cast<object>().Select(Convert.ToUInt16).TakeWhile(c => c != 0)
                           .Select(c => (char)c).ToArray();
            var str = new string(chars).Trim();
            return str.Length > 0 ? str : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads the accurate 64-bit VRAM size from the display driver's registry key, matched to
    /// <paramref name="adapterName"/> by its <c>DriverDesc</c>. Returns null if not found.
    /// </summary>
    private static ulong? VideoMemoryFromRegistry(string adapterName)
    {
        try
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var root = Registry.LocalMachine.OpenSubKey(classKey);
            if (root is null) return null;

            foreach (var sub in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var k = root.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") as string is not { } desc) continue;
                if (!string.Equals(desc, adapterName, StringComparison.OrdinalIgnoreCase)) continue;
                if (k.GetValue("HardwareInformation.qwMemorySize") is long size && size > 0)
                    return (ulong)size;
            }
        }
        catch { /* registry shape varies / access denied */ }
        return null;
    }

    // ── Native / registry probes ──────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;        // 1 = TPM 1.2, 2 = TPM 2.0
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, out TpmDeviceInfo info);

    /// <summary>"2.0"/"1.2" if a TPM is present (via TBS, no elevation needed), else null.</summary>
    private static string? TpmVersionViaTbs()
    {
        try
        {
            // 0 = TBS_SUCCESS; any non-zero (incl. no-device) means no usable TPM.
            if (Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), out var info) != 0)
                return null;
            return info.TpmVersion switch { 2 => "2.0", 1 => "1.2", _ => "present" };
        }
        catch { return null; }
    }

    private static bool? SecureBootState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int v) return v == 1;
            return null; // key absent → legacy BIOS / not supported
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out uint firmwareType);

    private static string FirmwareType()
    {
        try
        {
            if (GetFirmwareType(out var t))
                return t switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => "Unknown" };
        }
        catch { /* ignore */ }
        return "Unknown";
    }

    private static string? NormaliseMedia(string? media)
        => media is null ? null
         : media.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD"
         : media.Contains("Fixed", StringComparison.OrdinalIgnoreCase) ? "Fixed disk"
         : media;
}
