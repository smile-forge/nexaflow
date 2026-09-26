using System.Linq;
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
/// It is drawn by the sequence diagram's own builder, and read by the sequence diagram's own reader: the lifelines, the
/// messages, the frames, the bars and the notes are all a sequence's, and only the words a reader wrote to say them differ.
/// What each macro means is C4's stages' to say, shared with the structural diagrams (<see cref="C4ElementNode"/>,
/// <see cref="C4RelationNode"/>, <see cref="C4BoundaryNode"/>); all that is here is what each comes to on a timeline.
/// </para>
/// </summary>
internal sealed class C4SequenceBuilder : SequenceBuilder
{
    /// <summary>The grading, worked out once from the theme this is drawn on.</summary>
    private readonly DiagramTone ink;

    internal C4SequenceBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) => this.ink = C4Grading.Of(style);

    /// <inheritdoc/>
    protected override Diagram Read(ContentPart root, SequenceConfig config) => new Macros().Read(root, config);

    /// <inheritdoc/>
    /// <remarks>C4's own grading, deepest for the outermost abstraction — see <see cref="DiagramTone"/>.</remarks>
    protected override (Brush Fill, Brush Stroke, Brush Ink, Brush Muted)? Toned(Card card)
    {
        if (card.Tone is not { } tone) return null;

        var (fill, stroke, ink) = this.ink.Card(tone, card.Fill, card.Border, card.Ink);

        return (fill, stroke, ink, DiagramTone.Muted(ink));
    }

    /// <inheritdoc/>
    protected override Brush? Swatch(Legend row) =>
        row.Tone is { } tone ? this.ink.Card(tone, row.Fill, row.Border, null).Fill : null;

    /// <summary>
    /// The sequence diagram's reader, taking C4's lines as its own: an element is a participant whose box is a card, a boundary
    /// a box round the lifelines written in it, closed by <c>}</c> as a frame is by <c>end</c>, and a relationship a message.
    /// Everything that is not a macro — <c>alt</c>, <c>note over</c>, <c>activate</c> — is the sequence diagram's own.
    /// </summary>
    private sealed class Macros : Reader
    {
        /// <inheritdoc/>
        protected override bool Closes(string kind) => kind == C4Kinds.Ends || base.Closes(kind);

        /// <inheritdoc/>
        protected override bool Opens(ContentPart stated, string key, string? inside, ISourcePart whole)
        {
            if (stated.Kind != C4Kinds.Boundary) return base.Opens(stated, key, inside, whole);

            Boxed(stated, key, stated.Argument(C4Means.Label) ?? stated.Argument(C4Means.Alias), (stated.Node as C4BoundaryNode)?.Paint.Fill, inside, whole);
            return true;
        }

        /// <inheritdoc/>
        protected override bool Claims(ContentPart stated, string? inside)
        {
            switch (stated.Node)
            {
                case C4ElementNode element:
                    Standing(stated, element, inside);
                    return true;

                case C4RelationNode relation:
                    Related(stated, relation);
                    return true;

                case C4LegendNode legend:
                    if (Legend.Count == 0) Legend.AddRange(legend.Keys.Select(key => new SequenceBuilder.Legend(key.Says, key.Fill, key.Border) { Tone = key.Tone }));
                    return true;

                // Every other macro sets something for the whole block, which its stages have already read; a pasted line is nothing.
                default:
                    return stated.Kind is C4Kinds.Macro or C4Kinds.Boundary or C4Kinds.Ends or C4Kinds.Aside;
            }
        }

        /// <summary>An element: a participant whose box is a card saying what it is and what it does.</summary>
        private void Standing(ContentPart stated, C4ElementNode element, string? inside)
        {
            if (stated.Argument(C4Means.Alias) is not { } alias) return;

            var one = Called(alias, alias.Text);

            one.Said = stated.Argument(C4Means.Label) ?? alias;
            one.Card = new Card
            {
                Stereotype = element.Stereotype,
                Technology = stated.Argument(C4Means.Technology),
                Said = element.Described ? stated.Argument(C4Means.Description) : null,
                Shape = Carded(element.Shape),
                Tone = element.Tone,
                Fill = element.Paint.Fill,
                Ink = element.Paint.Ink,
                Border = element.Paint.Border,
            };

            if (Holds(inside)) one.Box ??= inside;
        }

        /// <summary>A relationship: a message from one lifeline to another, carrying what it is done with.</summary>
        private void Related(ContentPart stated, C4RelationNode relation)
        {
            if (stated.Argument(C4Means.From) is not { } leaves || stated.Argument(C4Means.To) is not { } reaches) return;

            var from = Called(leaves, relation.From);
            var to = Called(reaches, relation.To);

            from.Reached = true;
            to.Reached = true;

            Items.Add(new Message(stated, from.Id, to.Id, Items.Count)
            {
                Said = stated.Argument(C4Means.Label),
                Under = [.. new[] { stated.Argument(C4Means.Technology), stated.Argument(C4Means.Description) }.OfType<ContentPart>()],
                Near = relation.Both ? Tip.Arrow : Tip.None,
                Far = Tip.Arrow,
                Dotted = relation.Stroke.Dotted,
                Ink = relation.Stroke.Ink,
                SaidInk = relation.Stroke.Said,
                Number = relation.Number,
            });
        }

        /// <summary>
        /// The outline a card is drawn with. A person is a card with a head above it — the three <c>SHOW_PERSON_*</c> styles are
        /// one shape here, since a lifeline's head is a column heading and the three differ only in how much of a figure they
        /// draw above the same card.
        /// </summary>
        private static DiagramCardShape Carded(C4Shape shape) => shape switch
        {
            C4Shape.Database => DiagramCardShape.Database,
            C4Shape.Queue => DiagramCardShape.Queue,
            C4Shape.Person => DiagramCardShape.Person,
            _ => DiagramCardShape.Box,
        };
    }
}
