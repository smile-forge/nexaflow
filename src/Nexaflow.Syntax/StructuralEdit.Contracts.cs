using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexaflow.Syntax;

/// <summary>
/// What an edit can have broken elsewhere. A change inside a body is invisible from outside it; a declaration that
/// was removed, renamed or re-signed is not, and its name is how the code that depends on it can be found.
/// </summary>
public static partial class StructuralEdit
{
    /// <summary>
    /// The declarations in <paramref name="before"/> whose outside <paramref name="after"/> changes: gone from where they
    /// were (removed, renamed, moved), or declared differently — a parameter, a return type, a modifier. A declaration
    /// whose body alone changed is not among them. Returned as they stood before, since that is where their uses point.
    /// </summary>
    public static IReadOnlyList<Declaration> ChangedDeclarations(string grammarId, string before, string after)
    {
        if (string.IsNullOrEmpty(grammarId) || TreeSitterLanguages.IsXml(grammarId) || before == after) return [];

        var was  = Declarations(grammarId, before);
        var now  = Declarations(grammarId, after).GroupBy(d => d.AstPath, StringComparer.Ordinal)
                                                 .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var hunk = HunkOf(before, after);
        var last = hunk.Line + Math.Max(hunk.Removed.Count, 1) - 1;

        var anchors = new DeclarationAnchors();
        var changed = new List<Declaration>();
        foreach (var declaration in was)
        {
            if (!now.TryGetValue(declaration.AstPath, out var still))
            {
                changed.Add(declaration);
                continue;
            }

            // Nothing in a declaration that lies wholly outside the changed lines can have changed.
            if (declaration.EndLine < hunk.Line || declaration.Line > last) continue;

            if (Outside(grammarId, before, declaration, anchors) != Outside(grammarId, after, still, anchors))
                changed.Add(declaration);
        }
        return changed;
    }

    /// <summary>
    /// Where the name of the declaration <paramref name="astPath"/> names begins — the position a compiler is asked
    /// about to find the symbol declared there. Null when it cannot be resolved.
    /// </summary>
    public static int? NameStartOf(string grammarId, string source, string astPath, string expectedName) =>
        NameStartsOf(grammarId, source, [(astPath, expectedName)])[0];

    /// <summary>
    /// <see cref="NameStartOf"/> for several declarations of one file, which is parsed once for all of them rather than
    /// twice for each. A path that no longer resolves is found again by name when exactly one declaration has it, as an
    /// edit finds it; otherwise its start is null.
    /// </summary>
    public static IReadOnlyList<int?> NameStartsOf(string grammarId, string source, IReadOnlyList<(string AstPath, string Name)> declarations)
    {
        var starts = new int?[declarations.Count];
        if (string.IsNullOrEmpty(grammarId) || TreeSitterLanguages.IsXml(grammarId) || declarations.Count == 0) return starts;

        var outline = new CodeStructureExtractor().Extract(grammarId, source);
        IReadOnlyList<Declaration>? declared = null;
        var spans = new List<(int Index, string Name, int Line, int EndLine)>();
        for (var i = 0; i < declarations.Count; i++)
        {
            var (astPath, name) = declarations[i];
            if (CodeStructureExtractor.ResolveSpan(outline, astPath) is { } span)
            {
                spans.Add((i, name, span.Line, span.EndLine));
                continue;
            }

            declared ??= DeclarationsIn(outline);
            if (declared.Where(d => d.Name == name).ToList() is [var only]) spans.Add((i, name, only.Line, only.EndLine));
        }

        var anchors = new DeclarationAnchors().FindAll(grammarId, source, [.. spans.Select(s => (s.Name, s.Line, s.EndLine))]);
        for (var s = 0; s < spans.Count; s++) starts[spans[s].Index] = anchors[s]?.NameStart;
        return starts;
    }

    /// <summary>A declaration as seen from outside it — everything before its body, whitespace folded — or the whole
    /// of it when it has no body to leave out.</summary>
    private static string? Outside(string grammarId, string source, Declaration declaration, DeclarationAnchors anchors)
    {
        if (anchors.Find(grammarId, source, declaration.Name, declaration.Line, declaration.EndLine) is not { } anchor)
            return null;

        var (start, end) = anchor.Header ?? (anchor.Start, anchor.End);
        return Regex.Replace(source[start..end], @"\s+", " ").Trim();
    }
}
