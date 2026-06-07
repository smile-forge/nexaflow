using Microsoft.Win32;

namespace Nexaflow.Features.WindowsRegistry.Services;

/// <summary>
/// In-process registry writes (the fast path for HKCU and any already-writable key). Each method
/// throws <see cref="UnauthorizedAccessException"/>/<see cref="System.Security.SecurityException"/> on a
/// protected key, which the ViewModel catches to retry via the elevation bridge. The logic mirrors the
/// bridge's handlers so an edit produces identical bytes either way.
/// </summary>
public static class RegistryWriter
{
    public static void SetValue(RegistryRoot root, string subPath, string name, string typeToken, string wire)
    {
        var (data, kind) = RegistryValueCodec.Decode(typeToken, wire);
        var key = subPath.Length == 0 ? root.Key : root.Key.CreateSubKey(subPath, writable: true);
        if (key is null) throw new InvalidOperationException($"Could not open key '{subPath}'.");
        try { key.SetValue(name, data, kind); }
        finally { if (subPath.Length > 0) key.Dispose(); }
    }

    public static void DeleteValue(RegistryRoot root, string subPath, string name)
    {
        var key = subPath.Length == 0 ? root.Key : root.Key.OpenSubKey(subPath, writable: true);
        if (key is null) return;   // key gone — nothing to delete
        try { key.DeleteValue(name, throwOnMissingValue: false); }
        finally { if (subPath.Length > 0) key.Dispose(); }
    }

    public static void CreateKey(RegistryRoot root, string subPath)
    {
        if (string.IsNullOrEmpty(subPath)) throw new InvalidOperationException("Missing key path.");
        using var key = root.Key.CreateSubKey(subPath, writable: true)
                        ?? throw new InvalidOperationException($"Could not create key '{subPath}'.");
    }

    public static void DeleteKey(RegistryRoot root, string subPath)
    {
        if (string.IsNullOrEmpty(subPath)) throw new InvalidOperationException("Cannot delete the hive root.");
        root.Key.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
    }

    public static void RenameKey(RegistryRoot root, string subPath, string newName)
    {
        if (string.IsNullOrEmpty(subPath)) throw new InvalidOperationException("Cannot rename the hive root.");

        var slash    = subPath.LastIndexOf('\\');
        var parent   = slash >= 0 ? subPath[..slash] : "";
        var destPath = parent.Length == 0 ? newName : $"{parent}\\{newName}";

        using (var existing = root.Key.OpenSubKey(destPath, writable: false))
            if (existing is not null) throw new InvalidOperationException($"A key named '{newName}' already exists.");

        using (var source = root.Key.OpenSubKey(subPath, writable: false)
                            ?? throw new InvalidOperationException($"Key '{subPath}' not found."))
        using (var dest = root.Key.CreateSubKey(destPath, writable: true)
                          ?? throw new InvalidOperationException($"Could not create key '{destPath}'."))
        {
            CopyTree(source, dest);
        }

        root.Key.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
    }

    private static void CopyTree(RegistryKey source, RegistryKey dest)
    {
        foreach (var valueName in source.GetValueNames())
        {
            var kind = source.GetValueKind(valueName);
            var data = source.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (data is not null) dest.SetValue(valueName, data, kind);
        }
        foreach (var subName in source.GetSubKeyNames())
        {
            using var srcSub = source.OpenSubKey(subName, writable: false);
            if (srcSub is null) continue;
            using var dstSub = dest.CreateSubKey(subName, writable: true);
            if (dstSub is null) continue;
            CopyTree(srcSub, dstSub);
        }
    }
}
