namespace Nexaflow.Syntax;

/// <summary>
/// Maps a file extension to the tree-sitter grammar that parses it.
/// </summary>
/// <remarks>
/// This lives with the syntax engine rather than the editor so headless callers — the Initiatives snaplink
/// validator and its CLI — can resolve a grammar (and therefore a class/method outline) without dragging in
/// WPF/AvalonEdit. The editor's <c>HighlightingRegistry</c> delegates its tree-sitter branch here and keeps
/// only its own <c>.xshd</c> fallback.
/// </remarks>
public static class TreeSitterLanguages
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase);

    static TreeSitterLanguages()
    {
        // Real code languages → tree-sitter (yields a parse tree for AI/graphify too).
        Register("c-sharp",    ".cs", ".csx");
        Register("javascript", ".js", ".mjs", ".cjs", ".jsx");
        Register("typescript", ".ts", ".cts", ".mts");
        Register("python",     ".py", ".pyw");
        Register("ruby",       ".rb", ".rbw", ".rake", ".gemspec", ".ru");
        Register("json",       ".json");
        Register("rust",       ".rs");
        Register("cpp",        ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".ipp");
        Register("java",       ".java");

        // Markup / templating languages — also the hosts for embedded-language injection (a <script> in
        // HTML, the Ruby in an ERB/Razor block, the HTML around <?php …?>). See LanguageInjections.
        Register("html",              ".html", ".htm");
        Register("css",               ".css");
        Register("embedded-template", ".erb");
        Register("razor",             ".razor", ".cshtml");
        Register("php",               ".php", ".phtml");
        Register("jinja",             ".j2", ".jinja", ".jinja2");   // html + python {{ }}/{% %}
        // .ipynb is owned by the Notebook feature (its own viewer), not the code editor.
        // xml/xaml/xsl stay on AvalonEdit's built-in .xshd (no bundled tree-sitter xml grammar).
    }

    /// <summary>Registers a tree-sitter grammar for a set of extensions.</summary>
    public static void Register(string grammarId, params string[] extensions)
    {
        foreach (var ext in extensions) ByExtension[ext] = grammarId;
    }

    /// <summary>The grammar id for this file, or <c>null</c> when no tree-sitter grammar covers its extension.</summary>
    public static string? ForFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? null : ByExtension.GetValueOrDefault(ext);
    }

    /// <summary>True when a tree-sitter grammar can parse this file (i.e. it has a class/method outline).</summary>
    public static bool IsCode(string fileName) => ForFile(fileName) is not null;
}
