using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid.Class;

/// <summary>
/// Draws a <c>classDiagram</c> in the class diagram's own drawing (<see cref="PortableClassDrawing"/>): each class a box of its
/// compartments, its relations the lines between them, its namespaces boxes round what they hold.
/// </summary>
internal sealed class PortableClassBuilder : PortableContentBuilder
{
    internal PortableClassBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                  PortableHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new PortableDiagramInk(style, medium);
        Words = new PortableDiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, PortableStanding.Inline));
        Sheet = new PortableDiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
        Folded = new PortableDiagramFolding(reading, style, Words, Ink);    }
    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private PortableDiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private PortableDiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private PortableDiagramSheet Sheet { get; }

    /// <summary>How much of the diagram is drawn, as the front matter and the reader's folding ask.</summary>
    private PortableDiagramFolding Folded { get; }
    protected override PortableLaid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new PortableBuilder(Medium);
        var body = new PortableClassDrawing(Reading, State, Style, Medium, Ink, Words, Sheet, Folded).Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);
}
