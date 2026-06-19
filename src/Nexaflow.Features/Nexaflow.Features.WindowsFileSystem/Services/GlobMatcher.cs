using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Matches a full file path against a glob pattern. Shared by
/// <see cref="FileMapManager"/> (PathPattern criteria) and
/// <see cref="ExternalAppRegistry"/> so the two stay in lock-step.
///
/// Tokens (case-insensitive, either <c>\</c> or <c>/</c> accepted as a separator):
///   <c>?</c>  — any single character except a separator
///   <c>*</c>  — any run of characters within a single path segment
///   <c>**</c> — any number of full path segments (e.g. <c>C:\src\**\*.cs</c>
///               matches <c>C:\src\a.cs</c> and <c>C:\src\x\y\a.cs</c>)
/// Everything else is matched literally. Compiled regexes are cached per pattern.
/// </summary>
public static class GlobMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> _cache = new();

    /// <summary>True when <paramref name="path"/> matches <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return _cache.GetOrAdd(pattern, Compile).IsMatch(path);
    }

    private static Regex Compile(string pattern)
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
        return new Regex(sb.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
