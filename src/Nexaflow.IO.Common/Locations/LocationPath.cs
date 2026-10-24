using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// The rules that let a remote name live inside a Windows-shaped path, and the display form people type.
/// <para>
/// A remote folder keeps the <c>::{id}\…</c> form mounts use, so every string-path caller carries it unchanged. A
/// POSIX name may hold characters no Windows path segment can (<c>\ : * ? " &lt; &gt; |</c>, control characters) or end
/// in a dot or a space; those are percent-encoded in the segment and decoded at the provider boundary. <c>%</c> is
/// encoded too, so decoding is never ambiguous and the round trip is lossless. A name with none of them is its own
/// segment.
/// </para>
/// </summary>
public static class LocationPath
{
    private const char Escape = '%';
    private const string Reserved = "\\:*?\"<>|%";

    /// <summary>True when <paramref name="id"/> can form the single segment of a <c>::{id}</c> root — the rule
    /// <see cref="IVirtualFileSystem.RegisterMount"/> applies to mount ids.</summary>
    public static bool IsSegmentSafeId(string id)
        => !string.IsNullOrWhiteSpace(id) && id.AsSpan().IndexOfAny(@"\/:") < 0;

    /// <summary>A remote name as a Windows path segment.</summary>
    public static string EncodeSegment(string name)
    {
        int last = name.Length - 1;
        StringBuilder? sb = null;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool encode = c < ' ' || Reserved.IndexOf(c) >= 0 || (i == last && (c == '.' || c == ' '));
            if (!encode) { sb?.Append(c); continue; }
            sb ??= new StringBuilder(name.Length + 8).Append(name, 0, i);
            sb.Append(Escape).Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb?.ToString() ?? name;
    }

    /// <summary>The remote name a segment stands for — the inverse of <see cref="EncodeSegment"/>. A <c>%</c> not
    /// followed by two hex digits is kept as it is, so a hand-typed segment decodes to what was typed.</summary>
    public static string DecodeSegment(string segment)
    {
        int at = segment.IndexOf(Escape);
        if (at < 0) return segment;

        var sb = new StringBuilder(segment.Length).Append(segment, 0, at);
        for (int i = at; i < segment.Length; i++)
        {
            if (segment[i] == Escape && i + 2 < segment.Length
                && int.TryParse(segment.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 2;
            }
            else sb.Append(segment[i]);
        }
        return sb.ToString();
    }

    /// <summary>The provider path for what follows <c>::{id}\</c> — <c>home\adam\a%3Ab.md</c> becomes
    /// <c>/home/adam/a:b.md</c>; empty is the root, <c>/</c>.</summary>
    public static string ToProviderPath(string remainder)
    {
        var segments = remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "/";
        var sb = new StringBuilder(remainder.Length + 1);
        foreach (var s in segments) sb.Append('/').Append(DecodeSegment(s));
        return sb.ToString();
    }

    /// <summary>The inverse of <see cref="ToProviderPath"/>: what follows <c>::{id}\</c> for a provider path, with
    /// every name encoded; empty for the root.</summary>
    public static string ToRemainder(string providerPath)
    {
        var names = providerPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\\', names.Select(EncodeSegment));
    }

    /// <summary>The comparison path names on a location with these capabilities need: ordinal where names differ by
    /// case (<see cref="LocationCapabilities.CaseSensitive"/>), ordinal ignoring case otherwise.</summary>
    public static StringComparison Comparison(LocationCapabilities capabilities)
        => (capabilities & LocationCapabilities.CaseSensitive) != 0
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>The comparer for <see cref="Comparison"/>, for sets and dictionaries keyed by path.</summary>
    public static StringComparer Comparer(LocationCapabilities capabilities)
        => (capabilities & LocationCapabilities.CaseSensitive) != 0
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The display form of a provider path on a connection — <c>sftp://adam@nas/home/adam/notes.md</c>,
    /// <c>wsl://Ubuntu/home/adam</c> — as the address bar shows it and <see cref="TryParseDisplayForm"/> reads it back.
    /// Names are shown as the server holds them: the display form is for people, not for a URI parser.
    /// </summary>
    public static string ToDisplayForm(ConnectionProfile connection, string providerPath)
    {
        var sb = new StringBuilder(connection.Kind.Length + connection.Host.Length + providerPath.Length + 8)
            .Append(connection.Kind).Append("://");
        if (!string.IsNullOrEmpty(connection.User)) sb.Append(connection.User).Append('@');
        sb.Append(connection.Host);
        if (connection.Port is { } port) sb.Append(':').Append(port.ToString(CultureInfo.InvariantCulture));
        sb.Append(providerPath.Length == 0 ? "/" : providerPath);
        return sb.ToString();
    }

    /// <summary>
    /// Reads a display form typed or pasted into the address bar. False when the text is not one — a Windows path,
    /// a plain word — so the caller tries its other readings. The user part runs to the <b>last</b> <c>@</c> before
    /// the host, so a sign-in that is itself an e-mail address reads correctly.
    /// </summary>
    public static bool TryParseDisplayForm(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocationAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0) return false;
        var kind = text[..scheme];
        if (!kind.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return false;

        var rest  = text[(scheme + 3)..];
        int slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var path      = slash < 0 ? "/" : rest[slash..];

        int at = authority.LastIndexOf('@');
        var user     = at < 0 ? null : authority[..at];
        var hostPort = at < 0 ? authority : authority[(at + 1)..];

        int? port = null;
        int colon = hostPort.LastIndexOf(':');
        if (colon >= 0 && hostPort.IndexOf(']') < colon)
        {
            if (!int.TryParse(hostPort.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int p)
                || p is < 1 or > 65535)
                return false;
            port     = p;
            hostPort = hostPort[..colon];
        }
        if (hostPort.Length == 0) return false;

        address = new LocationAddress(kind.ToLowerInvariant(), string.IsNullOrEmpty(user) ? null : user, hostPort, port, path);
        return true;
    }

    /// <summary>True when <paramref name="address"/> names a place on <paramref name="connection"/>: the same kind
    /// and host, and the same user and port where the address gives them.</summary>
    public static bool Matches(LocationAddress address, ConnectionProfile connection)
        => string.Equals(address.Kind, connection.Kind, StringComparison.OrdinalIgnoreCase)
           && string.Equals(address.Host, connection.Host, StringComparison.OrdinalIgnoreCase)
           && (address.User is null || string.Equals(address.User, connection.User, StringComparison.Ordinal))
           && (address.Port is null || address.Port == connection.Port);
}

/// <summary>A display form read back into its parts.</summary>
/// <param name="Kind">The scheme, lower-case — the <see cref="ConnectionProfile.Kind"/> it names.</param>
/// <param name="User">The user before the <c>@</c>, or null when none was given.</param>
/// <param name="Port">The port after the host, or null when none was given.</param>
/// <param name="Path">The provider path, always starting with <c>/</c>.</param>
public sealed record LocationAddress(string Kind, string? User, string Host, int? Port, string Path);
