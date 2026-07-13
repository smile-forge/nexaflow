using System.Diagnostics;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Attaches/detaches a disk image through the built-in Windows Virtual Disk Service (the Storage
/// <c>Mount-DiskImage</c>/<c>Dismount-DiskImage</c> cmdlets) while running elevated. VHD/VHDX attach requires
/// administrator rights, which is why it happens here in the bridge rather than in-process.
/// <para>
/// The scripts mirror the feature's <c>DiskMounter</c> (the trust boundary forbids sharing code — the bridge
/// references only <c>Elevation.Contracts</c>). The image path is passed via an environment variable, never
/// interpolated into the script text, so a crafted path cannot inject PowerShell into an elevated process.
/// </para>
/// </summary>
internal static class DiskImageMountHelper
{
    private const string ImageEnvVar = "NEXA_DISK_IMAGE";

    private const string MountScript = """
        $ErrorActionPreference = 'Stop'
        $p = $env:NEXA_DISK_IMAGE
        $null = Mount-DiskImage -ImagePath $p -PassThru
        $dl = (Get-DiskImage -ImagePath $p | Get-Volume | Where-Object { $_.DriveLetter } | Select-Object -First 1).DriveLetter
        if ($dl) { [Console]::Out.Write("DRIVE=$dl") }
        """;

    private const string DismountScript = """
        $ErrorActionPreference = 'Stop'
        Dismount-DiskImage -ImagePath $env:NEXA_DISK_IMAGE | Out-Null
        """;

    public static (bool Success, string? DriveLetter, string Message) Mount(string imagePath)
    {
        var (exit, output, error) = Run(MountScript, imagePath);
        return exit == 0
            ? (true, ParseDrive(output), "Mounted.")
            : (false, null, FirstLine(error) ?? "Mount failed.");
    }

    public static (bool Success, string Message) Dismount(string imagePath)
    {
        var (exit, _, error) = Run(DismountScript, imagePath);
        return exit == 0 ? (true, "Dismounted.") : (false, FirstLine(error) ?? "Dismount failed.");
    }

    public static string? ParseDrive(string output)
    {
        var i = output.IndexOf("DRIVE=", System.StringComparison.Ordinal);
        if (i < 0) return null;
        foreach (var c in output[(i + 6)..])
            if (char.IsLetter(c)) return $"{char.ToUpperInvariant(c)}:";
        return null;
    }

    private static string? FirstLine(string s)
    {
        s = s?.Trim() ?? string.Empty;
        if (s.Length == 0) return null;
        var nl = s.IndexOfAny(['\r', '\n']);
        return nl < 0 ? s : s[..nl];
    }

    private static (int Exit, string Output, string Error) Run(string script, string imagePath)
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
        psi.EnvironmentVariables[ImageEnvVar] = imagePath;

        try
        {
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch { } return (-1, o, "Operation timed out."); }
            return (p.ExitCode, o, e);
        }
        catch (System.Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
