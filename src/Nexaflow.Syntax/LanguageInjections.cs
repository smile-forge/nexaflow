using System;
using System.Collections.Generic;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// A sub-range of a document that is written in a <b>different</b> language than the outer file — e.g. the
/// JavaScript inside an HTML <c>&lt;script&gt;</c>, the Ruby inside an ERB <c>&lt;% %&gt;</c>, or the SQL/HTML
/// inside a Ruby heredoc. Char offsets (<see cref="Start"/>/<see cref="Length"/>) index the same UTF-16 space
/// as the editor document; <see cref="StartRow"/> is the 0-based row of the region's start (so the outline can
/// map a child's line numbers back to absolute document lines).
/// </summary>
public readonly record struct InjectionRange(int Start, int Length, int StartRow, string TargetGrammarId);

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
            or "javascript" or "typescript" or "ipynb" => true,
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
        "ipynb"                     => FindIpynb(root),
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

    // ── ERB (embedded-template): code → ruby, content → html ───────────────────────────────────
    private static List<InjectionRange> FindErb(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
        {
            if (n.Type == "code") Add(list, n, "ruby");
            else if (n.Type == "content") Add(list, n, "html");
        }
        return list;
    }

    // ── PHP: the raw HTML lives in `text` nodes between/around the <?php …?> tags ───────────────
    private static List<InjectionRange> FindPhp(Node root)
    {
        var list = new List<InjectionRange>();
        foreach (var n in Descendants(root))
            if (n.Type == "text") Add(list, n, "html");
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
            int len = e - inner;
            if (len > 0) list.Add(new InjectionRange(inner, len, RowAt(text, inner), target));
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
                        list.Add(new InjectionRange(start, end - start, c.StartPosition.Row, "graphql"));
                    break;
                }
        }
        return list;
    }

    // ── Jupyter (.ipynb): JSON whose code cells hold source in the kernel language ──────────────
    // Outer grammar is json (aliased from "ipynb" in CodeHighlighter). Each code cell's `source` is a JSON
    // array of line-strings; we inject the kernel language over each string's *content* (between the quotes).
    // Per-line because the JSON `","` separators sit between array elements — a single combined range would
    // swallow them; this colours each line but doesn't see multi-line constructs (a best-effort limitation).
    private static List<InjectionRange> FindIpynb(Node root)
    {
        var list = new List<InjectionRange>();
        if (FirstOfType(root, "object") is not { } top) return list;

        var lang = NormalizeKernel(DetectKernel(top));
        if (lang is null) return list;

        if (ValueOfKey(top, "cells") is not { Type: "array" } cells) return list;
        foreach (var cell in cells.NamedChildren)
        {
            if (cell.Type != "object") continue;
            var kind = ValueOfKey(cell, "cell_type") is { } ct ? StringValue(ct) : "code";
            if (kind != "code") continue;                       // markdown/raw cells: no grammar, skip
            if (ValueOfKey(cell, "source") is not { } source) continue;

            if (source.Type == "array")
                foreach (var el in source.NamedChildren) AddStringContent(el, lang, list);
            else
                AddStringContent(source, lang, list);           // some writers store source as one string
        }
        return list;
    }

    private static void AddStringContent(Node maybeString, string target, List<InjectionRange> list)
    {
        if (maybeString.Type != "string") return;
        foreach (var c in maybeString.NamedChildren)
            if (c.Type == "string_content") { Add(list, c, target); return; }
    }

    /// <summary>The kernel language from <c>metadata.kernelspec.language</c> or
    /// <c>metadata.language_info.name</c>, or null when absent.</summary>
    private static string? DetectKernel(Node top)
    {
        if (ValueOfKey(top, "metadata") is not { Type: "object" } meta) return "python";  // default for a bare notebook
        if (ValueOfKey(meta, "kernelspec") is { Type: "object" } ks && ValueOfKey(ks, "language") is { } l)
            return StringValue(l);
        if (ValueOfKey(meta, "language_info") is { Type: "object" } li && ValueOfKey(li, "name") is { } n)
            return StringValue(n);
        return "python";
    }

    /// <summary>Maps a kernel language name to a grammar id we can load (python3/ipython → python), or null.</summary>
    private static string? NormalizeKernel(string? kernel)
    {
        if (string.IsNullOrWhiteSpace(kernel)) return null;
        var k = kernel.ToLowerInvariant();
        if (k.Contains("python") || k == "ipython") return "python";
        return k switch
        {
            "ruby"                  => "ruby",
            "javascript" or "node"  => "javascript",
            "typescript"            => "typescript",
            "csharp" or "c#"        => "c-sharp",
            _                       => null,   // unknown kernel ⇒ no injection
        };
    }

    /// <summary>The value node of the first <c>pair</c> in <paramref name="obj"/> whose key string equals
    /// <paramref name="key"/> (field-name-independent: key = first string child, value = the pair's value).</summary>
    private static Node? ValueOfKey(Node obj, string key)
    {
        foreach (var pair in obj.NamedChildren)
        {
            if (pair.Type != "pair") continue;
            if (pair.GetChildForField("key") is not { } k || StringValue(k) != key) continue;
            return pair.GetChildForField("value");
        }
        return null;
    }

    private static Node? FirstOfType(Node n, string type)
    {
        foreach (var c in n.NamedChildren)
            if (c.Type == type) return c;
        return null;
    }

    /// <summary>The decoded-ish text of a JSON string node (its <c>string_content</c>, else quote-stripped).</summary>
    private static string StringValue(Node n)
    {
        if (n.Type == "string")
            foreach (var c in n.NamedChildren)
                if (c.Type == "string_content") return c.Text;
        var t = n.Text;
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }

    // ── shared ─────────────────────────────────────────────────────────────────────────────────
    private static void Add(List<InjectionRange> list, Node node, string targetGrammarId)
    {
        int length = node.EndIndex - node.StartIndex;
        if (length > 0)
            list.Add(new InjectionRange(node.StartIndex, length, node.StartPosition.Row, targetGrammarId));
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.NamedChildren)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
