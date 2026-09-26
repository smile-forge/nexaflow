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

    /// <summary>What the front matter says about folding, hung on the diagram by <see cref="WithFolds"/> — written nowhere as one.</summary>
    public const string Folds = "mermaid-folds";

    /// <summary>What each run of a diagram's words is made of, hung on the diagram by <see cref="WithWordPieces"/> — written nowhere as one.</summary>
    public const string WordPieces = "mermaid-word-pieces";

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

    // ── What diagrams' own lines are made of ────────────────────────────────
    //
    // The shapes every grammar reads through MermaidLine, so escaping, holes, renames and the builders find them alike in
    // every diagram. What a line as a whole is — a slice, a set, an edge — is its diagram's own kind.

    /// <summary>A <c>title …</c>: its word, and what it says as <see cref="MermaidRoles.Title"/> — on a line of its own, or after a header's keyword.</summary>
    public const string Title = "mermaid-title";

    /// <summary>A name, bare or in quotes — quotes included, so a name with nothing between its quotes yet is somewhere a hole stands.</summary>
    public const string Name = "mermaid-name";

    /// <summary>Several names with a separator between each.</summary>
    public const string Names = "mermaid-names";

    /// <summary>A label in its brackets, with the quotes inside them where it has any.</summary>
    public const string Label = "mermaid-label";

    /// <summary>
    /// What names an icon, however the diagram writes one — <c>::icon(fa fa-book)</c>, a service's <c>(database)</c>, an
    /// <c>icon:</c> in a node's metadata — around what was written. Which icon it is, is the stage's (<see cref="WithIcons"/>).
    /// </summary>
    public const string Icon = "mermaid-icon";

    /// <summary>Text in quotes, quotes included.</summary>
    public const string Quoted = "mermaid-quoted";

    /// <summary>What a name, a label or a title says, without its quotes or brackets.</summary>
    public const string Words = "mermaid-words";

    /// <summary>
    /// Where a number is written: the <see cref="Number"/> — or, while none has been, the place after its separator where it
    /// goes, which is where a hole stands for it.
    /// </summary>
    public const string Amount = "mermaid-amount";

    /// <summary>A number, as it was written.</summary>
    public const string Number = "mermaid-number";

    /// <summary>A style's properties, with a comma between each.</summary>
    public const string Properties = "mermaid-properties";

    /// <summary>One <c>name:value</c> of a style: its name as a <see cref="Key"/>, and what it is set to.</summary>
    public const string Property = "mermaid-property";

    /// <summary>What a property is set to, as it was written.</summary>
    public const string Setting = "mermaid-setting";
}

/// <summary>What a piece of a <c>mermaid</c> block is <em>to</em> the piece holding it.</summary>
public static class MermaidRoles
{
    /// <summary>A field's value, or an accessibility line's text. The key is <see cref="Ast.Roles.Name"/>.</summary>
    public const string Value = "value";

    /// <summary>What follows the keyword on the header line: <c>TD</c>, <c>title Pets</c>, <c>horizontal</c>.</summary>
    public const string Arguments = "arguments";

    /// <summary>What a <see cref="MermaidKinds.Title"/> says: the title a builder sets over its diagram.</summary>
    public const string Title = "title";
}
