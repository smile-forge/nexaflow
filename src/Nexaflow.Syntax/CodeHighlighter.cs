using System;
using System.Collections.Generic;
using System.Linq;
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
    /// (function/class bodies, blocks, object/array literals) plus comment blocks — a single multi-line block
    /// comment, or a run of consecutive own-line line/doc comments collapsed into one fold. Char offsets.</summary>
    public IReadOnlyList<FoldRange> GetFolds(string text)
    {
        var folds = new List<FoldRange>();
        if (string.IsNullOrEmpty(text)) return folds;
        using var tree = _parser.Parse(text);
        if (tree is null) return folds;

        var comments = new List<CommentSpan>();
        CollectFolds(tree.RootNode, folds, comments);
        AddCommentFolds(comments, text, folds);
        return folds;
    }

    private readonly record struct CommentSpan(int Start, int End, int StartRow, int EndRow);

    private static void CollectFolds(Node node, List<FoldRange> folds, List<CommentSpan> comments)
    {
        foreach (var child in node.NamedChildren)
        {
            if (child.Type.Contains("comment"))
            {
                if (child.EndIndex > child.StartIndex)
                    comments.Add(new CommentSpan(child.StartIndex, child.EndIndex,
                        child.StartPosition.Row, child.EndPosition.Row));
            }
            else if (child.EndPosition.Row > child.StartPosition.Row
                && child.EndIndex > child.StartIndex
                && IsFoldable(child.Type))
            {
                folds.Add(new FoldRange(child.StartIndex, child.EndIndex));
            }
            CollectFolds(child, folds, comments);
        }
    }

    /// <summary>Folds comment blocks: a multi-line block comment, or a run of consecutive comments that each
    /// start their own line (so trailing end-of-line comments are not folded). Adjacent comments on
    /// consecutive lines merge into a single fold; a lone single-line comment is left alone.</summary>
    private static void AddCommentFolds(List<CommentSpan> comments, string text, List<FoldRange> folds)
    {
        if (comments.Count == 0) return;

        var ownLine = comments
            .Where(c => IsAtLineStart(text, c.Start))
            .OrderBy(c => c.Start)
            .ToList();

        int i = 0;
        while (i < ownLine.Count)
        {
            int runStart = ownLine[i].Start, runEnd = ownLine[i].End;
            int runStartRow = ownLine[i].StartRow, runEndRow = ownLine[i].EndRow;
            int j = i + 1;
            while (j < ownLine.Count && ownLine[j].StartRow <= runEndRow + 1)   // contiguous (no blank gap)
            {
                runEnd    = Math.Max(runEnd, ownLine[j].End);
                runEndRow = Math.Max(runEndRow, ownLine[j].EndRow);
                j++;
            }
            if (runEndRow > runStartRow) folds.Add(new FoldRange(runStart, runEnd));   // spans >1 line ⇒ foldable
            i = j;
        }
    }

    /// <summary>True if only whitespace precedes <paramref name="offset"/> on its line (the comment owns the line).</summary>
    private static bool IsAtLineStart(string text, int offset)
    {
        int i = Math.Min(offset, text.Length) - 1;
        while (i >= 0 && (text[i] == ' ' || text[i] == '\t')) i--;
        return i < 0 || text[i] == '\n';
    }

    // Block-like node types across our grammars: `{…}`/indented bodies, collections. (Comments are folded
    // separately, by run, in AddCommentFolds.)
    private static bool IsFoldable(string type) =>
        type.Contains("block")
        || type.Contains("body")
        || type.EndsWith("_list")
        || type is "object" or "array" or "hash" or "dictionary" or "set";

    /// <summary>Parses <paramref name="text"/> and invokes <paramref name="visit"/> with the root node while
    /// the tree is alive, returning whatever it produces (or <c>default</c> if the text is empty / fails to
    /// parse). The visitor MUST extract plain data — tree-sitter <c>Node</c>s are only valid until the tree is
    /// disposed, which happens as this method returns. Same one-thread constraint as <see cref="Highlight"/>.</summary>
    public T? WithParseTree<T>(string text, Func<Node, T> visit)
    {
        if (string.IsNullOrEmpty(text)) return default;
        using var tree = _parser.Parse(text);
        return tree is null ? default : visit(tree.RootNode);
    }

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
