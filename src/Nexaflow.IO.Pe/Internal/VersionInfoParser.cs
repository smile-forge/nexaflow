using System.Text;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// VS_VERSIONINFO. The resource is a tree of variable-length blocks that all share one header —
/// length, value length, type, a NUL-terminated UTF-16 key, then 4-byte-aligned padding before the
/// value and again before the children.
/// <para>
/// The trap: <c>wValueLength</c> counts <em>bytes</em> for a binary block but <em>characters</em>
/// for a text block. Reading it as bytes throughout truncates every string to half its length.
/// </para>
/// </summary>
internal static class VersionInfoParser
{
    private const uint FixedFileInfoSignature = 0xFEEF_04BD;

    public static PeVersionInfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6) return PeVersionInfo.Empty;

        if (!TryReadHeader(data, 0, out var root)) return PeVersionInfo.Empty;
        if (root.Key != "VS_VERSION_INFO") return PeVersionInfo.Empty;

        string fileVersion = "", productVersion = "";
        uint   flags = 0, os = 0, type = 0, subtype = 0;

        if (root.ValueLength >= 52 && root.ValueStart + 52 <= data.Length)
        {
            var fixedInfo = data.Slice(root.ValueStart, 52);
            if (U32(fixedInfo, 0) == FixedFileInfoSignature)
            {
                fileVersion    = Version(U32(fixedInfo, 8),  U32(fixedInfo, 12));
                productVersion = Version(U32(fixedInfo, 16), U32(fixedInfo, 20));
                flags   = U32(fixedInfo, 24) & U32(fixedInfo, 28);   // dwFileFlags masked by dwFileFlagsMask
                os      = U32(fixedInfo, 32);
                type    = U32(fixedInfo, 36);
                subtype = U32(fixedInfo, 40);
            }
        }

        var tables = new List<PeVersionStrings>();
        foreach (var child in Children(data, root))
        {
            if (child.Key != "StringFileInfo") continue;
            foreach (var table in Children(data, child))
                if (ReadStringTable(data, table) is { } parsed) tables.Add(parsed);
        }

        return new PeVersionInfo(fileVersion, productVersion, flags, os, type, subtype, tables);
    }

    private static PeVersionStrings? ReadStringTable(ReadOnlySpan<byte> data, Block table)
    {
        // The key is "<langid><codepage>" as 8 hex digits, e.g. "040904B0".
        ushort language = 0, codePage = 0;
        if (table.Key.Length >= 8)
        {
            _ = ushort.TryParse(table.Key.AsSpan(0, 4), System.Globalization.NumberStyles.HexNumber, null, out language);
            _ = ushort.TryParse(table.Key.AsSpan(4, 4), System.Globalization.NumberStyles.HexNumber, null, out codePage);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Children(data, table))
        {
            if (entry.Key.Length == 0) continue;

            // Text values count wValueLength in characters, not bytes.
            int byteLength = entry.IsText ? entry.ValueLength * 2 : entry.ValueLength;
            if (byteLength <= 0 || entry.ValueStart + byteLength > data.Length) continue;

            string text = Encoding.Unicode.GetString(data.Slice(entry.ValueStart, byteLength)).TrimEnd('\0');
            if (text.Length > 0) values[entry.Key] = text;
        }

        return values.Count == 0 ? null : new PeVersionStrings(language, codePage, values);
    }

    // ── Block plumbing ────────────────────────────────────────────────────────

    private readonly record struct Block(
        int Start, int Length, int ValueLength, bool IsText, string Key, int ValueStart, int ChildrenStart);

    private static bool TryReadHeader(ReadOnlySpan<byte> data, int offset, out Block block)
    {
        block = default;
        if (offset < 0 || offset + 6 > data.Length) return false;

        int length      = U16(data, offset);
        int valueLength = U16(data, offset + 2);
        int type        = U16(data, offset + 4);
        if (length < 6 || offset + length > data.Length) return false;

        // The key is NUL-terminated UTF-16 immediately after the 6-byte header.
        int keyStart = offset + 6;
        int cursor   = keyStart;
        while (cursor + 1 < data.Length && !(data[cursor] == 0 && data[cursor + 1] == 0)) cursor += 2;
        if (cursor + 1 >= data.Length) return false;

        string key        = Encoding.Unicode.GetString(data[keyStart..cursor]);
        int    valueStart = Align4(cursor + 2);

        int byteLength    = type == 1 ? valueLength * 2 : valueLength;
        int childrenStart = Align4(valueStart + byteLength);

        block = new Block(offset, length, valueLength, type == 1, key, valueStart, childrenStart);
        return true;
    }

    /// <summary>Every immediate child block of <paramref name="parent"/>.</summary>
    private static List<Block> Children(ReadOnlySpan<byte> data, Block parent)
    {
        var    result = new List<Block>();
        int    cursor = parent.ChildrenStart;
        int    end    = Math.Min(parent.Start + parent.Length, data.Length);

        while (cursor < end)
        {
            if (!TryReadHeader(data, cursor, out var child)) break;
            result.Add(child);

            // A zero-length block would not advance the cursor; stop rather than spin.
            int next = Align4(cursor + child.Length);
            if (next <= cursor) break;
            cursor = next;
        }
        return result;
    }

    private static int    Align4(int value)                       => (value + 3) & ~3;
    private static ushort U16(ReadOnlySpan<byte> d, int o)        => (ushort)(d[o] | (d[o + 1] << 8));
    private static uint   U32(ReadOnlySpan<byte> d, int o)
        => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

    private static string Version(uint most, uint least)
        => $"{most >> 16}.{most & 0xFFFF}.{least >> 16}.{least & 0xFFFF}";
}
