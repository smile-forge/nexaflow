using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Where names appear in the repository's code and views, each place credited to the declaration that holds it.
/// <para>
/// By spelling, on purpose, and so a superset: a mention of <c>Plan</c> may be a different <c>Plan</c>. That is the
/// right shape for the question it answers — which files could an edit to <c>Plan</c> have broken — because the
/// compiler then settles which of them actually did. The graph's call edges are the other way to ask, and they are
/// resolved by name across files only on a full build, so right after an edit they know least about the code that
/// was just changed.
/// </para>
/// </summary>
public static class GraphMentions
{
    /// <summary>One line naming one of the names: where, what the line says, and the declaration it sits in.</summary>
    public sealed record Mention(string RelativePath, int Line, string Text, GraphNode? Owner);

    /// <summary>The kinds of file a C# name can be used from.</summary>
    private static readonly string[] Extensions = [".cs", ".xaml"];

    /// <summary>
    /// Every line of <paramref name="files"/> — the C# and XAML among them — naming any of <paramref name="names"/> as a
    /// whole word, in path order. The files come from the caller rather than from the graph's file list, which does not
    /// yet hold a file created a moment ago or a project added since the last build.
    /// </summary>
    public static IReadOnlyList<Mention> Of(KnowledgeGraph graph, IReadOnlyCollection<string> names,
                                        IEnumerable<string> files, Func<string, string?> read, int limit = 2000)
    {
        var wanted = names.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return [];

        var word = new Regex(@"(?<![\w@])(?:" + string.Join("|", wanted.Select(Regex.Escape)) + @")(?!\w)",
                             RegexOptions.CultureInvariant);

        var declarations = graph.Nodes
            .Where(n => n.FilePath is { Length: > 0 } && n.Type is NodeType.Type or NodeType.Member)
            .GroupBy(n => n.FilePath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var found = new List<Mention>();
        foreach (var rel in files.Where(f => Extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (read(rel) is not { } text || !wanted.Any(n => text.Contains(n, StringComparison.Ordinal))) continue;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!word.IsMatch(lines[i])) continue;

                found.Add(new Mention(rel, i + 1, lines[i].Trim(), Innermost(declarations.GetValueOrDefault(rel), i + 1)));
                if (found.Count >= limit) return found;
            }
        }
        return found;
    }

    /// <summary>The narrowest declaration the graph records at <paramref name="line"/> of <paramref name="relativePath"/>.</summary>
    public static GraphNode? OwnerOf(KnowledgeGraph graph, string relativePath, int line) =>
        Innermost([.. graph.Nodes.Where(n => n.Type is NodeType.Type or NodeType.Member
                                          && string.Equals(n.FilePath, relativePath, StringComparison.OrdinalIgnoreCase))], line);

    /// <summary>The narrowest declaration whose lines hold <paramref name="line"/>.</summary>
    private static GraphNode? Innermost(List<GraphNode>? candidates, int line)
    {
        GraphNode? best = null;
        var bestSpan = int.MaxValue;
        foreach (var node in candidates ?? [])
        {
            if (!int.TryParse(node.Metadata?.GetValueOrDefault("line"), out var start)) continue;
            var end = int.TryParse(node.Metadata?.GetValueOrDefault("endLine"), out var e) ? e : start;
            if (line < start || line > end || end - start >= bestSpan) continue;

            best     = node;
            bestSpan = end - start;
        }
        return best;
    }
}
