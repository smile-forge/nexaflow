using System.Globalization;
using System.Security;
using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Shared plumbing for elevated registry handlers: resolves the hive token, opens the target key, maps
/// the wire value type/data, and turns access/security failures into a clean failed result. Only the
/// three user-facing hives are reachable (HKCU | HKLM | HKCR).
/// <para>
/// The value wire format here MUST stay byte-for-byte identical to the feature's in-process codec
/// (<c>Nexaflow.Features.WindowsRegistry</c> <c>RegistryValueCodec</c>) so a write produces the same
/// bytes whether it ran in-process or via the bridge. Keep the two in sync.
/// </para>
/// </summary>
internal abstract class RegOperationBase : IElevatedOperation
{
    public abstract string Id { get; }

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        var hiveToken = args.GetValueOrDefault(ElevatedArgs.RegHive);
        if (!TryResolveHive(hiveToken, out var hive))
            return Fail($"Unknown registry hive '{hiveToken}'.");

        var path = args.GetValueOrDefault(ElevatedArgs.RegPath) ?? "";

        try
        {
            return Run(hive!, path, args);
        }
        catch (UnauthorizedAccessException ex) { return Fail($"Access denied: {ex.Message}"); }
        catch (SecurityException ex)            { return Fail($"Access denied: {ex.Message}"); }
        catch (Exception ex)                    { return Fail(ex.Message); }
    }

    /// <summary>Performs the privileged registry action. <paramref name="path"/> is the key path under the hive.</summary>
    protected abstract ElevatedOperationResult Run(
        RegistryKey hive, string path, IReadOnlyDictionary<string, string> args);

    // ── Helpers shared by handlers ───────────────────────────────────────────

    protected ElevatedOperationResult Ok(string message) => ElevatedOperationResult.Ok(Id, message);

    protected ElevatedOperationResult Fail(string message) =>
        ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, message);

    private static bool TryResolveHive(string? token, out RegistryKey? hive)
    {
        hive = token switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            "HKCR" => Registry.ClassesRoot,
            _      => null,
        };
        return hive is not null;
    }

    /// <summary>Decodes the wire (<paramref name="typeToken"/>, <paramref name="data"/>) into the object
    /// <see cref="RegistryKey.SetValue(string, object, RegistryValueKind)"/> expects, plus the kind.</summary>
    protected static (object Data, RegistryValueKind Kind) DecodeValue(string? typeToken, string? data)
    {
        var raw = data ?? "";
        if (!Enum.TryParse<RegistryValueKind>(typeToken, ignoreCase: true, out var kind))
            kind = RegistryValueKind.String;

        object value = kind switch
        {
            RegistryValueKind.DWord       => DecodeDword(raw),
            RegistryValueKind.QWord       => DecodeQword(raw),
            RegistryValueKind.Binary      => raw.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(raw),
            RegistryValueKind.MultiString => raw.Length == 0 ? Array.Empty<string>() : raw.Split('\0'),
            _                             => raw,   // String / ExpandString / None
        };
        return (value, kind);
    }

    private static int DecodeDword(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : unchecked((int)uint.Parse(s, CultureInfo.InvariantCulture));

    private static long DecodeQword(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l
            : unchecked((long)ulong.Parse(s, CultureInfo.InvariantCulture));
}
