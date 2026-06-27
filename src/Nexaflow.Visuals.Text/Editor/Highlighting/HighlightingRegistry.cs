using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>How the editor should colour a document.</summary>
public enum HighlightMode { PlainText, Xshd, TreeSitter }

/// <summary>The resolved highlighting strategy for a file. Plain text ⇒ no colouring (and spell-check is
/// eligible); <see cref="HighlightMode.Xshd"/> carries an AvalonEdit definition; <see cref="HighlightMode.TreeSitter"/>
/// carries a grammar id the tree-sitter colourizer understands.</summary>
public sealed record HighlightResolution(
    HighlightMode Mode,
    IHighlightingDefinition? Definition = null,
    string? TreeSitterLanguage = null)
{
    public static readonly HighlightResolution Plain = new(HighlightMode.PlainText);
}

/// <summary>
/// Picks a highlighting strategy by file extension. Real code languages route to tree-sitter (which also
/// yields a parse tree for AI/graphify); simpler/markup formats use AvalonEdit's built-in .xshd; anything
/// unrecognised stays plain text (so the Windows spell-checker can attach instead).
/// </summary>
public static class HighlightingRegistry
{
    // Extensions whose colouring (and parse tree) come from tree-sitter rather than .xshd.
    private static readonly Dictionary<string, string> TreeSitterByExtension =
        new(StringComparer.OrdinalIgnoreCase);

    static HighlightingRegistry()
    {
        // Real code languages → tree-sitter (yields a parse tree for AI/graphify too).
        RegisterTreeSitter("c-sharp",    ".cs", ".csx");
        RegisterTreeSitter("javascript", ".js", ".mjs", ".cjs", ".jsx");
        RegisterTreeSitter("typescript", ".ts", ".cts", ".mts");
        RegisterTreeSitter("python",     ".py", ".pyw");
        RegisterTreeSitter("ruby",       ".rb", ".rbw", ".rake", ".gemspec", ".ru");
        RegisterTreeSitter("json",       ".json");
        RegisterTreeSitter("rust",       ".rs");
        RegisterTreeSitter("cpp",        ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".ipp");
        RegisterTreeSitter("java",       ".java");

        // Markup / templating languages — also the hosts for embedded-language injection (a <script> in
        // HTML, the Ruby in an ERB/Razor block, the HTML around <?php …?>). See LanguageInjections.
        RegisterTreeSitter("html",              ".html", ".htm");
        RegisterTreeSitter("css",               ".css");
        RegisterTreeSitter("embedded-template", ".erb");
        RegisterTreeSitter("razor",             ".razor", ".cshtml");
        RegisterTreeSitter("php",               ".php", ".phtml");
        RegisterTreeSitter("jinja",             ".j2", ".jinja", ".jinja2");   // html + python {{ }}/{% %}
        // .ipynb is owned by the Notebook feature (its own viewer), not the code editor.
        // xml/xaml/xsl stay on AvalonEdit's built-in .xshd (no bundled tree-sitter xml grammar).
    }

    /// <summary>Registers a tree-sitter grammar for a set of extensions (called during tree-sitter setup).</summary>
    public static void RegisterTreeSitter(string grammarId, params string[] extensions)
    {
        foreach (var ext in extensions)
            TreeSitterByExtension[ext] = grammarId;
    }

    public static HighlightResolution Resolve(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return HighlightResolution.Plain;

        if (TreeSitterByExtension.TryGetValue(ext, out var grammar))
            return new HighlightResolution(HighlightMode.TreeSitter, TreeSitterLanguage: grammar);

        var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        return def is not null
            ? new HighlightResolution(HighlightMode.Xshd, def)
            : HighlightResolution.Plain;
    }

    /// <summary>True when the file has a known syntax (structured mode); false ⇒ plain text (spell-check mode).</summary>
    public static bool IsStructured(string fileName) => Resolve(fileName).Mode != HighlightMode.PlainText;
}
