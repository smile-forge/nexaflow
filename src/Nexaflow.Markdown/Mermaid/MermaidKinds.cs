namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What a piece of a <c>mermaid</c> block is.
/// </summary>
/// <remarks>
/// Only what every diagram shares: the front matter, the directives and comments, the line that names the diagram, and
/// the accessibility lines every diagram accepts. What a diagram's own lines say — a slice of a pie, an edge of a
/// flowchart — is that diagram's grammar, and until one reads it a line is a <see cref="Statement"/>.
/// </remarks>
public static class MermaidKinds
{
    /// <summary>The whole body of the fence.</summary>
    public const string Block = "mermaid-block";

    /// <summary>One line and the characters that ended it.</summary>
    public const string Line = "mermaid-line";

    /// <summary>
    /// The <c>---</c> … <c>---</c> block that may open a diagram: its opening fence line, its lines, and its closing
    /// fence line.
    /// </summary>
    public const string FrontMatter = "mermaid-front-matter";

    /// <summary>A <c>---</c> that opens or closes the front matter.</summary>
    public const string Fence = "mermaid-fence";

    /// <summary>A <c>key: value</c> line of front matter: its key, its colon, and its value when one was written.</summary>
    public const string Field = "mermaid-field";

    public const string Key = "mermaid-key";

    /// <summary>Everything after a colon, less the space either side of it.</summary>
    public const string Value = "mermaid-value";

    /// <summary>
    /// A line of front matter that is not a key and its value — a list item, a folded continuation — held as written.
    /// What it means is for whatever reads the config.
    /// </summary>
    public const string Yaml = "mermaid-yaml";

    /// <summary>A <c>%%{ … }%%</c> directive, over as many lines as it takes.</summary>
    public const string Directive = "mermaid-directive";

    /// <summary>The line that names the diagram: its keyword, and whatever is written after it.</summary>
    public const string Header = "mermaid-header";

    /// <summary>The name of the diagram's type: <c>flowchart</c>, <c>sequenceDiagram</c>, <c>xychart-beta</c>.</summary>
    public const string Keyword = "mermaid-keyword";

    /// <summary>
    /// An <c>accTitle: …</c> or <c>accDescr: …</c> line, or an <c>accDescr { … }</c> block — the accessible title and
    /// description a diagram of any type may carry.
    /// </summary>
    public const string Accessibility = "mermaid-accessibility";

    /// <summary>A line of the diagram itself, as written. Which of its characters mean what is its diagram's grammar.</summary>
    public const string Statement = "mermaid-statement";
}

/// <summary>What a piece of a <c>mermaid</c> block is <em>to</em> the piece holding it.</summary>
public static class MermaidRoles
{
    /// <summary>A field's value, or an accessibility line's text. The key is <see cref="Ast.Roles.Name"/>.</summary>
    public const string Value = "value";

    /// <summary>What follows the keyword on the header line: <c>TD</c>, <c>title Pets</c>, <c>horizontal</c>.</summary>
    public const string Arguments = "arguments";
}
