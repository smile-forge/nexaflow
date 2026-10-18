using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// The app as the agent launches it for someone to look at or drive: started by the journeys' own recipe on a fresh config
/// root, and closed with its crash log read the way a journey's teardown reads it.
/// </summary>
internal sealed class AppUnderTest(UIA3Automation automation) : IDisposable
{
    private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(60);

    private Application? _app;
    private string? _configDir;

    public int? Pid => _app is { HasExited: false } app ? app.ProcessId : null;

    public bool IsRunning => Pid is not null;

    public string Launch(string? tabKind, IReadOnlyDictionary<string, string> tabParams)
    {
        if (Pid is { } running) throw new SandboxException($"Nexaflow is already running (pid {running}). `close` it first.");

        _configDir = TestTemp.CreateSubdirectory("sandbox-app-").FullName;
        _app = Application.Launch(AppLaunchRecipe.StartInfo(_configDir, tabKind, tabParams));
        var shell = WaitForShell(_app);
        UiTestGate.BringToForeground(shell.Properties.NativeWindowHandle.ValueOrDefault, TimeSpan.FromSeconds(10));
        return $"Nexaflow is up (pid {_app.ProcessId}){(tabKind is null ? "" : $" on {tabKind}")}; its config root is {_configDir}.";
    }

    /// <summary>Closes the app and says whether it logged a crash while it was up.</summary>
    public string Close()
    {
        if (_app is null) return "Nexaflow was not running.";

        try
        {
            _app.Kill();
            for (var waited = 0; !_app.HasExited && waited < 5000; waited += 100) Thread.Sleep(100);
        }
        catch (InvalidOperationException) { /* already gone */ }
        _app.Dispose();
        _app = null;

        var crash = _configDir is null ? null : AppLaunchRecipe.ReadCrashLogs(_configDir);
        try { if (_configDir is not null) Directory.Delete(_configDir, recursive: true); }
        catch (IOException) { /* TestTemp clears it when the agent exits */ }
        _configDir = null;

        return crash is null
            ? "Closed Nexaflow. It logged no crash."
            : $"Closed Nexaflow. It logged an unhandled exception:{Environment.NewLine}{crash}";
    }

    /// <summary>The shell window, AutomationId <c>MainWindow</c>; with <c>--uiTest</c> no wizard comes before it.</summary>
    private Window WaitForShell(Application app)
    {
        var deadline = DateTime.UtcNow + ShellTimeout;
        while (DateTime.UtcNow < deadline)
        {
            // A window still being made answers its properties with a COM error: it is not the shell yet.
            try
            {
                if (app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Properties.AutomationId.ValueOrDefault == "MainWindow") is { } shell)
                    return shell;
            }
            catch (COMException) { }
            Thread.Sleep(100);
        }
        throw new SandboxException($"Nexaflow started, but its window did not appear within {ShellTimeout.TotalSeconds:0}s.");
    }

    public void Dispose()
    {
        if (_app is not null) Close();
    }
}
