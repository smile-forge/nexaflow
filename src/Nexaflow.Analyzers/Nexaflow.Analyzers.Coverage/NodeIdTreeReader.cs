using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Nexaflow.Analyzers.Coverage;

/// <summary>
/// Extracts the valid product-node id set from <c>.product/tree.json</c> (passed as an AdditionalFile) so
/// the analyzer can flag a stale <c>[CoversNode]</c> id. A netstandard2.0 analyzer can't casually depend on
/// System.Text.Json (shipping a dependency alongside an analyzer is its own headache), so this is a small,
/// dependency-free scanner that collects the keys of the top-level <c>"nodes"</c> object. Returns null when
/// the tree isn't available — a clean checkout has no gitignored <c>.product/</c> — so id-validity is simply
/// not checked there.
/// </summary>
internal sealed class TreeInfo
{
    private readonly ImmutableHashSet<string> _ids;
    private readonly ImmutableHashSet<string> _parents;   // ids that some node names as its 'parent' ⇒ have children

    public TreeInfo(ImmutableHashSet<string> ids, ImmutableHashSet<string> parents)
    {
        _ids = ids;
        _parents = parents;
    }

    public bool Contains(string id) => _ids.Contains(id);

    /// <summary>A known node with no children — a specific behaviour, not a container.</summary>
    public bool IsLeaf(string id) => _ids.Contains(id) && !_parents.Contains(id);
}

internal static class NodeIdTreeReader
{
    public static TreeInfo? TryLoad(ImmutableArray<AdditionalText> additionalFiles, CancellationToken ct)
    {
        AdditionalText? tree = null;
        foreach (var f in additionalFiles)
            if (Normalize(f.Path).EndsWith("/.product/tree.json"))
            {
                tree = f;
                break;
            }
        if (tree is null) return null;

        var text = tree.GetText(ct)?.ToString();
        if (string.IsNullOrEmpty(text)) return null;
        return new TreeInfo(ExtractNodeIds(text!), ExtractParents(text!));
    }

    /// <summary>Every distinct <c>"parent": "id"</c> value — the ids that are a container (have children).</summary>
    private static ImmutableHashSet<string> ExtractParents(string s)
    {
        var parents = ImmutableHashSet.CreateBuilder<string>();
        const string key = "\"parent\"";
        var i = 0;
        while ((i = s.IndexOf(key, i, System.StringComparison.Ordinal)) >= 0)
        {
            var j = i + key.Length;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j < s.Length && s[j] == ':')
            {
                j++;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                if (j < s.Length && s[j] == '"')
                {
                    var start = j + 1;
                    j = start;
                    while (j < s.Length && s[j] != '"') { if (s[j] == '\\') j++; j++; }
                    if (start <= s.Length && j <= s.Length && j >= start)
                        parents.Add(s.Substring(start, System.Math.Min(j, s.Length) - start));
                }
            }
            i += key.Length;
        }
        return parents.ToImmutable();
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Collects the keys of the top-level <c>"nodes"</c> object. Tracks string context so braces inside
    /// string values don't skew the depth count; node ids are ASCII kebab-case keys so no unescaping is
    /// needed to read a key correctly (only correct string termination matters).
    /// </summary>
    private static ImmutableHashSet<string> ExtractNodeIds(string s)
    {
        var ids = ImmutableHashSet.CreateBuilder<string>();

        var anchor = s.IndexOf("\"nodes\"", System.StringComparison.Ordinal);
        if (anchor < 0) return ids.ToImmutable();

        var i = anchor + "\"nodes\"".Length;
        // Advance to the '{' that opens the nodes object (only ':' + whitespace should intervene).
        while (i < s.Length && s[i] != '{')
        {
            if (s[i] == '"') return ids.ToImmutable();   // unexpected — bail rather than misread
            i++;
        }
        if (i >= s.Length) return ids.ToImmutable();
        i++;                 // now inside the nodes object
        var depth = 1;

        while (i < s.Length && depth > 0)
        {
            var c = s[i];
            if (c == '"')
            {
                var start = i + 1;
                i = start;
                while (i < s.Length)
                {
                    if (s[i] == '\\') { i += 2; continue; }   // skip escape (keeps \" from ending the string)
                    if (s[i] == '"') break;
                    i++;
                }
                var str = i <= s.Length ? s.Substring(start, System.Math.Min(i, s.Length) - start) : string.Empty;
                i++;   // past closing quote

                if (depth == 1)
                {
                    var j = i;
                    while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                    if (j < s.Length && s[j] == ':') ids.Add(str);   // a key at nodes-object level = a node id
                }
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}') depth--;
            i++;
        }

        return ids.ToImmutable();
    }
}
