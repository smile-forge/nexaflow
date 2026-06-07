using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Deletes a value from a key (no-op if the value is already absent).</summary>
internal sealed class RegDeleteValueOperation : RegOperationBase
{
    public override string Id => ElevatedOps.RegDeleteValue;

    protected override ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args)
    {
        var name = args.GetValueOrDefault(ElevatedArgs.RegName) ?? "";

        using var key = hive.OpenSubKey(path, writable: true);
        if (key is null) return Fail($"Key '{path}' not found.");

        key.DeleteValue(name, throwOnMissingValue: false);
        return Ok($"Deleted value '{(name.Length == 0 ? "(Default)" : name)}'.");
    }
}
