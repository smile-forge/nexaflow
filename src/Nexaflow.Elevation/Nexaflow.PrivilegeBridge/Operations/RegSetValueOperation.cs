using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Creates or changes a value under a key (the key is created if missing).</summary>
internal sealed class RegSetValueOperation : RegOperationBase
{
    public override string Id => ElevatedOps.RegSetValue;

    protected override ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args)
    {
        var name = args.GetValueOrDefault(ElevatedArgs.RegName) ?? "";
        var (data, kind) = DecodeValue(
            args.GetValueOrDefault(ElevatedArgs.RegType),
            args.GetValueOrDefault(ElevatedArgs.RegValue));

        using var key = hive.CreateSubKey(path, writable: true)
                        ?? throw new InvalidOperationException($"Could not open key '{path}'.");
        key.SetValue(name, data, kind);
        return Ok($"Set value '{(name.Length == 0 ? "(Default)" : name)}'.");
    }
}
