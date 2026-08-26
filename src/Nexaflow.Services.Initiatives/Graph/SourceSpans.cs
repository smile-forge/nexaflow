using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Where a declaration's source block starts and stops, taken from the parser that knows the language.
/// <para>
/// This used to be a hand-written C# lexer (<c>BlockEnd</c>) that counted braces while tracking comments,
/// verbatim strings, char literals and raw-string fences of any length - a second, partial C# parser living
/// beside the real one, correct only for C# and only for the shapes someone had thought of. It sat on the hot
/// path of <c>graph grep --mode content</c>, where getting an end wrong produces a false "no match" that is
/// indistinguishable from the term genuinely being absent. Tree-sitter already records both ends of every
/// declaration it extracts, for every grammar; asking it is cheaper than re-deriving it and right by
/// construction.
/// </para>
/// <para>
/// The parse is of the file <i>in hand</i> - the caller's working tree - so a query from a linked worktree
/// describes that branch's code rather than the main checkout the graph was built from. One parse per file is
/// cached, because a grep asks about many nodes of the same file.
/// </para>
/// </summary>
public sealed class SourceSpans
{
    private readonly CodeStructureExtractor _extractor = new();
    private readonly Dictionary<string, FileSpans?> _byFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One file's declarations: by AST path for an exact lookup, and by line for the fallbacks.</summary>
    private sealed record FileSpans(IReadOnlyDictionary<string, (int Line, int EndLine)> ByPath,
                                    IReadOnlyList<(int Line, int EndLine)> Ordered)
    {
        public static FileSpans Of(CodeOutline outline)
        {
            var byPath = CodeStructureExtractor.Spans(outline);
            return new FileSpans(byPath, [.. byPath.Values.OrderBy(s => s.Line).ThenBy(s => s.EndLine)]);
        }
    }

    /// <summary>
    /// The 0-based line range of the declaration <paramref name="astPath"/> names in <paramref name="relPath"/>,
    /// re-resolved against <paramref name="lines"/>. Both ends come from the parse when it can answer, so a file
    /// edited since the graph was built reports where the declaration is <i>now</i>, not where it was.
    /// </summary>
    /// <param name="start0">The 0-based line to fall back to when the parse cannot resolve the path.</param>
    /// <param name="maxLines">Runaway guard - see <see cref="GraphQuery.BlockScanLines"/>.</param>
    public (int Start, int End) Block(string relPath, string[] lines, string? astPath, int start0, int maxLines)
        => Locate(Parse(relPath, lines), lines.Length, astPath, start0, maxLines);

    /// <summary>The same, for a caller that has already parsed the file (the snaplink resolver, which needs the
    /// outline anyway to find a member by name).</summary>
    public static (int Start, int End) BlockOf(CodeOutline? outline, int lineCount, string? astPath,
                                               int start0, int maxLines)
        => Locate(outline is null ? null : FileSpans.Of(outline), lineCount, astPath, start0, maxLines);

    /// <summary>The span the parser records for <paramref name="astPath"/>, 1-based, or null when the path no
    /// longer resolves - which is a real answer ("regenerate the graph"), not a case to paper over.</summary>
    public (int Line, int EndLine)? Resolve(string relPath, string[] lines, string astPath)
        => Parse(relPath, lines) is { } f && f.ByPath.TryGetValue(astPath, out var span) ? span : null;

    private static (int Start, int End) Locate(FileSpans? file, int lineCount, string? astPath,
                                               int start0, int maxLines)
    {
        if (file is not null && astPath is { Length: > 0 } && file.ByPath.TryGetValue(astPath, out var span))
        {
            if (span.EndLine > 0) return Clamp(span.Line - 1, span.EndLine - 1, lineCount, maxLines);

            // The parse placed the declaration but its extractor records no end (Razor's synthetic @code type
            // is one). Its START is exact, so it replaces whatever the caller guessed - two of the three call
            // sites pass 0, having left the start to the parse. For the end, take whichever bound comes first:
            // the next declaration to begin, or the end of whatever contains this one. Neither is always the
            // tighter of the two - the next declaration wins between siblings, the container wins for the last
            // member of a type - and both are places the parser actually saw.
            var at = span.Line - 1;
            return Clamp(at, Tighter(PrecedingNext(file, at), Enclosing(file, at)) ?? lineCount - 1,
                         lineCount, maxLines);
        }

        // Nothing resolved: a stale graph against a file edited since. The recorded line is all there is, and
        // the same parse still knows every OTHER declaration - so the block runs to the end of the innermost
        // one around that line, or, failing that, stops just before the next one begins. Enclosing leads here
        // because the line is presumed to be INSIDE something, and that something is the block being asked for.
        var end = file is null ? null : Enclosing(file, start0) ?? PrecedingNext(file, start0);
        return Clamp(start0, end ?? lineCount - 1, lineCount, maxLines);
    }

    /// <summary>The 0-based end of the tightest declaration spanning <paramref name="start0"/>.</summary>
    private static int? Enclosing(FileSpans file, int start0)
    {
        int? best = null;
        foreach (var (line, endLine) in file.Ordered)
            if (endLine > 0 && line - 1 <= start0 && endLine - 1 >= start0
                && (best is null || endLine - 1 < best)) best = endLine - 1;
        return best;
    }

    /// <summary>Whichever bound stops sooner, when both exist.</summary>
    private static int? Tighter(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    /// <summary>The 0-based line before the next declaration to start after <paramref name="start0"/>.</summary>
    private static int? PrecedingNext(FileSpans file, int start0)
    {
        foreach (var (line, _) in file.Ordered)
            if (line - 1 > start0) return line - 2;
        return null;
    }

    private static (int Start, int End) Clamp(int start, int end, int lineCount, int maxLines)
    {
        var s = Math.Clamp(start, 0, Math.Max(0, lineCount - 1));
        return (s, Math.Clamp(end, s, Math.Min(lineCount - 1, s + maxLines)));
    }

    private FileSpans? Parse(string relPath, string[] lines)
    {
        if (_byFile.TryGetValue(relPath, out var cached)) return cached;
        var grammar = TreeSitterLanguages.ForFile(relPath);
        var outline = grammar is null ? null : _extractor.Extract(grammar, string.Join('\n', lines));
        return _byFile[relPath] = outline is { HasContent: true } ? FileSpans.Of(outline) : null;
    }
}
