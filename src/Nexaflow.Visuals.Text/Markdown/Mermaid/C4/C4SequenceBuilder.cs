using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>
/// Draws a <c>C4Sequence</c>: a sequence diagram written in C4-PlantUML's words. An element macro is a participant whose box
/// is a card saying what it is and what it does, a <c>Boundary</c> is the box grouping the lifelines it holds, and a
/// <c>Rel</c> is a message carrying what it is done with.
///
/// <para>
/// It is drawn by the sequence diagram's own builder, which is the whole point of reading it into the very same model: the
/// lifelines, the messages, the frames, the bars and the notes are all a sequence's, and only the words a reader wrote to say
/// them differ. Only the reading is its own (<see cref="C4Sequence"/>).
/// </para>
/// </summary>
internal sealed class C4SequenceBuilder : SequenceBuilder
{
    /// <summary>The grading, worked out once from the theme this is drawn on.</summary>
    private readonly DiagramTone ink;

    internal C4SequenceBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) => this.ink = C4Grading.Of(style);

    /// <inheritdoc/>
    protected override SequenceDiagram Of(MermaidBlock block) => C4Sequence.Of(block);

    /// <inheritdoc/>
    /// <remarks>C4's own grading, deepest for the outermost abstraction — see <see cref="DiagramTone"/>.</remarks>
    protected override (Brush Fill, Brush Stroke, Brush Ink, Brush Muted)? Toned(SequenceCard card)
    {
        if (card.Tone is not { } tone) return null;

        var (fill, stroke, ink) = this.ink.Card(tone, card.Fill, card.Border, card.Ink);

        return (fill, stroke, ink, DiagramTone.Muted(ink));
    }

    /// <inheritdoc/>
    protected override Brush? Swatch(SequenceLegend row) =>
        row.Tone is { } tone ? this.ink.Card(tone, row.Fill, row.Border, null).Fill : null;

}
