using System.Text;

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
/// <para>
/// Which diagram it is and what its front matter says are asked of the tree's nodes; only the parts — a title, the
/// header, the front matter — need the tree placed, every part of it given where it stands, and that is done the first
/// time one of them is asked for. Reading a block asks which grammar reads it and what its front matter configures, and
/// neither is worth placing a whole diagram for.
/// </para>
/// </summary>
public sealed class MermaidBlock
{
    private readonly ContentNode _tree;
    private readonly int _at;
    private ContentReading? _reading;
    private bool _found;

    private ContentPart? _frontMatter;
    private ContentPart? _header;
    private ContentPart? _ownTitle;
    private ContentPart? _frontMatterTitle;
    private ContentPart? _accessibleTitle;
    private ContentPart? _accessibleDescription;

    private MermaidBlock(ContentNode tree, int at, ContentReading? reading)
    {
        _tree = tree;
        _at = at;
        _reading = reading;
    }

    /// <summary>Reads <paramref name="source"/>.</summary>
    public static MermaidBlock Read(string? source) => Of(MermaidParser.Parse(source));

    /// <summary>The block a tree reads as, positioned <paramref name="at"/> in the document that holds it.</summary>
    public static MermaidBlock Of(ContentNode tree, int at = 0) => new(tree, at, null);

    /// <summary>A block already read, taken as it stands.</summary>
    public static MermaidBlock Of(ContentReading reading) => new(reading.Root.Node, reading.Root.Start, reading);

    /// <summary>The tree, with where each part sits.</summary>
    public ContentReading Reading => _reading ??= ContentReading.Of(_tree, _at);

    /// <summary>The source the block was read from.</summary>
    public string Source => Reading.Source;

    /// <summary>
    /// Where <see cref="Source"/> begins in the document that holds it. Every part is counted in the document; a slice of
    /// the source is counted from here.
    /// </summary>
    private int Origin => Reading.Root.Start;

    /// <summary>The <c>---</c> … <c>---</c> block the diagram opens with, fences included, or null.</summary>
    public ContentPart? FrontMatter => Found()._frontMatter;

    /// <summary>The line in the header's place — the first that is not blank, a comment or a directive — or null.</summary>
    public ContentPart? Header => Found()._header;

    /// <summary>The header's keyword, or null where there is no header or it starts with none.</summary>
    public ContentPart? Keyword => Header?.Part(Roles.Name);

    /// <summary>
    /// Which diagram this is. A header that names nothing this reads is <see cref="MermaidDiagram.Unknown"/>; no header at
    /// all is a <see cref="MermaidDiagram.Flowchart"/> — see <see cref="MermaidDiagrams.Named"/>.
    /// </summary>
    public MermaidDiagram Diagram
    {
        get
        {
            foreach (var node in _tree.SelfAndDescendants())
                if (node.Kind == MermaidKinds.Header)
                    return node.Part(Roles.Name) is { } keyword ? MermaidDiagrams.Named(keyword.Text) : MermaidDiagram.Unknown;

            return MermaidDiagram.Flowchart;
        }
    }

    /// <summary>
    /// The value of the front matter's <c>title:</c> — the one at the top level, not one nested under <c>config:</c> —
    /// or null. Quotes and all, as written: <see cref="FrontMatterTitleText"/> is what it says.
    /// </summary>
    public ContentPart? FrontMatterTitle => Found()._frontMatterTitle;

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

    /// <summary>What the title says, read back from its entity codes — what is drawn where nobody is writing in it.</summary>
    public string? TitleSays => TitleText is { } text ? MermaidText.Decode(text) : null;

    /// <summary>Whether the title says exactly the characters it is written as, so each one drawn stands where it was typed.</summary>
    public bool TitleAsWritten => Title is { } title && TitleSays == title.Text;

    /// <summary>What the diagram's own first title says, or null where it writes none.</summary>
    private ContentPart? OwnTitle => Found()._ownTitle;

    /// <summary>
    /// What is between the front matter's fences — the YAML the diagrams that take a <c>config:</c> read — or null where
    /// there is no front matter. The last line's own line ending is not part of it, and every other character is.
    /// </summary>
    public string? Config
    {
        get
        {
            foreach (var node in _tree.SelfAndDescendants())
            {
                if (node.Kind != MermaidKinds.FrontMatter) continue;
                if (node.Children.Count < 2) return null;

                // Everything between the opening fence and the closing one, as it prints.
                var inner = new StringBuilder();
                for (var at = 1; at < node.Children.Count - 1; at++) node.Children[at].PrintTo(inner);

                var text = inner.ToString();
                return text.EndsWith('\n') ? text[..^1] : text;
            }

            return null;
        }
    }

    /// <summary>
    /// Where the diagram starts, in <see cref="Source"/>: past the front matter where there is one, and the start of the
    /// block where there is not.
    /// </summary>
    public int BodyStart => FrontMatter is { } front ? front.End - Origin : 0;

    /// <summary>The block from <see cref="BodyStart"/> on — the header, and everything the diagram says.</summary>
    public string Body => Source[BodyStart..];

    /// <summary>The text of an <c>accTitle</c>, or null.</summary>
    public ContentPart? AccessibleTitle => Found()._accessibleTitle;

    /// <summary>The text of an <c>accDescr</c>, on one line or in braces, or null.</summary>
    public ContentPart? AccessibleDescription => Found()._accessibleDescription;

    /// <summary>The diagram's own lines, in order — what its grammar reads.</summary>
    public IEnumerable<ContentPart> Statements =>
        Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Statement);

    /// <summary>The parts every block is asked for, found once, the first time any of them is.</summary>
    private MermaidBlock Found()
    {
        if (_found) return this;
        _found = true;

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case MermaidKinds.FrontMatter when _frontMatter is null:
                    _frontMatter = part;
                    break;
                case MermaidKinds.Header when _header is null:
                    _header = part;
                    break;
                case MermaidKinds.Title when _ownTitle is null:
                    _ownTitle = part.Part(MermaidRoles.Title);
                    break;
                case MermaidKinds.Accessibility:
                    if (part.Part(Roles.Name)?.Text == MermaidParser.AccessibleTitle) _accessibleTitle ??= part.Part(MermaidRoles.Value);
                    else _accessibleDescription ??= part.Part(MermaidRoles.Value);
                    break;
            }
        }

        // A field at the top level is the first thing on its line; one nested under another starts with its indent.
        _frontMatterTitle = _frontMatter?.Children
            .Where(line => line.Children.Count > 0 && line.Children[0].Kind == MermaidKinds.Field)
            .Select(line => line.Children[0])
            .FirstOrDefault(field => string.Equals(field.Part(Roles.Name)?.Text, "title", StringComparison.OrdinalIgnoreCase))
            ?.Part(MermaidRoles.Value);

        return this;
    }
}
