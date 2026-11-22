using System.Text;

namespace Nexaflow.Interaction;

/// <summary>
/// A web address out of what applications hand over as one: a string, or the bytes browsers use — UTF-16 or ASCII,
/// often NUL-terminated, Firefox's with the page's title on a second line. Only http and https are addresses here: any
/// other scheme could launch something.
/// </summary>
public static class WebLinks
{
    /// <summary>The address in <paramref name="carried"/> by a link format, or null.</summary>
    public static string? Decode(object? carried)
    {
        var raw = carried switch
        {
            string text         => text,
            MemoryStream stream => DecodeBytes(stream.ToArray()),
            byte[] bytes        => DecodeBytes(bytes),
            _                   => null,
        };
        return raw is null ? null : SingleAddress(raw.Split('\n', '\r')[0]);
    }

    /// <summary><paramref name="text"/> where the whole of it is one address, or null.</summary>
    public static string? SingleAddress(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsWhiteSpace)) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var address)
               && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    private static string DecodeBytes(byte[] bytes)
    {
        // An address starts with ASCII, so UTF-16 has a NUL for its second byte.
        var text = bytes.Length >= 2 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }
}
