using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Renames a key by copying its tree to a sibling under the new leaf name, then deleting the
/// original. (The registry has no native rename.)</summary>
internal sealed class RegRenameKeyOperation : RegOperationBase
{
    public override string Id => ElevatedOps.RegRenameKey;

    protected override ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args)
    {
        var newName = args.GetValueOrDefault(ElevatedArgs.RegNewName)?.Trim();
        if (string.IsNullOrEmpty(path))    return Fail("Cannot rename the hive root.");
        if (string.IsNullOrEmpty(newName)) return Fail("Missing new key name.");
        if (newName.Contains('\\'))        return Fail("New key name cannot contain '\\'.");

        var slash      = path.LastIndexOf('\\');
        var parentPath = slash >= 0 ? path[..slash] : "";
        var destPath   = parentPath.Length == 0 ? newName : $"{parentPath}\\{newName}";

        if (string.Equals(path, destPath, StringComparison.OrdinalIgnoreCase))
            return Ok("Name unchanged.");

        using (var existing = hive.OpenSubKey(destPath, writable: false))
            if (existing is not null) return Fail($"A key named '{newName}' already exists.");

        using (var source = hive.OpenSubKey(path, writable: false))
        {
            if (source is null) return Fail($"Key '{path}' not found.");
            using var dest = hive.CreateSubKey(destPath, writable: true)
                             ?? throw new InvalidOperationException($"Could not create key '{destPath}'.");
            RegistryKeyCopy.CopyTree(source, dest);
        }

        hive.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        return Ok($"Renamed key to '{newName}'.");
    }
}
