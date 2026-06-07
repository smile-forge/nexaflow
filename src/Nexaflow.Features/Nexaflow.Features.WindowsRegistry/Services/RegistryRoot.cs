using Microsoft.Win32;

namespace Nexaflow.Features.WindowsRegistry.Services;

/// <summary>
/// One of the three user-facing hive roots offered by the viewer. <see cref="Token"/> is the short
/// form used in paths, tab params, <c>reg.exe</c>, and the elevation bridge (<c>ElevatedArgs.RegHive</c>).
/// </summary>
public sealed class RegistryRoot
{
    public string      Token       { get; }   // "HKCU" | "HKLM" | "HKCR"
    public string      DisplayName { get; }   // "HKEY_CURRENT_USER" …
    public RegistryKey Key         { get; }   // process-wide singleton — never disposed

    private RegistryRoot(string token, string displayName, RegistryKey key)
    {
        Token = token; DisplayName = displayName; Key = key;
    }

    public static readonly RegistryRoot CurrentUser =
        new("HKCU", "HKEY_CURRENT_USER", Registry.CurrentUser);
    public static readonly RegistryRoot LocalMachine =
        new("HKLM", "HKEY_LOCAL_MACHINE", Registry.LocalMachine);
    public static readonly RegistryRoot ClassesRoot =
        new("HKCR", "HKEY_CLASSES_ROOT", Registry.ClassesRoot);

    /// <summary>The three roots in dropdown order.</summary>
    public static readonly IReadOnlyList<RegistryRoot> All = [CurrentUser, LocalMachine, ClassesRoot];

    public static RegistryRoot? FromToken(string? token) =>
        All.FirstOrDefault(r => string.Equals(r.Token, token, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => DisplayName;
}
