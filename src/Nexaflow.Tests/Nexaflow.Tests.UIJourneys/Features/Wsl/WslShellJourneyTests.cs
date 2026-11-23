using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Wsl.UI;

/// <summary>
/// One-pass UI journey for the WSL shell: Shell Here on the distro's <c>/tmp</c> opens the distro's own shell in its own tab,
/// and the Environment panel switches between the variables that shell holds now and those a new shell starts with — the
/// two views only a shell whose host knows its environment offers. Never types into the shell.
/// <para>
/// Needs a WSL distro, which Windows Sandbox has none of: run it on a desktop that has one, with
/// <c>--filter "TestCategory=UI&amp;TestCategory=Wsl"</c>. Inconclusive where WSL has no distro.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("Wsl")]
[CoversNode("wslshell")]
public class WslShellJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("wslshell-ui")]
    public void ShellHere_OpensTheDistrosShell_WhoseEnvironmentSwitchesBetweenLiveAndStartup()
    {
        var distro = FirstDistro() ?? throw new AssertInconclusiveException("WSL has no distro installed on this machine.");

        NavigateFileBrowserTo($@"\\wsl.localhost\{distro}\tmp");
        var action = WaitForId("ShellHereAction", 8);
        Assert.IsNotNull(action, "The 'Shell Here' folder action did not appear for the distro's folder.");
        action!.AsButton().Invoke();
        Assert.IsNotNull(WaitForId("ConsoleView", 20), "The distro's shell did not open.");

        CheckInvoke("Environment sub-tab", "Console_EnvironmentTab");
        Assert.IsNotNull(WaitForId("Console_EnvLive", 20), "The Live / Startup switch did not appear for the distro's shell.");
        CheckInvoke("Startup variables", "Console_EnvStartup");
        CheckInvoke("Live variables", "Console_EnvLive");

        AssertJourney();
    }

    /// <summary>The first distro <c>wsl.exe</c> lists, or null when it lists none.</summary>
    private static string? FirstDistro()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"), "-l -q")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.Unicode,   // wsl.exe lists in UTF-16
        };
        try
        {
            using var process = Process.Start(start)!;
            var listed = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return listed.Split('\n').Select(line => line.Trim('\0', '\r', ' ')).FirstOrDefault(line => line.Length > 0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;   // no WSL on this machine
        }
    }
}
