namespace Nexaflow.Markdown.Prose;

/// <summary>
/// What a piece of a markdown document is — the constructs a writer spells with punctuation and indentation
/// rather than with a language of its own.
///
/// <para>
/// Every one of them is the same shape: the marks that make it what it is, kept as <see cref="Ast.Roles.Name"/>,
/// <see cref="Ast.Roles.Open"/>, <see cref="Ast.Roles.Close"/> or <see cref="Ast.Roles.Trivia"/>, beside what
/// they were put round, kept as <see cref="Ast.Roles.Body"/>. So the tree prints back as exactly what was typed,
/// a reader asks what a stretch <em>is</em> rather than counting asterisks, and the caret can be put among the
/// marks somebody wrote.
/// </para>
/// </summary>
public static class MarkdownKinds
{
    /// <summary>A whole document.</summary>
    public const string Document = "document";

    // ── Blocks ──────────────────────────────────────────────────────────────

    /// <summary>A run of text with a blank line either side of it.</summary>
    public const string Paragraph = "paragraph";

    /// <summary><c>## Like this</c>, or the underlined form.</summary>
    public const string Heading = "heading";

    /// <summary><c>---</c> on a line of its own.</summary>
    public const string Rule = "rule";

    /// <summary><c>&gt; quoted</c>.</summary>
    public const string Quote = "quote";

    /// <summary><c>&gt; [!NOTE]</c> and its kin: a quote that says what it is for.</summary>
    public const string Alert = "alert";

    /// <summary>Bulleted or numbered.</summary>
    public const string List = "list";

    /// <summary>One thing in a list.</summary>
    public const string Item = "item";

    /// <summary><c>- [x]</c> — an item somebody can tick.</summary>
    public const string Task = "task";

    /// <summary>A grid of rows and cells, written with pipes or with a frame.</summary>
    public const string Table = "table";

    /// <summary>One line of a table.</summary>
    public const string Row = "row";

    /// <summary>One cell of a row.</summary>
    public const string Cell = "cell";

    /// <summary>A term and what it means.</summary>
    public const string Definition = "definition";

    /// <summary>The term of one.</summary>
    public const string Term = "term";

    /// <summary>What a term is explained by — the <c>:</c> line under it.</summary>
    public const string Described = "described";

    /// <summary>A note written at the foot and pointed at from the text.</summary>
    public const string Footnote = "footnote";

    /// <summary>The YAML between two fences at the top: what a document says about itself.</summary>
    public const string FrontMatter = "frontmatter";

    /// <summary>A link's destination written once and named from several places.</summary>
    public const string Reference = "reference";

    /// <summary>What a whole document defines for the words in it — hung on the document by its reading, never written anywhere as one (<see cref="MarkdownDefinitions"/>).</summary>
    public const string Definitions = "definitions";

    /// <summary>Markup a writer put in by hand, held as written because it is not markdown.</summary>
    public const string Html = "html";

    // ── Inlines ─────────────────────────────────────────────────────────────

    /// <summary>A run of characters that are only themselves.</summary>
    public const string Word = "word";

    /// <summary>Several of them in a row: what a construct was put round.</summary>
    public const string Words = "words";

    /// <summary><c>*one*</c> — set slanted.</summary>
    public const string Emphasis = "emphasis";

    /// <summary><c>**two**</c> — set heavy.</summary>
    public const string Strong = "strong";

    /// <summary><c>~~struck~~</c>.</summary>
    public const string Strike = "strike";

    /// <summary><c>==marked==</c> — washed rather than recoloured, so it reads as a highlighter would.</summary>
    public const string Mark = "mark";

    /// <summary><c>++inserted++</c> — underlined.</summary>
    public const string Insert = "insert";

    /// <summary><c>~under~</c>.</summary>
    public const string Sub = "sub";

    /// <summary><c>^over^</c>.</summary>
    public const string Sup = "sup";

    /// <summary>
    /// <c>`as typed`</c>, and a block of the same. Its body is <see cref="Ast.Kinds.Verbatim"/>, because nothing
    /// inside code is read — an asterisk in there is an asterisk.
    /// </summary>
    public const string Code = "code";

    /// <summary>
    /// A block written between two rows of backticks, naming the language inside it. Its body is that
    /// language's own source, read by that language's own parser — a tune, a formula, a diagram.
    /// </summary>
    public const string Fence = "fence";

    /// <summary>
    /// A formula written between two rows of <c>$$</c>. A fence spelled another way, with the language it holds
    /// implied by the delimiter rather than written after it — so its body is LaTeX and its shape is a fence's:
    /// an opening token, the body, a closing token.
    /// </summary>
    public const string Math = "math";

    /// <summary><c>$x^2$</c> — a formula written in the middle of a sentence, set on the line it was written on.</summary>
    public const string Formula = "formula";

    /// <summary><c>[words](where)</c>, and the bracketed and bare URL forms.</summary>
    public const string Link = "link";

    /// <summary><c>![words](where)</c>.</summary>
    public const string Image = "image";

    /// <summary><c>&amp;amp;</c> — a character spelled out because the text could not hold it.</summary>
    public const string Entity = "entity";

    /// <summary>The end of a line, hard or soft.</summary>
    public const string Break = "break";

    /// <summary><c>\*</c> — a character written behind a backslash so markdown reads it as itself.</summary>
    public const string Escape = "escape";

    /// <summary><c>""quoted""</c> — a citation, raised and set small.</summary>
    public const string Citation = "citation";

    /// <summary>A word a <c>*[…]:</c> line elsewhere said what it stands for.</summary>
    public const string Abbreviation = "abbreviation";

    /// <summary>A block set apart with a caption under it.</summary>
    public const string Figure = "figure";

    /// <summary>What a figure calls itself, or the words under a page.</summary>
    public const string Caption = "caption";

    /// <summary><c>^^ …</c> — what stands at the foot of the page.</summary>
    public const string Footer = "footer";

    // ── What a stage works out ──────────────────────────────────────────────

    /// <summary>
    /// Which way a table's column is set. Derived, because nobody wrote it on the cell: the colons that say it
    /// are in a rule of their own, in another row.
    /// </summary>
    public const string Aligned = "aligned";

    /// <summary>
    /// How a list counts itself — which letters or numerals its markers are, and what the first one is.
    /// Derived, because only the reader can tell <c>i.</c> the roman numeral from <c>i.</c> the ninth letter.
    /// </summary>
    public const string Numbering = "numbering";

    /// <summary>How many columns or rows a cell was written to cover.</summary>
    public const string Spans = "spans";

    /// <summary>That what is written in a cell is blocks rather than a run of words.</summary>
    public const string Blocks = "blocks";

    /// <summary>
    /// The name a heading answers to when a link points at it. Derived: nobody writes it, the reader works
    /// it out from the words, and two headings saying the same thing are told apart by where they are.
    /// </summary>
    public const string Anchor = "anchor";
}

/// <summary>What the parts of a markdown construct are to it, beside the shared <see cref="Ast.Roles"/>.</summary>
public static class MarkdownRoles
{
    /// <summary>Where a link or an image points.</summary>
    public const string Destination = "destination";

    /// <summary>What a link or an image says about itself when rested on.</summary>
    public const string Title = "title";

    /// <summary>The row of a table that names its columns.</summary>
    public const string Head = "head";

    /// <summary>An item still to do.</summary>
    public const string Todo = "todo";

    /// <summary>An item done.</summary>
    public const string Done = "done";

    /// <summary>What an abbreviation or a cell's span is, where a stage worked it out.</summary>
    public const string Means = "means";
}

/// <summary>Which way a column of a table is set, as the rule under its head says.</summary>
public static class MarkdownAligns
{
    public const string Left = "left";

    public const string Center = "center";

    public const string Right = "right";
}
