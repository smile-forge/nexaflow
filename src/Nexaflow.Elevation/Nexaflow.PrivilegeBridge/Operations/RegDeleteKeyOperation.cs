using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Deletes a key and everything under it (no-op if already absent).</summary>
internal sealed class RegDeleteKeyOperation : RegOperationBase
{
    public override string Id => ElevatedOps.RegDeleteKey;

    protected override ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args)
    {
        if (string.IsNullOrEmpty(path)) return Fail("Refusing to delete the hive root.");

        hive.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        return Ok($"Deleted key '{path}'.");
    }
}
