using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A <c>mermaid</c> block, read: which diagram it is, what its front matter says, and where the diagram itself starts.
///
/// <para>
/// Everything here is asked of the tree <see cref="MermaidParser"/> made, rather than of the characters, so a title is
/// a part a builder can draw and a reader can select — and so what counts as front matter, a directive or the header
/// is decided once, by the parser, instead of again by everything that wants to know.
/// </para>
/// </summary>
public sealed class MermaidBlock
{
    private MermaidBlock(ContentReading reading)
    {
        Reading = reading;

        foreach (var part in reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case MermaidKinds.FrontMatter when FrontMatter is null:
                    FrontMatter = part;
                    break;
                case MermaidKinds.Header when Header is null:
                    Header = part;
                    break;
                case MermaidKinds.Title when OwnTitle is null:
                    OwnTitle = part.Part(MermaidRoles.Title);
                    break;
                case MermaidKinds.Accessibility:
                    if (part.Part(Roles.Name)?.Text == MermaidParser.AccessibleTitle) AccessibleTitle ??= part.Part(MermaidRoles.Value);
                    else AccessibleDescription ??= part.Part(MermaidRoles.Value);
                    break;
            }
        }

        // A field at the top level is the first thing on its line; one nested under another starts with its indent.
        FrontMatterTitle = FrontMatter?.Children
            .Where(line => line.Children.Count > 0 && line.Children[0].Kind == MermaidKinds.Field)
            .Select(line => line.Children[0])
            .FirstOrDefault(field => string.Equals(field.Part(Roles.Name)?.Text, "title", StringComparison.OrdinalIgnoreCase))
            ?.Part(MermaidRoles.Value);
    }

    /// <summary>Reads <paramref name="source"/>.</summary>
    public static MermaidBlock Read(string? source) => Of(MermaidParser.Parse(source));

    /// <summary>The block a tree reads as, positioned <paramref name="at"/> in the document that holds it.</summary>
    public static MermaidBlock Of(ContentNode tree, int at = 0) => new(ContentReading.Of(tree, at));

    /// <summary>The tree, with where each part sits.</summary>
    public ContentReading Reading { get; }

    /// <summary>The source the block was read from.</summary>
    public string Source => Reading.Source;

    /// <summary>The <c>---</c> … <c>---</c> block the diagram opens with, fences included, or null.</summary>
    public ContentPart? FrontMatter { get; }

    /// <summary>The line in the header's place — the first that is not blank, a comment or a directive — or null.</summary>
    public ContentPart? Header { get; }

    /// <summary>The header's keyword, or null where there is no header or it starts with none.</summary>
    public ContentPart? Keyword => Header?.Part(Roles.Name);

    /// <summary>
    /// Which diagram this is. A header that names nothing this reads is <see cref="MermaidDiagram.Unknown"/>; no header at
    /// all is a <see cref="MermaidDiagram.Flowchart"/> — see <see cref="MermaidDiagrams.Named"/>.
    /// </summary>
    public MermaidDiagram Diagram => Header is null
        ? MermaidDiagram.Flowchart
        : Keyword is { } keyword ? MermaidDiagrams.Named(keyword.Text) : MermaidDiagram.Unknown;

    /// <summary>
    /// The value of the front matter's <c>title:</c> — the one at the top level, not one nested under <c>config:</c> —
    /// or null. Quotes and all, as written: <see cref="FrontMatterTitleText"/> is what it says.
    /// </summary>
    public ContentPart? FrontMatterTitle { get; }

    /// <summary>The front-matter title as it reads, without the quotes round it, or null where there is none or it is blank.</summary>
    public string? FrontMatterTitleText
    {
        get
        {
            var text = FrontMatterTitle?.Text.Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    /// <summary>
    /// The title the diagram is drawn under, as written: its own — on a <c>title</c> line, or after its header's keyword
    /// (<see cref="MermaidKinds.Title"/>) — and otherwise the front matter's; or null.
    /// </summary>
    public ContentPart? Title => OwnTitle ?? FrontMatterTitle;

    /// <summary>What <see cref="Title"/> says, without the quotes a front-matter one may carry — or null where there is none, or it is blank.</summary>
    public string? TitleText => OwnTitle is { } own ? string.IsNullOrWhiteSpace(own.Text) ? null : own.Text : FrontMatterTitleText;

    /// <summary>What the diagram's own first title says, or null where it writes none.</summary>
    private ContentPart? OwnTitle { get; }

    /// <summary>
    /// What is between the front matter's fences — the YAML the diagrams that take a <c>config:</c> read — or null where
    /// there is no front matter. The last line's own line ending is not part of it, and every other character is.
    /// </summary>
    public string? Config
    {
        get
        {
            if (FrontMatter is not { Children: [var open, .., var close] }) return null;

            var inner = Source[open.End..close.Start];
            return inner.EndsWith('\n') ? inner[..^1] : inner;
        }
    }

    /// <summary>Where the diagram starts: past the front matter where there is one, and the start of the block where there is not.</summary>
    public int BodyStart => FrontMatter?.End ?? 0;

    /// <summary>The block from <see cref="BodyStart"/> on — the header, and everything the diagram says.</summary>
    public string Body => Source[BodyStart..];

    /// <summary>The text of an <c>accTitle</c>, or null.</summary>
    public ContentPart? AccessibleTitle { get; }

    /// <summary>The text of an <c>accDescr</c>, on one line or in braces, or null.</summary>
    public ContentPart? AccessibleDescription { get; }

    /// <summary>The diagram's own lines, in order — what its grammar reads.</summary>
    public IEnumerable<ContentPart> Statements =>
        Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Statement);
}
