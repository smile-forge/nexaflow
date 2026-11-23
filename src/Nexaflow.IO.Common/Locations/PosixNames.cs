using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A POSIX name is bytes, which need not be UTF-8: <c>caf</c> and a stray <c>0xE9</c> is a name Linux holds and Windows
/// cannot. This is how such a name is held in a .NET string and given back, byte for byte: decoded as UTF-8, with each
/// byte that is no part of a character held as the lone surrogate U+DC80 + byte, as Python's <c>surrogateescape</c> holds
/// it. Valid UTF-8 never decodes to a lone surrogate, so the two forms map one to one. Compiled into the WSL helper too, so
/// both ends of its wire agree.
/// </summary>
public static class PosixNames
{
    private const char FirstStray = '\uDC80';
    private const char LastStray  = '\uDCFF';

    /// <summary>The name the bytes are, each stray byte held as a lone surrogate.</summary>
    public static string FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (Ascii.IsValid(bytes)) return Encoding.ASCII.GetString(bytes);
        var text = new StringBuilder(bytes.Length);
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done)
            {
                text.Append(rune.ToString());
                bytes = bytes[consumed..];
                continue;
            }
            text.Append((char)(FirstStray - 0x80 + bytes[0]));
            bytes = bytes[1..];
        }
        return text.ToString();
    }

    /// <summary>How many bytes <see cref="GetBytes(ReadOnlySpan{char}, Span{byte})"/> writes for <paramref name="name"/>.</summary>
    public static int GetByteCount(ReadOnlySpan<char> name)
    {
        int count = 0;
        for (int i = 0; i < name.Length;)
        {
            if (IsStrayAt(name, i, out _)) { count++; i++; continue; }
            count += Next(name, ref i).Utf8SequenceLength;
        }
        return count;
    }

    /// <summary>Writes the bytes <paramref name="name"/> is and returns how many: each character as UTF-8, each stray byte
    /// as itself. A lone surrogate that stands for no byte is written as U+FFFD, the character no name can hold.</summary>
    public static int GetBytes(ReadOnlySpan<char> name, Span<byte> bytes)
    {
        int written = 0;
        for (int i = 0; i < name.Length;)
        {
            if (IsStrayAt(name, i, out byte stray)) { bytes[written++] = stray; i++; continue; }
            written += Next(name, ref i).EncodeToUtf8(bytes[written..]);
        }
        return written;
    }

    /// <summary>The bytes <paramref name="name"/> is.</summary>
    public static byte[] GetBytes(string name)
    {
        var bytes = new byte[GetByteCount(name)];
        GetBytes(name, bytes);
        return bytes;
    }

    /// <summary>True when the name holds a byte that is no part of a character — one Windows has no name for.</summary>
    public static bool HoldsStrayBytes(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < name.Length; i++)
            if (IsStrayAt(name, i, out _)) return true;
        return false;
    }

    /// <summary>The name as a person reads it: each stray byte as <c>\xHH</c>, as <c>ls -b</c> shows it.</summary>
    public static string Shown(string name)
    {
        if (!HoldsStrayBytes(name)) return name;
        var shown = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (IsStrayAt(name, i, out byte stray)) shown.Append(@"\x").Append(stray.ToString("X2", CultureInfo.InvariantCulture));
            else shown.Append(name[i]);
        }
        return shown.ToString();
    }

    /// <summary>A path as an address writes it, read back exactly by <see cref="FromAddressed"/>: each stray byte as
    /// <c>\xHH</c>, and a backslash in a name as <c>\\</c> so it is never taken for one.</summary>
    public static string Addressed(string path)
    {
        if (!HoldsStrayBytes(path) && !path.Contains('\\')) return path;
        var addressed = new StringBuilder(path.Length + 8);
        for (int i = 0; i < path.Length; i++)
        {
            if (IsStrayAt(path, i, out byte stray)) addressed.Append(@"\x").Append(stray.ToString("X2", CultureInfo.InvariantCulture));
            else if (path[i] == '\\') addressed.Append(@"\\");
            else addressed.Append(path[i]);
        }
        return addressed.ToString();
    }

    /// <summary>The path an address names (<see cref="Addressed"/>). A backslash that starts neither <c>\\</c> nor
    /// <c>\xHH</c> is a backslash.</summary>
    public static string FromAddressed(string text)
    {
        if (!text.Contains('\\')) return text;
        var path = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { path.Append(text[i]); continue; }
            if (text[i + 1] == '\\') { path.Append('\\'); i++; continue; }
            if (text[i + 1] is 'x' or 'X' && i + 3 < text.Length
                && byte.TryParse(text.AsSpan(i + 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value)
                && value >= 0x80)
            {
                path.Append((char)(FirstStray - 0x80 + value));
                i += 3;
                continue;
            }
            path.Append('\\');
        }
        return path.ToString();
    }

    /// <summary>
    /// The name as an argument to <c>printf %b</c>, which gives back its exact bytes inside a POSIX shell however the
    /// argument reached it: printable ASCII as itself, a backslash as <c>\\</c>, and every other byte as <c>\0ooo</c>.
    /// Every character of it is ASCII, so it crosses any command line Windows builds unchanged.
    /// </summary>
    public static string ForPrintf(string name)
    {
        var escaped = new StringBuilder(name.Length + 8);
        foreach (byte b in GetBytes(name))
        {
            if (b == (byte)'\\') escaped.Append(@"\\");
            else if (b is >= 0x20 and < 0x7F) escaped.Append((char)b);
            else escaped.Append(@"\0").Append(Convert.ToString(b, 8).PadLeft(3, '0'));
        }
        return escaped.ToString();
    }

    // A stray byte is a lone low surrogate in U+DC80..U+DCFF: one no high surrogate stands before.
    private static bool IsStrayAt(ReadOnlySpan<char> name, int index, out byte stray)
    {
        char c = name[index];
        stray = (byte)(c - FirstStray + 0x80);
        return c is >= FirstStray and <= LastStray && (index == 0 || !char.IsHighSurrogate(name[index - 1]));
    }

    // The character at i, moving past it; a lone surrogate that is no stray byte reads as U+FFFD.
    private static Rune Next(ReadOnlySpan<char> name, ref int i)
    {
        if (Rune.DecodeFromUtf16(name[i..], out Rune rune, out int consumed) != System.Buffers.OperationStatus.Done)
        {
            i++;
            return Rune.ReplacementChar;
        }
        i += consumed;
        return rune;
    }
}
