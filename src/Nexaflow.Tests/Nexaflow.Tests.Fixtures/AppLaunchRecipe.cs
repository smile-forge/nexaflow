using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// How the built app is started for a journey. One recipe, so the app the sandbox agent launches for someone to drive is
/// the app a journey sees.
/// </summary>
public static class AppLaunchRecipe
{
    /// <summary>Points the app at its config root instead of <c>%APPDATA%\Smile\nexaflow</c>.</summary>
    public const string ConfigDirVariable = "NEXAFLOW_CONFIG_DIR";

    /// <summary>
    /// Starts <see cref="BuiltOutput.App"/> against the isolated <paramref name="configDir"/> with <c>--uiTest</c> — "a journey
    /// is driving this window": no first-run wizard, eager feature activation rather than lazy warm-up, and none of the
    /// background work that would otherwise talk over the test. <paramref name="tabKind"/> opens that page on launch
    /// (<c>--openTab</c>), with each of <paramref name="tabParams"/> passed as <c>--tabParam key=value</c>.
    /// </summary>
    public static ProcessStartInfo StartInfo(
        string configDir,
        string? tabKind = null,
        IReadOnlyDictionary<string, string>? tabParams = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(BuiltOutput.App) { UseShellExecute = false };
        psi.ArgumentList.Add("--uiTest");
        if (tabKind is { Length: > 0 })
        {
            psi.ArgumentList.Add("--openTab");
            psi.ArgumentList.Add(tabKind);
            foreach (var (name, value) in tabParams ?? ReadOnlyDictionary<string, string>.Empty)
            {
                psi.ArgumentList.Add("--tabParam");
                psi.ArgumentList.Add($"{name}={value}");
            }
        }
        psi.EnvironmentVariables[ConfigDirVariable] = configDir;
        foreach (var (name, value) in environment ?? ReadOnlyDictionary<string, string>.Empty)
            psi.EnvironmentVariables[name] = value;
        return psi;
    }

    /// <summary>
    /// Everything the app recorded under <paramref name="configDir"/>, or null if it recorded nothing. The app writes any
    /// unhandled exception to a dated crash log in its config root and marks it handled so it stays up, making the log the
    /// only trail. The log is one file per day (<c>crash-yyyy-MM-dd.log</c>), so a run spanning midnight leaves two — read
    /// them all rather than the one whose name today happens to be.
    /// </summary>
    public static string? ReadCrashLogs(string configDir)
    {
        try
        {
            if (!Directory.Exists(configDir)) return null;
            var logs = Directory.GetFiles(configDir, "crash*.log").OrderBy(p => p).ToList();
            if (logs.Count == 0) return null;
            return string.Join(Environment.NewLine, logs.Select(File.ReadAllText));
        }
        catch { return null; }
    }
}
