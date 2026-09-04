namespace Nexaflow.IO.Common;

/// <summary>
/// Win32 extended-length path prefixing. Windows caps a path at <c>MAX_PATH</c> (260) unless it is
/// given in the <c>\?\</c> form, and the app manifest's <c>longPathAware</c> opt-in does not cover
/// every API — so the copy engine prefixes at the file-system boundary rather than trusting either.
/// Prefixed paths are never shown to a user: <see cref="Display"/> takes the prefix back off before
/// a path reaches a message or a progress line.
/// </summary>
public static class LongPath
{
    private const string Prefixed    = @"\?\";
    private const string PrefixedUnc = @"\?\UNC\";

    /// <summary>The length at which prefixing starts. Below it a plain path is left exactly as given,
    /// so nothing changes for the overwhelming majority of paths.</summary>
    private const int Threshold = 250;

    /// <summary>
    /// Returns <paramref name="path"/> in a form Win32 will accept at any length. Relative paths,
    /// device paths and already-prefixed paths are returned untouched — the prefix only means
    /// anything on a fully-qualified one, and applying it to a relative path silently breaks it.
    /// </summary>
    public static string Prefix(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(Prefixed, StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\.\", StringComparison.Ordinal)) return path;
        if (path.Length < Threshold) return path;
        if (!Path.IsPathFullyQualified(path)) return path;

        // A UNC path takes the other form: \server\share -> \?\UNC\server\share.
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? PrefixedUnc + path[2..]
            : Prefixed + path;
    }

    /// <summary>The inverse of <see cref="Prefix"/>: the path as a human would write it.</summary>
    public static string Display(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(PrefixedUnc, StringComparison.Ordinal)) return @"\\" + path[PrefixedUnc.Length..];
        if (path.StartsWith(Prefixed, StringComparison.Ordinal))    return path[Prefixed.Length..];
        return path;
    }
}
