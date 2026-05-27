using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Expands user-authored argument / working-directory templates for
/// <see cref="CustomAction"/> against an ordered list of selected file paths.
/// Tokens (case-insensitive):
///   #file, #file[N]              filename with extension (N is 0-based)
///   #filenoext, #filenoext[N]    filename without extension
///   #filepath, #filepath[N]      full path
///   #pathonly, #pathonly[N]      directory of the file
///   %ENV%                        Environment.ExpandEnvironmentVariables
/// Paths that contain whitespace are wrapped in double quotes unless the
/// author has already quoted them.
/// </summary>
public static class ActionTemplateExpander
{
    // Longest alternatives first — regex alternation is left-to-right first-match,
    // so "filepath" must come before "file" to avoid the latter eating the former.
    private static readonly Regex TokenRegex = new(
        @"#(filepath|filenoext|pathonly|file)(?:\[(\d+)\])?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Expand(string template, IReadOnlyList<string> paths)
    {
        if (string.IsNullOrEmpty(template) || paths.Count == 0)
            return template ?? string.Empty;

        // 1. Environment variables first — they can themselves contain # tokens
        //    in pathological cases, but that's fine; we expand # tokens after.
        var s = Environment.ExpandEnvironmentVariables(template);

        // 2. # tokens
        return TokenRegex.Replace(s, m =>
        {
            var token = m.Groups[1].Value.ToLowerInvariant();
            int idx = 0;
            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var parsed))
                idx = parsed;

            if (idx < 0 || idx >= paths.Count) return m.Value;
            var path = paths[idx];

            string replacement = token switch
            {
                "file"      => Path.GetFileName(path),
                "filenoext" => Path.GetFileNameWithoutExtension(path),
                "filepath"  => path,
                "pathonly"  => Path.GetDirectoryName(path) ?? string.Empty,
                _           => m.Value,
            };

            return QuoteIfNeeded(replacement, s, m.Index);
        });
    }

    /// <summary>
    /// Wraps <paramref name="value"/> in double quotes if it contains whitespace
    /// and the author hasn't already quoted the surrounding region.
    /// </summary>
    private static string QuoteIfNeeded(string value, string source, int tokenIndex)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(' ') < 0) return value;
        if (tokenIndex > 0 && source[tokenIndex - 1] == '"') return value;
        return "\"" + value + "\"";
    }
}
