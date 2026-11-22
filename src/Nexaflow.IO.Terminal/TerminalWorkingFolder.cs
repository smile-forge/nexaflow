using System.Text;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// The folder a shell says it works in, as it reports it at a prompt (OSC 7, <c>file://host/path</c>): the host it names
/// and the path on that host, both percent-decoded and read as UTF-8. It is a claim — any output can print one — so nothing
/// that grants access follows it: that asks the shell's host
/// (<see cref="Nexaflow.IO.Common.Terminal.ITerminalTransport.GetWorkingDirectoryAsync"/>).
/// </summary>
public sealed record TerminalWorkingFolder(string Host, string Path)
{
    private const string Scheme = "file://";

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads a <c>file://host/path</c> URI; false when it is not one, or a percent escape or its bytes are not
    /// UTF-8 — a report the terminal cannot read is no report.</summary>
    public static bool TryParse(string uri, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TerminalWorkingFolder? folder)
    {
        folder = null;
        if (!uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        int slash = uri.IndexOf('/', Scheme.Length);
        if (slash < 0) return false;
        if (!TryDecode(uri.AsSpan(Scheme.Length, slash - Scheme.Length), out var host)
            || !TryDecode(uri.AsSpan(slash), out var path)) return false;

        folder = new TerminalWorkingFolder(host, path);
        return true;
    }

    /// <summary>Percent-decodes <paramref name="text"/> to bytes and reads them as UTF-8; a character not escaped stands
    /// for its own UTF-8 bytes.</summary>
    private static bool TryDecode(ReadOnlySpan<char> text, out string decoded)
    {
        decoded = string.Empty;
        var bytes = new List<byte>(text.Length);
        Span<byte> encoded = stackalloc byte[4];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '%')
            {
                if (i + 2 >= text.Length || !byte.TryParse(text.Slice(i + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier,
                                                           System.Globalization.CultureInfo.InvariantCulture, out var value))
                    return false;
                bytes.Add(value);
                i += 2;
                continue;
            }
            if (Rune.DecodeFromUtf16(text[i..], out var rune, out int used) != System.Buffers.OperationStatus.Done) return false;
            int length = rune.EncodeToUtf8(encoded);
            for (int b = 0; b < length; b++) bytes.Add(encoded[b]);
            i += used - 1;
        }

        try { decoded = Strict.GetString([.. bytes]); }
        catch (DecoderFallbackException) { return false; }
        return true;
    }
}
