using System;
using System.Collections.Generic;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// A sub-range of a document that is written in a <b>different</b> language than the outer file — e.g. the
/// JavaScript inside an HTML <c>&lt;script&gt;</c>, the Ruby inside an ERB <c>&lt;% %&gt;</c>, or the SQL/HTML
/// inside a Ruby heredoc. Char offsets (<see cref="Start"/>/<see cref="End"/>/<see cref="Length"/>) index the
/// same UTF-16 space as the editor document; the row/column fields give the same boundaries as tree-sitter
/// <c>Point</c>s (needed to build a combined <c>IncludedRanges</c> parse).
///
/// <para><see cref="GroupKey"/> controls how the range is parsed. When null, the range is parsed on its own
/// (a self-contained block like a <c>&lt;script&gt;</c> or a heredoc). When set, all ranges that share a
/// (<see cref="TargetGrammarId"/>, <see cref="GroupKey"/>) are parsed <b>together</b> as one tree via
/// <c>Parser.IncludedRanges</c> — so non-contiguous fragments of one language (PHP's HTML around <c>&lt;?php?&gt;</c>,
/// an ERB template's <c>&lt;% %&gt;</c> blocks) form a single coherent document.</para>
/// </summary>
public readonly record struct InjectionRange(
    int Start, int End, int StartRow, int StartColumn, int EndRow, int EndColumn,
    string TargetGrammarId, string? GroupKey = null)
{
    public int Length => End - Start;
}

/// <summary>
/// Language injection — tree-sitter's model for files that embed another language. Given an outer grammar's
/// parse tree, <see cref="Find"/> returns the embedded sub-ranges and the grammar each should be parsed with.
/// The two halves are deliberately decoupled: <em>detecting</em> a site here is independent of whether the
/// target grammar is actually available (see <see cref="CodeHighlighter"/>), so a site for a language we don't
/// yet ship (sql, graphql, …) is detected today and lights up the moment a grammar is added.
///
/// Returns plain data (no live <see cref="Node"/>s) so callers can keep using it after the outer tree is
/// disposed. The recursion, budget, and substring re-parsing all live in <see cref="CodeHighlighter"/>.
/// </summary>
public static class LanguageInjections
{
    /// <summary>Fast gate so the highlighter skips the walk entirely for grammars that never inject.</summary>
    public static bool HasInjections(string outerGrammarId) => outerGrammarId switch
    {
        "html" or "ruby" or "embedded-template" or "php" or "jinja"
            or "javascript" or "typescript" => true,
        _ => false,
    };

    /// <summary>The embedded regions in <paramref name="root"/> for an <paramref name="outerGrammarId"/> file.
    /// <paramref name="text"/> is the full source (used by text-scan arms; node-walk arms ignore it).</summary>
    public static IReadOnlyList<InjectionRange> Find(string outerGrammarId, Node root, string text) => outerGrammarId switch
    {
        "html"                      => FindHtml(root),
        "ruby"                      => FindRubyHeredocs(root),
        "embedded-template"         => FindErb(root),
        "php"                       => FindPhp(root),
        "jinja"                     => FindJinja(root, text),
        "javascript" or "typescript" => FindTaggedTemplates(root),
        _                           => [],
    };

    // ── HTML: <script> → javascript, <style> → css (the element's raw_text child) ──────────────
    private static List<InjectionRange> FindHtml(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
        {
            var target = n.Type switch
            {
                "script_element" => "javascript",
                "style_element"  => "css",
                _                => null,
            };
            if (target is null) continue;
            foreach (var c in n.NamedChildren)
                if (c.Type == "raw_text") { Add(list, c, target); break; }
        }
        return list;
    }

    // ── ERB (embedded-template): code → ruby, content → html. Both are COMBINED so do/end and start/end
    //    tags that span separate directives pair up into one tree. ──────────────────────────────────────
    private static List<InjectionRange> FindErb(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
        {
            if (n.Type == "code") Add(list, n, "ruby", group: "ruby");
            else if (n.Type == "content") Add(list, n, "html", group: "html");
        }
        return list;
    }

    // ── PHP: the raw HTML lives in `text` nodes around the <?php …?> tags. COMBINED so the markup parses as
    //    one document (otherwise the trailing close-tag-only fragment is an orphan the html grammar rejects). ─
    private static List<InjectionRange> FindPhp(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
            if (n.Type == "text") Add(list, n, "html", group: "html");
        return list;
    }

    // ── Ruby heredocs: <<~SQL … SQL → sql, <<~HTML … → html, etc. ──────────────────────────────
    // The delimiter (heredoc_beginning) and the body (heredoc_body→heredoc_content) are *siblings*; they
    // appear in matching document order, so we zip the two lists by index.
    private static List<InjectionRange> FindRubyHeredocs(Node root)
    {
        var delims = new List<string>();
        var bodies = new List<Node>();
        foreach (var n in Descendants(root))
        {
            if (n.Type == "heredoc_beginning") delims.Add(n.Text);
            else if (n.Type == "heredoc_body") bodies.Add(n);
        }

        var list = new List<InjectionRange>();
        for (int i = 0; i < bodies.Count && i < delims.Count; i++)
        {
            var target = HeredocGrammar(delims[i]);
            if (target is null) continue;                 // unknown / no grammar (e.g. SQL) → no-op
            foreach (var c in bodies[i].NamedChildren)
                if (c.Type == "heredoc_content") { Add(list, c, target); break; }
        }
        return list;
    }

    /// <summary>Maps a heredoc delimiter (<c>&lt;&lt;~SQL</c>, <c>&lt;&lt;-HTML</c>, <c>&lt;&lt;"JS"</c>) to a
    /// grammar id, or null when the tag isn't a language we inject. Self (RUBY) is left to the highlighter's
    /// self-injection guard.</summary>
    private static string? HeredocGrammar(string beginning)
    {
        // Strip the leading <<, an optional ~ or - (squiggly / dash heredoc), and any surrounding quotes.
        var s = beginning.TrimStart('<');
        s = s.TrimStart('~', '-');
        s = s.Trim('"', '\'', '`').Trim();
        return s.ToUpperInvariant() switch
        {
            "HTML"               => "html",
            "CSS"                => "css",
            "JS" or "JAVASCRIPT" => "javascript",
            "JSON"               => "json",
            "SQL"                => "sql",          // no grammar yet → no-op, but detected
            "ERB"                => "embedded-template",  // nested ERB (ruby + html) inside a heredoc
            "RUBY" or "RB"       => "ruby",
            _                    => null,
        };
    }

    // ── Jinja: HTML host + a text scan for {{ expr }} / {% stmt %} → python ─────────────────────
    // No tree-sitter jinja grammar ships, so jinja files parse as html (for the markup) and we additionally
    // scan the raw text for the moustache/statement delimiters. The grammar id "jinja" aliases the html
    // native (see CodeHighlighter), so script/style injection keeps working too.
    private static List<InjectionRange> FindJinja(Node root, string text)
    {
        var list = FindHtml(root);
        ScanDelimited(text, "{{", "}}", "python", list);   // expressions
        ScanDelimited(text, "{%", "%}", "python", list);   // statements (for/if/…)
        return list;
    }

    private static void ScanDelimited(string text, string open, string close, string target, List<InjectionRange> list)
    {
        int i = 0;
        while (true)
        {
            int s = text.IndexOf(open, i, StringComparison.Ordinal);
            if (s < 0) break;
            int e = text.IndexOf(close, s + open.Length, StringComparison.Ordinal);
            if (e < 0) break;
            int inner = s + open.Length;
            if (e > inner) list.Add(IsolatedRange(inner, e, RowAt(text, inner), target));
            i = e + close.Length;
        }
    }

    private static int RowAt(string text, int index)
    {
        int row = 0, end = Math.Min(index, text.Length);
        for (int k = 0; k < end; k++) if (text[k] == '\n') row++;
        return row;
    }

    // ── JS/TS: gql`…` / graphql`…` tagged templates → graphql ──────────────────────────────────
    // Detection ships now; the graphql grammar doesn't, so this is a no-op (the highlighter finds no child)
    // until one is added — at which point GraphQL inside JS lights up with no further change here.
    private static List<InjectionRange> FindTaggedTemplates(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
        {
            if (n.Type != "call_expression") continue;
            if (n.GetChildForField("function") is not { } fn) continue;
            if (fn.Text is not ("gql" or "graphql")) continue;
            foreach (var c in n.NamedChildren)
                if (c.Type == "template_string")
                {
                    int start = c.StartIndex + 1, end = c.EndIndex - 1;   // strip the surrounding backticks
                    if (end > start)
                        list.Add(IsolatedRange(start, end, c.StartPosition.Row, "graphql"));
                    break;
                }
        }
        return list;
    }

    // (Jupyter .ipynb is handled by the dedicated Notebook feature, which decodes each cell's JSON source —
    // the in-place injection model can't, since cell newlines are JSON-escaped.)

    // ── shared ─────────────────────────────────────────────────────────────────────────────────
    private static void Add(List<InjectionRange> list, Node node, string targetGrammarId, string? group = null)
    {
        if (node.EndIndex <= node.StartIndex) return;
        var s = node.StartPosition;
        var e = node.EndPosition;
        list.Add(new InjectionRange(node.StartIndex, node.EndIndex, s.Row, s.Column, e.Row, e.Column, targetGrammarId, group));
    }

    /// <summary>An isolated range (parsed on its own) built from text scanning, where row/column boundaries
    /// aren't needed — only combined (<see cref="InjectionRange.GroupKey"/>) ranges use the column/end points.</summary>
    private static InjectionRange IsolatedRange(int start, int end, int startRow, string targetGrammarId)
        => new(start, end, startRow, 0, 0, 0, targetGrammarId, null);

    private static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.NamedChildren)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
