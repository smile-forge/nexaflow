using System;
using System.Collections.Generic;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// Wraps a tree-sitter grammar + highlight query for one language. <see cref="Highlight"/> parses source
/// and returns coloured spans (char offsets). Not thread-safe — use from one thread (the editor's UI thread).
/// </summary>
public sealed class CodeHighlighter : IDisposable
{
    private readonly Language _language;
    private readonly Parser _parser;
    private readonly Query _query;

    private CodeHighlighter(Language language, Parser parser, Query query)
    {
        _language = language;
        _parser = parser;
        _query = query;
    }

    /// <summary>Creates a highlighter for <paramref name="grammarId"/> (e.g. "c-sharp"), or null if the
    /// grammar/native is unavailable or the query fails to compile. Never throws.</summary>
    public static CodeHighlighter? TryCreate(string grammarId)
    {
        if (!HighlightQueries.ByGrammar.TryGetValue(grammarId, out var queryText))
            return null;

        Language? language = null;
        Parser? parser = null;
        Query? query = null;
        try
        {
            language = new Language(grammarId);
            parser = new Parser(language);
            query = new Query(language, queryText);
            return new CodeHighlighter(language, parser, query);
        }
        catch
        {
            query?.Dispose();
            parser?.Dispose();
            language?.Dispose();
            return null;
        }
    }

    public IReadOnlyList<HighlightSpan> Highlight(string text)
    {
        var spans = new List<HighlightSpan>();
        if (string.IsNullOrEmpty(text)) return spans;

        using var tree = _parser.Parse(text);
        if (tree is null) return spans;

        foreach (var capture in _query.Execute(tree.RootNode).Captures)
        {
            var node = capture.Node;
            var length = node.EndIndex - node.StartIndex;
            if (length > 0)
                spans.Add(new HighlightSpan(node.StartIndex, length, capture.Name));
        }
        return spans;
    }

    /// <summary>Collapsible ranges for editor folding: every multi-line, block-like node in the parse tree
    /// (function/class bodies, blocks, object/array literals, multi-line comments). Char offsets.</summary>
    public IReadOnlyList<FoldRange> GetFolds(string text)
    {
        var folds = new List<FoldRange>();
        if (string.IsNullOrEmpty(text)) return folds;
        using var tree = _parser.Parse(text);
        if (tree is not null) CollectFolds(tree.RootNode, folds);
        return folds;
    }

    private static void CollectFolds(Node node, List<FoldRange> folds)
    {
        foreach (var child in node.NamedChildren)
        {
            if (child.EndPosition.Row > child.StartPosition.Row
                && child.EndIndex > child.StartIndex
                && IsFoldable(child.Type))
                folds.Add(new FoldRange(child.StartIndex, child.EndIndex));
            CollectFolds(child, folds);
        }
    }

    // Block-like node types across our grammars: `{…}`/indented bodies, collections, multi-line comments.
    private static bool IsFoldable(string type) =>
        type.Contains("block")
        || type.Contains("body")
        || type.EndsWith("_list")
        || type is "object" or "array" or "hash" or "dictionary" or "set"
        || type.Contains("comment");

    /// <summary>Returns the parse tree as an s-expression (for AI/graphify structural understanding), or null
    /// if the text is empty / fails to parse. Capped to keep large files from flooding a tool result.</summary>
    public string? GetParseTree(string text, int maxChars = 20_000)
    {
        if (string.IsNullOrEmpty(text)) return null;
        using var tree = _parser.Parse(text);
        var expr = tree?.RootNode.Expression;
        if (expr is null) return null;
        return expr.Length > maxChars ? expr[..maxChars] + " …(truncated)" : expr;
    }

    public void Dispose()
    {
        _query.Dispose();
        _parser.Dispose();
        _language.Dispose();
    }
}
