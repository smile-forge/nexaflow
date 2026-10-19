using Microsoft.Win32;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The host's Edge WebView2 runtime, lent to the sandbox, whose clean image has none. The runtime's version folder is
/// mapped read-only where a per-machine install would put it, but the loader finds no runtime there by its folder alone —
/// an install also leaves records a mapped folder does not bring — nor by <c>WEBVIEW2_BROWSER_EXECUTABLE_FOLDER</c>.
/// What it does take is the <c>BrowserExecutableFolder</c> policy, so the sandbox sets that for every app before the
/// agent starts.
/// </summary>
internal sealed record HostWebView2(string HostFolder, string Version)
{
    /// <summary>The runtime's EdgeUpdate client, under which its version and install folder are recorded.</summary>
    private const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>Its values name an app, or <c>*</c> for every app, and hold the runtime folder that app is to use.</summary>
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder";

    /// <summary>Where the runtime appears in the sandbox: where a per-machine install would put this version.</summary>
    public string SandboxFolder => Path.Combine(@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application", Version);

    /// <summary>The <c>cmd</c> commands that point every app in the sandbox at <see cref="SandboxFolder"/>.</summary>
    public IReadOnlyList<string> SandboxCommands => [$"reg add HKLM\\{PolicyKey} /v * /t REG_SZ /d \"{SandboxFolder}\" /f >nul"];

    /// <summary>The runtime the host's own apps get — a per-machine install first, as the loader looks, then a per-user one; null when there is neither.</summary>
    public static HostWebView2? Find() =>
        FromClientKey(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\{ClientKey}")
        ?? FromClientKey(Registry.CurrentUser, $@"Software\{ClientKey}");

    private static HostWebView2? FromClientKey(RegistryKey hive, string path)
    {
        using var key = hive.OpenSubKey(path);
        if (key?.GetValue("pv") is not string version || key.GetValue("location") is not string location) return null;
        var folder = Path.Combine(location, version);
        return File.Exists(Path.Combine(folder, "msedgewebview2.exe")) ? new HostWebView2(folder, version) : null;
    }
}
