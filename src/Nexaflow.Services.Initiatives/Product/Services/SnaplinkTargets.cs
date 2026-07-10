using System.Text.RegularExpressions;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Resolves what a snaplink can point <em>at</em> inside a file: markdown heading paths, and code
/// class/method outlines.
/// </summary>
/// <remarks>
/// Shared by the snaplink picker (which <em>offers</em> targets) and <see cref="SnaplinkValidator"/> (which
/// checks a recorded target still exists), so both agree on what "the heading H" or "the method C.M" means.
/// Two implementations would drift and the validator would start reporting phantom breakage.
/// </remarks>
public static class SnaplinkTargets
{
    /// <summary>Files larger than this are treated as unreadable — a snaplink target is never a huge blob.</summary>
    private const long MaxBytes = 2_000_000;

    private static readonly CodeStructureExtractor Extractor = new();
    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.+?)\s*#*$", RegexOptions.Compiled);

    public static bool IsMarkdown(string fileName) =>
        fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>File text, or null when absent, too large, or unreadable.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi is { Exists: true, Length: <= MaxBytes } ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Every heading as (level, text), skipping fenced code blocks.</summary>
    public static IEnumerable<(int Level, string Text)> Headings(string markdown)
    {
        var fenced = false;
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```") || line.StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced) continue;
            var m = HeadingRx.Match(line);
            if (m.Success) yield return (m.Groups[1].Value.Length, m.Groups[2].Value.Trim());
        }
    }

    /// <summary>Each heading's full ancestor path — the shape a markdown snaplink's <c>title_path</c> records.</summary>
    public static IEnumerable<IReadOnlyList<string>> HeadingPaths(string markdown)
    {
        var stack = new List<(int Level, string Text)>();
        foreach (var (level, heading) in Headings(markdown))
        {
            while (stack.Count > 0 && stack[^1].Level >= level) stack.RemoveAt(stack.Count - 1);
            var path = stack.Select(s => s.Text).Append(heading).ToList();
            stack.Add((level, heading));
            yield return path;
        }
    }

    /// <summary>True when the document still contains this heading path (an empty path means "whole file").</summary>
    public static bool HasHeadingPath(string markdown, IReadOnlyList<string> path) =>
        path.Count == 0 || HeadingPaths(markdown).Any(p => p.SequenceEqual(path, StringComparer.Ordinal));

    /// <summary>
    /// The structural outline of a source file, or <c>null</c> when no tree-sitter grammar covers its
    /// extension — a <c>.xaml</c> or <c>.txt</c> snaplink has no verifiable class/method structure, so
    /// callers must treat null as "unverifiable", never as "broken".
    /// </summary>
    public static CodeOutline? Outline(string fileName, string text, string? baseDir) =>
        TreeSitterLanguages.ForFile(fileName) is { } grammar ? Extractor.Extract(grammar, text, baseDir) : null;
}
