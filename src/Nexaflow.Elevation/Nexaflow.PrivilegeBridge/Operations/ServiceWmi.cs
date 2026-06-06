using System.Management;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// WMI-backed start-mode change. <see cref="System.ServiceProcess.ServiceController"/> can start/stop a
/// service but cannot change its start mode, so we invoke <c>Win32_Service.ChangeStartMode</c>.
/// </summary>
internal static class ServiceWmi
{
    /// <param name="wmiMode">"Automatic" | "Manual" | "Disabled" (WMI has no delayed-auto value).</param>
    public static (bool Ok, string Message) ChangeStartMode(string serviceName, string wmiMode)
    {
        using var svc = new ManagementObject($"Win32_Service.Name='{Escape(serviceName)}'");
        using var inParams = svc.GetMethodParameters("ChangeStartMode");
        inParams["StartMode"] = wmiMode;
        using var outParams = svc.InvokeMethod("ChangeStartMode", inParams, null);

        var code = Convert.ToUInt32(outParams["ReturnValue"]);
        return code == 0 ? (true, "") : (false, $"ChangeStartMode failed (code {code}: {Describe(code)}).");
    }

    // WMI object-path values escape backslash and single-quote.
    private static string Escape(string name) => name.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string Describe(uint code) => code switch
    {
        2  => "access denied",
        3  => "dependent services running",
        15 => "service disabled",
        16 => "service marked for deletion",
        21 => "invalid parameter",
        22 => "invalid service control",
        _  => "unknown error",
    };
}
