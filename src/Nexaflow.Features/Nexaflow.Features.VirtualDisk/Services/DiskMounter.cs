using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>Outcome of a mount attempt.</summary>
public readonly record struct MountOutcome(bool Success, string? DriveLetter, string? Error)
{
    public static MountOutcome Ok(string? drive) => new(true, drive, null);
    public static MountOutcome Failed(string? error) => new(false, null, error);
}

/// <summary>
/// Mounts and unmounts disk images through the built-in Windows Virtual Disk Service — no third-party driver —
/// and answers "is this drive backed by a mounted image?" via the Storage cmdlets.
/// <para>
/// Everything goes through the Storage PowerShell cmdlets, which handle attach + drive-letter assignment in one
/// robust step and — crucially — map a drive back to its image correctly for <b>every</b> format. (A mounted
/// ISO surfaces as a CD-ROM volume with <c>MSFT_DiskImage.Number = 0</c> and no <c>MSFT_Partition</c> row, so
/// the WMI disk-number join both misses the ISO's real drive and falsely matches the system disk — hence
/// <c>Get-Volume | Get-DiskImage</c> instead.) The image path is passed via an environment variable, never
/// interpolated into the script, so it can't inject PowerShell.
/// </para>
/// ISO mounts as a standard user; VHD/VHDX attach needs elevation (see <see cref="MountSupport"/>) and is
/// routed through the privilege bridge — this class runs the un-elevated (ISO) path and the read queries.
/// </summary>
public sealed class DiskMounter
{
    private const string ImageEnvVar = "NEXA_DISK_IMAGE";

    /// <summary>Mount script — reads the path from the environment (injection-safe) and echoes the assigned letter.</summary>
    public const string MountScript = """
        $ErrorActionPreference = 'Stop'
        $p = $env:NEXA_DISK_IMAGE
        $null = Mount-DiskImage -ImagePath $p -PassThru
        $dl = (Get-DiskImage -ImagePath $p | Get-Volume | Where-Object { $_.DriveLetter } | Select-Object -First 1).DriveLetter
        if ($dl) { [Console]::Out.Write("DRIVE=$dl") }
        """;

    public const string DismountScript = """
        $ErrorActionPreference = 'Stop'
        Dismount-DiskImage -ImagePath $env:NEXA_DISK_IMAGE | Out-Null
        """;

    /// <summary>Lists every drive letter currently backed by a mounted disk image, tab-separated from its
    /// image path. Reliable for ISO (CD-ROM) and VHD/VHDX alike — the reverse <c>Get-Volume | Get-DiskImage</c>
    /// hop is what the WMI join can't do for optical mounts.</summary>
    public const string ImageBackedDrivesScript = """
        Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
          $dl = $_.DriveLetter
          $di = $_ | Get-DiskImage -ErrorAction SilentlyContinue
          if ($di -and $di.ImagePath) { [Console]::Out.WriteLine("$dl`t$($di.ImagePath)") }
        }
        """;

    // ── Writes (un-elevated path: ISO) ───────────────────────────────────────────

    /// <summary>Mounts <paramref name="imagePath"/> and returns the assigned drive letter. Runs without
    /// elevation (correct for ISO); VHD/VHDX go through the bridge instead.</summary>
    public MountOutcome Mount(string imagePath)
    {
        var (exit, output, error) = RunPowerShell(MountScript, (ImageEnvVar, imagePath));
        InvalidateCache();
        return exit == 0 ? MountOutcome.Ok(ParseDrive(output)) : MountOutcome.Failed(FirstLine(error));
    }

    /// <summary>Dismounts <paramref name="imagePath"/>. Un-elevated path (ISO).</summary>
    public bool Dismount(string imagePath)
    {
        var (exit, _, _) = RunPowerShell(DismountScript, (ImageEnvVar, imagePath));
        InvalidateCache();
        return exit == 0;
    }

    /// <summary>Parses the <c>DRIVE=X</c> token the mount script writes; returns e.g. <c>"E:"</c> or null.</summary>
    public static string? ParseDrive(string output)
    {
        var i = output.IndexOf("DRIVE=", StringComparison.Ordinal);
        if (i < 0) return null;
        var c = output.Skip(i + 6).FirstOrDefault(char.IsLetter);
        return c == default ? null : $"{char.ToUpperInvariant(c)}:";
    }

    // ── Reads (cached — run on folder selection) ──────────────────────────────────

    private static Dictionary<char, string>? _cache;
    private static long _cacheStamp;
    private const long CacheTtlMs = 2000;
    private static readonly object CacheLock = new();

    public static void InvalidateCache()
    {
        lock (CacheLock) _cache = null;
    }

    /// <summary>True if <paramref name="driveLetter"/> (a single A–Z) is a volume of a mounted disk image.</summary>
    public bool IsImageBacked(char driveLetter) =>
        CachedDrives().ContainsKey(char.ToUpperInvariant(driveLetter));

    /// <summary>The image file backing <paramref name="driveLetter"/>, or null if the drive isn't image-backed.</summary>
    public string? ImagePathForDrive(char driveLetter) =>
        CachedDrives().GetValueOrDefault(char.ToUpperInvariant(driveLetter));

    private static Dictionary<char, string> CachedDrives()
    {
        long now = Environment.TickCount64;
        lock (CacheLock)
            if (_cache is not null && now - _cacheStamp < CacheTtlMs) return _cache;

        var fresh = QueryImageBackedDrives();

        lock (CacheLock) { _cache = fresh; _cacheStamp = Environment.TickCount64; }
        return fresh;
    }

    private static Dictionary<char, string> QueryImageBackedDrives()
    {
        var map = new Dictionary<char, string>();
        var (exit, output, _) = RunPowerShell(ImageBackedDrivesScript);
        if (exit != 0) return map;
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            int tab = t.IndexOf('\t');
            if (tab < 1) continue;
            char dl = t[0];
            var path = t[(tab + 1)..].Trim();
            if (char.IsLetter(dl) && path.Length > 0) map[char.ToUpperInvariant(dl)] = path;
        }
        return map;
    }

    // ── PowerShell plumbing ───────────────────────────────────────────────────────

    private static string? FirstLine(string s)
    {
        s = s?.Trim() ?? string.Empty;
        var nl = s.IndexOfAny(['\r', '\n']);
        return s.Length == 0 ? null : (nl < 0 ? s : s[..nl]);
    }

    private static (int Exit, string Output, string Error) RunPowerShell(
        string script, params (string Key, string Value)[] env)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);
        foreach (var (k, v) in env) psi.EnvironmentVariables[k] = v;

        try
        {
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch { } return (-1, o, "Operation timed out."); }
            return (p.ExitCode, o, e);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
