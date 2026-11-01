using System.Text.RegularExpressions;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
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

    /// <summary>
    /// The outline of a markdown document (<see cref="ContentOutline"/>): its headings, ranked, titled and named, read as the Markdown tab
    /// reads them — so what a snaplink is made from, what it is judged by and where it lands are one reading.
    /// </summary>
    public static ContentOutline MarkdownOutline(string markdown) => ContentOutline.Of(MarkdownParser.Worked(markdown));

    /// <summary>Each heading's full ancestor path — the shape a markdown snaplink's <c>title_path</c> records.</summary>
    public static IEnumerable<IReadOnlyList<string>> HeadingPaths(string markdown) =>
        MarkdownOutline(markdown).TitlePaths().Select(heading => heading.Titles);

    /// <summary>
    /// True when <paramref name="outline"/> still holds this heading path (an empty path means "whole file") — each title read as the
    /// markdown it was written as, and matched as the Markdown tab finds it, so a link judged sound is one that lands.
    /// </summary>
    public static bool HasHeadingPath(ContentOutline outline, IReadOnlyList<string> path) =>
        path.Count == 0 || outline.Titled([.. path.Select(WithOutline.Titled)]) is not null;

    /// <summary>
    /// The structural outline of a source file, or <c>null</c> when no tree-sitter grammar covers its
    /// extension — a <c>.xaml</c> or <c>.txt</c> snaplink has no verifiable class/method structure, so
    /// callers must treat null as "unverifiable", never as "broken".
    /// </summary>
    public static CodeOutline? Outline(string fileName, string text, string? baseDir) =>
        TreeSitterLanguages.ForFile(fileName) is { } grammar ? Extractor.Extract(grammar, text, baseDir) : null;
}
