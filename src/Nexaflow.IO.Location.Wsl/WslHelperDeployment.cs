using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Processes;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>Where a distro's helper and shell shims are, and the helper started there.</summary>
/// <param name="Folder">The distro's own path of the folder holding them.</param>
internal sealed record WslHelperStart(WslHelperLink Link, string Folder);

/// <summary>
/// Puts the helper and the shell shims into a distro — once per version, under the user's cache — and starts the helper.
/// The helper for each Linux sits beside this assembly in <c>wsl-helper\&lt;rid&gt;\</c>; which one a distro needs is
/// read from its machine and C library with one <c>sh</c>, before anything is copied.
/// </summary>
internal static class WslHelperDeployment
{
    public const string HelperName = "nexaflow-wsl-helper";

    // The shims embedded in this assembly, and where each goes under the helper's folder.
    private static readonly (string Resource, string Path)[] Shims =
    [
        ("Shell/nexaflow.bashrc", "nexaflow.bashrc"),
        ("Shell/zshenv", "zsh/.zshenv"),
        ("Shell/zprofile", "zsh/.zprofile"),
        ("Shell/zshrc", "zsh/.zshrc"),
    ];

    private static readonly ConcurrentDictionary<string, string> Versions = new(StringComparer.Ordinal);

    private static string HelperRoot
        => Path.Combine(Path.GetDirectoryName(typeof(WslHelperDeployment).Assembly.Location)!, "wsl-helper");

    /// <summary>Starts the distro if it is not running, deploys what this version needs, and starts the helper.</summary>
    public static async ValueTask<WslHelperStart> StartAsync(string distro, CancellationToken ct)
    {
        var probe = await ProbeAsync(distro, ct).ConfigureAwait(false);
        var rid   = RuntimeFor(probe.Machine, probe.Musl)
                    ?? throw new LocationException(LocationError.Disconnected, "/",
                           $"'{distro}' runs on {probe.Machine}{(probe.Musl ? " with musl" : "")}, which Nexaflow has no helper for.");
        var local = Path.Combine(HelperRoot, rid, HelperName);
        if (!File.Exists(local))
            throw new LocationException(LocationError.Disconnected, "/", $"The WSL helper for {rid} is missing from this installation.");

        var folder = $"{probe.Cache.TrimEnd('/')}/nexaflow/wsl-helper/{VersionOf(rid, local)}";
        await EnsureDeployedAsync(distro, local, folder, ct).ConfigureAwait(false);
        return new WslHelperStart(Launch(distro, $"{folder}/{HelperName}"), folder);
    }

    /// <summary>The runtime a distro's helper is published for — <c>linux-musl-arm64</c> — or null for a machine none is.</summary>
    internal static string? RuntimeFor(string machine, bool musl)
    {
        var arch = machine switch
        {
            "x86_64" or "amd64" => "x64",
            "aarch64" or "arm64" => "arm64",
            _ => null,
        };
        return arch is null ? null : musl ? $"linux-musl-{arch}" : $"linux-{arch}";
    }

    private sealed record Probe(string Machine, bool Musl, string Cache);

    // One sh, which also starts the distro: its machine, its C library, and the user's cache folder.
    private static async ValueTask<Probe> ProbeAsync(string distro, CancellationToken ct)
    {
        const string Script = "uname -m; if ls /lib/ld-musl-*.so.1 >/dev/null 2>&1; then echo musl; else echo gnu; fi; "
                            + "printf '%s\\n' \"${XDG_CACHE_HOME:-$HOME/.cache}\"";
        var output = await RunAsync(distro, ct, "--exec", "sh", "-c", Script).ConfigureAwait(false);
        var lines  = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3 || !lines[2].StartsWith('/'))
            throw new LocationException(LocationError.Disconnected, "/", $"'{distro}' did not say where its user's cache is.");
        return new Probe(lines[0], lines[1] == "musl", lines[2]);
    }

    // The helper and the shims, hashed together: a folder per version, so a newer Nexaflow never runs an older helper.
    private static string VersionOf(string rid, string local)
        => Versions.GetOrAdd(rid, _ =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(File.ReadAllBytes(local));
            foreach (var (resource, _) in Shims) hash.AppendData(ShimBytes(resource));
            return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
        });

    // A shim with Linux line endings, whatever the checkout gave it.
    private static byte[] ShimBytes(string resource)
    {
        using var stream = typeof(WslHelperDeployment).Assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"The shim '{resource}' is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new UTF8Encoding(false).GetBytes(reader.ReadToEnd().Replace("\r\n", "\n"));
    }

    // Copies into a staging folder, marks the helper runnable, then renames the folder into place: a session racing this
    // one never runs a half-copied helper, and whichever rename lands first is kept.
    private static async ValueTask EnsureDeployedAsync(string distro, string local, string folder, CancellationToken ct)
    {
        var target = WslNames.NativePath(distro, folder);
        if (await Task.Run(() => File.Exists(Path.Combine(target, HelperName)), ct).ConfigureAwait(false)) return;

        var staging       = $"{folder}.{Guid.NewGuid():N}.staging";
        var stagingNative = WslNames.NativePath(distro, staging);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(stagingNative);
                File.Copy(local, Path.Combine(stagingNative, HelperName));
                foreach (var (resource, path) in Shims)
                {
                    var file = Path.Combine(stagingNative, path.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllBytes(file, ShimBytes(resource));
                }
            }, ct).ConfigureAwait(false);
            await RunAsync(distro, ct, "--exec", "chmod", "755", $"{staging}/{HelperName}").ConfigureAwait(false);

            await Task.Run(() =>
            {
                try { Directory.Move(stagingNative, target); }
                catch (IOException) when (File.Exists(Path.Combine(target, HelperName))) { Directory.Delete(stagingNative, recursive: true); }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (LocationFailures.Translate(failure, folder) is { } translated)
        {
            throw translated;
        }
    }

    private static WslHelperLink Launch(string distro, string helper)
    {
        var start = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var argument in (string[])["-d", distro, "--exec", helper]) start.ArgumentList.Add(argument);

        var process = Process.Start(start)
                      ?? throw new LocationException(LocationError.Disconnected, "/", $"wsl.exe did not start for '{distro}'.");
        KillOnCloseJob.Assign(process.Handle);
        return new WslHelperLink(distro, process);
    }

    /// <summary>Runs <c>wsl.exe -d &lt;distro&gt;</c> with the arguments and returns what it wrote, failing with
    /// <see cref="LocationException"/> when it fails — <see cref="LocationError.NotFound"/> when the distro is not installed.</summary>
    internal static async ValueTask<string> RunAsync(string distro, CancellationToken ct, params string[] arguments)
    {
        var start = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        start.ArgumentList.Add("-d");
        start.ArgumentList.Add(distro);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
                            ?? throw new LocationException(LocationError.Disconnected, "/", $"wsl.exe did not start for '{distro}'.");
        using var output  = new MemoryStream();
        using var errors  = new MemoryStream();
        try
        {
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output, ct),
                               process.StandardError.BaseStream.CopyToAsync(errors, ct),
                               process.WaitForExitAsync(ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }

        if (process.ExitCode == 0) return Encoding.UTF8.GetString(output.ToArray());

        var said = Said(errors.ToArray()) is { Length: > 0 } error ? error : Said(output.ToArray());
        bool installed = WslRegistry.Read().Any(d => string.Equals(d.Name, distro, StringComparison.OrdinalIgnoreCase));
        throw new LocationException(installed ? LocationError.Disconnected : LocationError.NotFound, "/",
                                    $"wsl.exe failed for '{distro}' ({process.ExitCode}): {said}");
    }

    // wsl.exe writes its own messages as UTF-16; what the distro writes is UTF-8.
    private static string Said(byte[] bytes)
        => (bytes.Length > 1 && bytes.Contains((byte)0) ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes)).Trim();
}
