using Microsoft.Win32;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Deep-copies a registry key's values and subkeys — the registry has no native key rename,
/// so rename = copy-tree then delete the original.</summary>
internal static class RegistryKeyCopy
{
    public static void CopyTree(RegistryKey source, RegistryKey dest)
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
