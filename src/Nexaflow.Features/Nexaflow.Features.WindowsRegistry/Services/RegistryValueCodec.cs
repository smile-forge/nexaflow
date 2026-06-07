using System.Globalization;
using Microsoft.Win32;

namespace Nexaflow.Features.WindowsRegistry.Services;

/// <summary>
/// Converts registry value data between the live .NET object, a human display string, the user's
/// edit-box text, and the string "wire" form sent to the elevation bridge.
/// <para>
/// The wire form (<see cref="Encode"/> / <see cref="Decode"/>) MUST stay byte-for-byte identical to the
/// bridge's <c>RegOperationBase.DecodeValue</c> so an edit produces the same bytes whether it ran
/// in-process or elevated. Keep the two in sync.
/// </para>
/// </summary>
public static class RegistryValueCodec
{
    // ── Type label (REG_*) ───────────────────────────────────────────────────
    public static string TypeLabel(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String       => "REG_SZ",
        RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
        RegistryValueKind.DWord        => "REG_DWORD",
        RegistryValueKind.QWord        => "REG_QWORD",
        RegistryValueKind.Binary       => "REG_BINARY",
        RegistryValueKind.MultiString  => "REG_MULTI_SZ",
        RegistryValueKind.None         => "REG_NONE",
        _                              => "REG_SZ",
    };

    /// <summary>The value kinds offered when creating a new value, in menu order.</summary>
    public static readonly IReadOnlyList<RegistryValueKind> EditableKinds =
    [
        RegistryValueKind.String, RegistryValueKind.ExpandString, RegistryValueKind.DWord,
        RegistryValueKind.QWord, RegistryValueKind.Binary, RegistryValueKind.MultiString,
    ];

    // ── Display ───────────────────────────────────────────────────────────────
    public static string FormatForDisplay(object? data, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord       => $"0x{(uint)Convert.ToInt32(data):x8} ({Convert.ToInt32(data)})",
        RegistryValueKind.QWord       => $"0x{(ulong)Convert.ToInt64(data):x16} ({Convert.ToInt64(data)})",
        RegistryValueKind.Binary      => data is byte[] b ? string.Join(' ', b.Select(x => x.ToString("x2"))) : "",
        RegistryValueKind.MultiString => data is string[] s ? string.Join(", ", s) : "",
        _                             => data?.ToString() ?? "",
    };

    /// <summary>Edit-box seed text for an existing value (round-trips through <see cref="ParseUserInput"/>).</summary>
    public static string ToEditText(object? data, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord       => Convert.ToInt32(data).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord       => Convert.ToInt64(data).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary      => data is byte[] b ? Convert.ToHexString(b) : "",
        RegistryValueKind.MultiString => data is string[] s ? string.Join(Environment.NewLine, s) : "",
        _                             => data?.ToString() ?? "",
    };

    // ── Wire form (shared with the bridge) ─────────────────────────────────────
    public static string Encode(object? data, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord       => Convert.ToInt32(data).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord       => Convert.ToInt64(data).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary      => data is byte[] b ? Convert.ToHexString(b) : "",
        RegistryValueKind.MultiString => data is string[] s ? string.Join('\0', s) : "",
        _                             => data?.ToString() ?? "",
    };

    public static (object Data, RegistryValueKind Kind) Decode(string? typeToken, string? wire)
    {
        var raw = wire ?? "";
        if (!Enum.TryParse<RegistryValueKind>(typeToken, ignoreCase: true, out var kind))
            kind = RegistryValueKind.String;

        object value = kind switch
        {
            RegistryValueKind.DWord       => DecodeDword(raw),
            RegistryValueKind.QWord       => DecodeQword(raw),
            RegistryValueKind.Binary      => raw.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(raw),
            RegistryValueKind.MultiString => raw.Length == 0 ? Array.Empty<string>() : raw.Split('\0'),
            _                             => raw,
        };
        return (value, kind);
    }

    // ── User edit text → wire form (throws FormatException on bad input) ────────
    public static string ParseUserInput(string text, RegistryValueKind kind)
    {
        text ??= "";
        return kind switch
        {
            RegistryValueKind.DWord       => ParseInt(text).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord       => ParseLong(text).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.Binary      => Convert.ToHexString(ParseHexBytes(text)),
            RegistryValueKind.MultiString => string.Join('\0',
                                              text.ReplaceLineEndings("\n").Split('\n')),
            _                             => text,
        };
    }

    // ── helpers ─────────────────────────────────────────────────────────────────
    private static int DecodeDword(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : unchecked((int)uint.Parse(s, CultureInfo.InvariantCulture));

    private static long DecodeQword(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l : unchecked((long)ulong.Parse(s, CultureInfo.InvariantCulture));

    private static int ParseInt(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((int)uint.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : unchecked((int)uint.Parse(text, CultureInfo.InvariantCulture));
    }

    private static long ParseLong(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((long)ulong.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l : unchecked((long)ulong.Parse(text, CultureInfo.InvariantCulture));
    }

    private static byte[] ParseHexBytes(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact.Length == 0 ? [] : Convert.FromHexString(compact);
    }
}
