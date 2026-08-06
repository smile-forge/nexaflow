using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.IO.Common;

/// <summary>
/// Shared glob vocabulary used across the app: in-process path/name matching
/// (<see cref="IsMatch"/>), a cheap "does this contain glob chars" check
/// (<see cref="ContainsGlobChars"/>), and glob→SQL <c>LIKE</c> conversion
/// (<see cref="ToSqlLike"/>) for query builders.
///
/// Match tokens (case-insensitive, either <c>\</c> or <c>/</c> accepted as a separator):
///   <c>?</c>  — any single character except a separator
///   <c>*</c>  — any run of characters within a single path segment
///   <c>**</c> — any number of full path segments (e.g. <c>C:\src\**\*.cs</c>
///               matches <c>C:\src\a.cs</c> and <c>C:\src\x\y\a.cs</c>)
/// Everything else is matched literally. Compiled regexes are cached per pattern.
/// </summary>
public static class Glob
{
    private static readonly ConcurrentDictionary<string, Regex> _cache = new();

    /// <summary>True when <paramref name="text"/> (a path or bare file name) matches
    /// <paramref name="pattern"/>. A pattern with no separator matches against a bare name.</summary>
    public static bool IsMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return _cache.GetOrAdd(pattern, Compile).IsMatch(text);
    }

    /// <summary>True when <paramref name="s"/> contains a glob wildcard (<c>*</c> or <c>?</c>).</summary>
    public static bool ContainsGlobChars(string s) => s.IndexOfAny(['*', '?']) >= 0;

    /// <summary>
    /// True when <paramref name="s"/> is a wildcard pattern aimed at a <em>file name or path</em> — it has a
    /// wildcard AND a name's shape: an extension dot or a path separator (<c>*.txt</c>, <c>src/**/*.cs</c>,
    /// <c>*a*b?c.*d*</c>).
    /// <para>
    /// The distinction is what a bare wildcard word is <em>not</em>: <c>*term*</c>, <c>term*</c> and
    /// <c>*term</c> carry no filename shape, so they are ordinary content wildcards matched against a file's
    /// text as well as its name — not a name-only glob. A name-only glob is worth typing precisely because a
    /// folder scan can skip a file by its name without opening it; a wildcard word can't claim that.
    /// </para>
    /// </summary>
    public static bool IsFilenameGlob(string s) =>
        ContainsGlobChars(s) && s.IndexOfAny(['.', '/', '\\']) >= 0;

    /// <summary>
    /// Converts a glob to a SQL <c>LIKE</c> pattern: existing <c>%</c>/<c>_</c> are escaped to literals,
    /// single quotes doubled, then <c>*</c>→<c>%</c> and <c>?</c>→<c>_</c>. For OLE DB / SQL query builders.
    /// </summary>
    public static string ToSqlLike(string glob)
        => glob.Replace("%", "[%]")
               .Replace("_", "[_]")
               .Replace("'", "''")
               .Replace("*", "%")
               .Replace("?", "_");

    /// <summary>
    /// The glob as an anchored regular expression. Public so a caller that already has a regex engine — the
    /// search query language — can express a filename glob without depending on this library or
    /// re-deriving what <c>*</c> and <c>?</c> mean.
    /// </summary>
    public static string ToRegexPattern(string pattern)
    {
        var sb = new StringBuilder("^");
        int i = 0, n = pattern.Length;
        while (i < n)
        {
            char c = pattern[i];
            if (c == '*')
            {
                bool doubleStar = i + 1 < n && pattern[i + 1] == '*';
                if (doubleStar)
                {
                    bool sepAfter = i + 2 < n && (pattern[i + 2] == '\\' || pattern[i + 2] == '/');
                    if (sepAfter)
                    {
                        // "**\" — zero or more whole segments, separator included.
                        sb.Append("(?:[^\\\\/]*[\\\\/])*");
                        i += 3;
                    }
                    else
                    {
                        // trailing "**" — anything, separators included.
                        sb.Append(".*");
                        i += 2;
                    }
                }
                else
                {
                    sb.Append("[^\\\\/]*");
                    i += 1;
                }
            }
            else if (c == '?')
            {
                sb.Append("[^\\\\/]");
                i += 1;
            }
            else if (c == '\\' || c == '/')
            {
                sb.Append("[\\\\/]");
                i += 1;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i += 1;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }

    private static Regex Compile(string pattern)
        => new(ToRegexPattern(pattern),
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
