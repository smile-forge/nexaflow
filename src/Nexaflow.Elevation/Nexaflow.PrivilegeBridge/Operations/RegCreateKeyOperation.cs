using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Creates a key (and any missing parents); no-op if it already exists.</summary>
internal sealed class RegCreateKeyOperation : RegOperationBase
{
    public override string Id => ElevatedOps.RegCreateKey;

    protected override ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args)
    {
        if (string.IsNullOrEmpty(path)) return Fail("Missing key path.");

        using var key = hive.CreateSubKey(path, writable: true)
                        ?? throw new InvalidOperationException($"Could not create key '{path}'.");
        return Ok($"Created key '{path}'.");
    }
}
