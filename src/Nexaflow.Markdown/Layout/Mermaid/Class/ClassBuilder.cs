using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid.Class;

/// <summary>
/// Draws a <c>classDiagram</c> in the class diagram's own drawing (<see cref="ClassDrawing"/>): each class a box of its
/// compartments, its relations the lines between them, its namespaces boxes round what they hold.
/// </summary>
internal sealed class ClassBuilder : ContentBuilder
{
    internal ClassBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                  LayoutHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
        Folded = new DiagramFolding(reading, style, Words, Ink);    }
    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private DiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private DiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private DiagramSheet Sheet { get; }

    /// <summary>How much of the diagram is drawn, as the front matter and the reader's folding ask.</summary>
    private DiagramFolding Folded { get; }
    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = new ClassDrawing(Reading, State, Style, Medium, Ink, Words, Sheet, Folded).Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);
}
