using System.Windows.Media;
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

    private C4SequenceBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) => this.ink = C4Grading.Of(palette);

    /// <summary>Lays a C4 sequence's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static new Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip,
                                 double room = double.PositiveInfinity, bool writing = false) =>
        new C4SequenceBuilder(state, palette, pixelsPerDip, room, writing).Lay();

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

}
