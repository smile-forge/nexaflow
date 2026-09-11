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
        Register("c",          ".c", ".h");
        Register("java",       ".java");

        // Markup / templating languages — also the hosts for embedded-language injection (a <script> in
        // HTML, the Ruby in an ERB/Razor block, the HTML around <?php …?>). See LanguageInjections.
        Register("html",              ".html", ".htm");
        Register("css",               ".css");
        Register("embedded-template", ".erb");
        Register("razor",             ".razor", ".cshtml");
        Register("php",               ".php", ".phtml");
        Register("jinja",             ".j2", ".jinja", ".jinja2");   // html + python {{ }}/{% %}
        // XML family. XAML *is* XML, so both ids resolve to the one native xml grammar we build from
        // external/tree-sitter-xml (see CodeHighlighter.NativeAlias). They stay separate ids so the
        // structure extractor can read WPF meaning - x:Class/x:Name/x:Key, event handlers - out of .xaml
        // while a plain .xml gets a generic element outline.
        Register("xaml", ".xaml");
        // The build and the installer are XML too, and were the last unread part of how this app ships:
        // .wxs/.wxl author the MSI, .props/.targets carry logic every project inherits. (.wixproj already
        // reads through the csproj path — it is a project file with PropertyGroups like any other.)
        Register("xml",  ".xml", ".xsl", ".xslt", ".wxs", ".wxl", ".props", ".targets", ".manifest");
        // .ipynb is owned by the Notebook feature (its own viewer), not the code editor.
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

    /// <summary>
    /// The grammar that proves an edit to this file: <see cref="ForFile"/>, and XML for project and solution
    /// files, which are XML to an edit and to nothing else.
    /// <para>
    /// Not registered through <see cref="Register"/>, because <see cref="ForFile"/> also decides which files the
    /// graph walks as code, and the graph reads these through its structured layer instead — their project
    /// references and member projects. Registering them would walk them twice. An edit wants one thing from a
    /// grammar, proof that the file still parses, and that is all this lends them.
    /// </para>
    /// </summary>
    public static string? ForEdit(string fileName) =>
        ForFile(fileName) ?? (EditedAsXml.Contains(Path.GetExtension(fileName)) ? "xml" : null);

    /// <summary>Project and solution files: XML, read by the graph's structured layer rather than as code.</summary>
    private static readonly HashSet<string> EditedAsXml = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj", ".vcxproj", ".wixproj", ".slnx",
    };
}
