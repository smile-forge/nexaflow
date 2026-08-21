using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// Wraps a tree-sitter grammar + highlight query for one language. <see cref="Highlight"/> parses source
/// and returns coloured spans (char offsets). Not thread-safe — use from one thread (the editor's UI thread).
///
/// <para><b>Embedded languages.</b> A file can embed another language (HTML's <c>&lt;script&gt;</c>, an ERB
/// <c>&lt;% %&gt;</c>, a Ruby heredoc, …). <see cref="LanguageInjections"/> finds those sub-ranges; this class
/// re-parses each substring with a cached child highlighter and merges the results (offset into parent
/// coordinates), recursing so nesting works. Embedded spans are appended <em>after</em> the outer spans so the
/// colourizer's last-wins overlap rule gives them priority. A target grammar we don't ship simply yields no
/// child — a graceful no-op.</para>
/// </summary>
public sealed class CodeHighlighter : IDisposable
{
    private readonly string _grammarId;

    /// <summary>The grammar this highlighter reads, for callers whose behaviour is language-specific (an
    /// 8-digit hex colour means <c>#AARRGGBB</c> in XAML and <c>#RRGGBBAA</c> in CSS).</summary>
    public string GrammarId => _grammarId;
    private readonly Language _language;
    private readonly Parser _parser;
    private readonly Query _query;

    // Child highlighters for embedded languages, keyed by grammar id. Caches the *null miss* too, so an
    // unavailable grammar (sql, graphql) isn't retried on every reparse. One UI thread ⇒ no locking.
    private readonly Dictionary<string, CodeHighlighter?> _children = new();

    // Bounds on injection work per top-level call (a fresh budget each call — never instance state).
    private const int MaxDepth = 4;          // ipynb/jinja → html → <script> → …
    private const int MaxInjections = 128;
    private sealed class InjectionBudget { public int Depth; public int Remaining = MaxInjections; }

    // Empty included-ranges ⇒ parse the whole document (resets any previous combined-parse state on reuse).
    private static readonly TreeSitter.Range[] NoRanges = System.Array.Empty<TreeSitter.Range>();

    // Grammar ids that parse with a *different* native grammar but route injection by their own id (jinja
    // parses as html for the markup, then additionally injects python into {{ }}/{% %} — see LanguageInjections).
    private static readonly Dictionary<string, string> NativeAlias = new()
    {
        ["jinja"] = "html",   // markup parses as html; python injected into {{ }}/{% %}
        ["xaml"]  = "xml",    // XAML is XML; the id stays distinct so the outline can read WPF meaning
    };

    private CodeHighlighter(string grammarId, Language language, Parser parser, Query query)
    {
        _grammarId = grammarId;
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
            language = new Language(NativeAlias.GetValueOrDefault(grammarId, grammarId));
            parser = new Parser(language);
            query = new Query(language, queryText);
            return new CodeHighlighter(grammarId, language, parser, query);   // keep the alias id for injection routing
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
        HighlightInto(spans, text, 0, null, new InjectionBudget());
        return spans;
    }

    // <paramref name="included"/> null ⇒ parse the whole <paramref name="text"/> (offset shifts isolated child
    // spans). Non-null ⇒ a combined parse over those ranges, whose node offsets are already in <paramref
    // name="text"/> coordinates (so offset is the parent's, not per-fragment).
    private void HighlightInto(List<HighlightSpan> spans, string text, int offset,
        TreeSitter.Range[]? included, InjectionBudget budget)
    {
        if (string.IsNullOrEmpty(text)) return;
        _parser.IncludedRanges = included ?? NoRanges;
        using var tree = _parser.Parse(text);
        if (tree is null) return;

        foreach (var capture in _query.Execute(tree.RootNode).Captures)
        {
            var node = capture.Node;
            var length = node.EndIndex - node.StartIndex;
            if (length > 0)
                spans.Add(new HighlightSpan(node.StartIndex + offset, length, capture.Name));
        }

        // XAML asks for one thing the grammar cannot give: an attribute value is a single token to XML, but
        // {StaticResource Foo} has structure a reader relies on. Appended here so the finer spans win.
        if (_grammarId == "xaml") XamlHighlighting.AddValueSpans(tree.RootNode, text, offset, spans);

        // Embedded spans appended after the outer spans ⇒ last-wins gives them priority.
        ForEachInjection(text, tree.RootNode, offset, budget,
            (child, t, o, inc) => child.HighlightInto(spans, t, o, inc, budget));
    }

    /// <summary>Collapsible ranges for editor folding: every multi-line, block-like node in the parse tree
    /// (function/class bodies, blocks, object/array literals) plus comment blocks — a single multi-line block
    /// comment, or a run of consecutive own-line line/doc comments collapsed into one fold. Char offsets.
    /// Folds inside embedded languages are included too (offset into parent coordinates).</summary>
    public IReadOnlyList<FoldRange> GetFolds(string text)
    {
        var folds = new List<FoldRange>();
        GetFoldsInto(folds, text, 0, null, new InjectionBudget());
        return folds;
    }

    private void GetFoldsInto(List<FoldRange> folds, string text, int offset,
        TreeSitter.Range[]? included, InjectionBudget budget)
    {
        if (string.IsNullOrEmpty(text)) return;
        _parser.IncludedRanges = included ?? NoRanges;
        using var tree = _parser.Parse(text);
        if (tree is null) return;

        var local = new List<FoldRange>();
        var comments = new List<CommentSpan>();
        CollectFolds(tree.RootNode, local, comments);
        AddCommentFolds(comments, text, local);
        foreach (var f in local) folds.Add(new FoldRange(f.Start + offset, f.End + offset));

        ForEachInjection(text, tree.RootNode, offset, budget,
            (child, t, o, inc) => child.GetFoldsInto(folds, t, o, inc, budget));
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
        _parser.IncludedRanges = NoRanges;   // always whole-doc (clear any combined-parse state)
        using var tree = _parser.Parse(text);
        return tree is null ? default : visit(tree.RootNode);
    }

    /// <summary>The embedded sub-ranges in <paramref name="text"/> (HTML's script/style, ERB code, Ruby
    /// heredocs, …), as plain data safe to use after the parse tree is disposed. Used by the structure
    /// extractor to render embedded code as linked sub-graphs. Empty when this grammar never injects.</summary>
    public IReadOnlyList<InjectionRange> FindInjections(string text)
    {
        if (string.IsNullOrEmpty(text) || !LanguageInjections.HasInjections(_grammarId)) return [];
        return WithParseTree(text, root => LanguageInjections.Find(_grammarId, root, text)) ?? [];
    }

    /// <summary>Returns the parse tree as an s-expression (for AI/graphify structural understanding), or null
    /// if the text is empty / fails to parse. Embedded languages are spliced in as labelled child blocks.
    /// Capped to keep large files from flooding a tool result.</summary>
    public string? GetParseTree(string text, int maxChars = 20_000)
    {
        var expr = BuildParseTree(text, null, new InjectionBudget());
        if (expr is null) return null;
        return expr.Length > maxChars ? expr[..maxChars] + " …(truncated)" : expr;
    }

    private string? BuildParseTree(string text, TreeSitter.Range[]? included, InjectionBudget budget)
    {
        if (string.IsNullOrEmpty(text)) return null;
        _parser.IncludedRanges = included ?? NoRanges;
        using var tree = _parser.Parse(text);
        var expr = tree?.RootNode.Expression;
        if (expr is null) return null;

        var sb = new StringBuilder(expr);
        ForEachInjection(text, tree!.RootNode, 0, budget, (child, t, _, inc) =>
        {
            var childExpr = child.BuildParseTree(t, inc, budget);
            if (childExpr is not null)
                sb.Append("\n(injection language: \"").Append(child._grammarId).Append("\"\n  ")
                  .Append(childExpr).Append(')');
        });
        return sb.ToString();
    }

    /// <summary>Discovers the embedded ranges in <paramref name="root"/> (must be live) and invokes
    /// <paramref name="visit"/> with each child highlighter and how to parse it: an <b>isolated</b> range is
    /// passed its substring (and the absolute offset), while a <b>combined</b> group is passed the parent text
    /// plus <c>IncludedRanges</c> so its non-contiguous fragments parse as one tree in original coordinates.
    /// Honours the depth/count budget and skips self-injection and unavailable grammars.</summary>
    private void ForEachInjection(string text, Node root, int offset, InjectionBudget budget,
        Action<CodeHighlighter, string, int, TreeSitter.Range[]?> visit)
    {
        if (budget.Depth >= MaxDepth) return;
        if (!LanguageInjections.HasInjections(_grammarId)) return;

        IReadOnlyList<InjectionRange> injections;
        try { injections = LanguageInjections.Find(_grammarId, root, text); }
        catch { return; }
        if (injections.Count == 0) return;

        budget.Depth++;

        foreach (var inj in injections)   // isolated ranges → parse each substring on its own
        {
            if (inj.GroupKey is not null) continue;
            if (budget.Remaining <= 0) break;
            if (inj.TargetGrammarId == _grammarId) continue;                          // self-injection guard
            if (inj.Start < 0 || inj.Length <= 0 || inj.End > text.Length) continue;
            var sub = text.Substring(inj.Start, inj.Length);
            if (string.IsNullOrWhiteSpace(sub)) continue;
            var child = GetChild(inj.TargetGrammarId);
            if (child is null) continue;                                              // grammar unavailable → no-op
            budget.Remaining--;
            try { visit(child, sub, offset + inj.Start, null); }
            catch { /* a bad embedded fragment must never break the host's highlighting */ }
        }

        foreach (var (grammar, ranges) in BuildGroups(injections, text.Length))   // combined groups
        {
            if (budget.Remaining <= 0) break;
            if (grammar == _grammarId) continue;
            var child = GetChild(grammar);
            if (child is null) continue;
            budget.Remaining--;
            try { visit(child, text, offset, ranges); }
            catch { /* combined parse failure must never break the host */ }
        }

        budget.Depth--;
    }

    /// <summary>Builds the <c>IncludedRanges</c> arrays for combined injections, one per
    /// (<see cref="InjectionRange.TargetGrammarId"/>, <see cref="InjectionRange.GroupKey"/>), in first-seen
    /// order, each sorted by position and bounds-checked.</summary>
    private static IEnumerable<(string Grammar, TreeSitter.Range[] Ranges)> BuildGroups(
        IReadOnlyList<InjectionRange> injections, int textLength)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, (string Grammar, List<InjectionRange> Ranges)>();
        foreach (var inj in injections)
        {
            if (inj.GroupKey is null) continue;
            if (inj.Start < 0 || inj.End <= inj.Start || inj.End > textLength) continue;
            var key = inj.TargetGrammarId + "\0" + inj.GroupKey;
            if (!byKey.TryGetValue(key, out var g)) { g = (inj.TargetGrammarId, new List<InjectionRange>()); byKey[key] = g; order.Add(key); }
            g.Ranges.Add(inj);
        }

        foreach (var key in order)
        {
            var (grammar, ranges) = byKey[key];
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            var arr = new TreeSitter.Range[ranges.Count];
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                arr[i] = new TreeSitter.Range(new Point(r.StartRow, r.StartColumn), new Point(r.EndRow, r.EndColumn), r.Start, r.End);
            }
            yield return (grammar, arr);
        }
    }

    private CodeHighlighter? GetChild(string grammarId)
    {
        if (_children.TryGetValue(grammarId, out var cached)) return cached;
        var child = TryCreate(grammarId);
        _children[grammarId] = child;   // cache null too
        return child;
    }

    public void Dispose()
    {
        foreach (var c in _children.Values) c?.Dispose();
        _children.Clear();
        _query.Dispose();
        _parser.Dispose();
        _language.Dispose();
    }
}
