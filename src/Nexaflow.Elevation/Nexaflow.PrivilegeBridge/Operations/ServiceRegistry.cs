using Microsoft.Win32;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// The "Automatic (Delayed Start)" flag has no WMI representation; it lives in the service's registry
/// key as the <c>DelayedAutostart</c> DWORD. We toggle it after a WMI ChangeStartMode to Automatic.
/// </summary>
internal static class ServiceRegistry
{
    public static void SetDelayedAutostart(string serviceName, bool delayed)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
        key?.SetValue("DelayedAutostart", delayed ? 1 : 0, RegistryValueKind.DWord);
    }
}
