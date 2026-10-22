using Microsoft.Win32;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// Smart App Control as the host has it, carried into the sandbox. The clean image boots with it on whatever the host
/// says, so an agent freshly built here, unsigned and with no reputation yet, is refused there while the host runs it
/// freely. A host with it off has the sandbox turn it off too before the agent starts; a host with it on leaves the
/// sandbox as it boots.
/// </summary>
internal static class HostSmartAppControl
{
    private const string PolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    /// <summary>0 is off; 1 enforces and 2 evaluates, and either can refuse an unsigned build.</summary>
    private const string StateValue = "VerifiedAndReputablePolicyState";

    public static bool IsOff
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
            return key?.GetValue(StateValue) is 0;
        }
    }

    /// <summary>
    /// The <c>cmd</c> commands that bring the sandbox to the host's state; none when it is to be left as it boots.
    /// <c>citool --refresh</c> applies the change without the restart a sandbox cannot survive.
    /// </summary>
    public static IReadOnlyList<string> SandboxCommands(bool hostIsOff) => hostIsOff
        ? [$@"reg add HKLM\{PolicyKey} /v {StateValue} /t REG_DWORD /d 0 /f >nul", "citool.exe --refresh >nul"]
        : [];
}
