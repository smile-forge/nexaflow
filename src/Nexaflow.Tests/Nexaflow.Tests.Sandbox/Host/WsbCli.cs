using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The Windows Sandbox command line, <c>wsb</c>, which ships with the Windows Sandbox app. Starting, connecting to and
/// stopping a sandbox by its id is what lets the host tear one down cleanly: a sandbox shut down from inside leaves its
/// window behind on an error.
/// </summary>
internal static class WsbCli
{
    /// <summary>Starts a sandbox from configuration XML and returns its id. Nobody is signed in to it until it is connected to.</summary>
    public static string Start(string configurationXml)
    {
        var output = Invoke("start", "--config", configurationXml, "--raw");
        using var answer = JsonDocument.Parse(output);
        return answer.RootElement.GetProperty("Id").GetString()
            ?? throw new SandboxException($"wsb start answered without an id: {output}");
    }

    /// <summary>The ids of the sandboxes running now.</summary>
    public static IReadOnlyList<string> Running()
    {
        using var answer = JsonDocument.Parse(Invoke("list", "--raw"));
        return answer.RootElement.GetProperty("WindowsSandboxEnvironments").EnumerateArray()
            .Select(e => e.GetProperty("Id").GetString())
            .OfType<string>()
            .ToList();
    }

    /// <summary>Opens the sandbox's window, which signs its user in. <c>wsb connect</c> lasts as long as the window, so it is left running.</summary>
    public static void Connect(string id)
    {
        using var _ = Launch(capture: false, "connect", "--id", id);
    }

    public static void Stop(string id) => Invoke("stop", "--id", id);

    private static string Invoke(params string[] arguments)
    {
        using var process = Launch(capture: true, arguments);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new SandboxException($"wsb {arguments[0]} failed ({process.ExitCode}): {(error.Result + output.Result).Trim()}");
        return output.Result;
    }

    private static Process Launch(bool capture, params string[] arguments)
    {
        var start = new ProcessStartInfo("wsb")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            return Process.Start(start) ?? throw new SandboxException("wsb did not start.");
        }
        catch (Win32Exception)
        {
            throw new SandboxException(
                "The wsb command was not found. It comes with the Windows Sandbox app: turn the feature on with "
                + "`Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM`, then keep the app updated.");
        }
    }
}
