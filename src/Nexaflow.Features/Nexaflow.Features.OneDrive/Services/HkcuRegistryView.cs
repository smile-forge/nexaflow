using Microsoft.Win32;

namespace Nexaflow.Features.OneDrive.Services;

/// <summary>Reads the real HKEY_CURRENT_USER hive. Every failure is absence: a machine without OneDrive
/// and a machine whose keys we can't read are the same answer as far as detection is concerned.</summary>
public sealed class HkcuRegistryView : IRegistryView
{
    public IReadOnlyList<string> SubKeyNames(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetSubKeyNames() ?? [];
        }
        catch { return []; }
    }

    public string? GetString(string path, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }
}
