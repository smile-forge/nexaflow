using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid.Sequence;

/// <summary>
/// Draws a <c>sequenceDiagram</c> in the sequence diagram's own drawing (<see cref="SequenceDrawing"/>): a lifeline for each
/// participant, the messages between them in the order they are written, and the frames, bars and notes over them.
/// </summary>
internal sealed class SequenceBuilder : ContentBuilder
{
    /// <summary>A sequence diagram's own way of being read and painted, which differs from a sequence diagram's in nothing.</summary>
    private static readonly SequenceDrawing.Dialect Own = new();

    internal SequenceBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                     LayoutHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
            Folded = new DiagramFolding(reading, style, Words, Ink);
    }

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
        var body = new SequenceDrawing(Reading, State, Style, Medium, Ink, Words, Sheet, Folded, Own).Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);
}
