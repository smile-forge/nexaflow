using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// What a <c>mermaid</c> block is drawn as: the diagram its header names, laid out by that diagram's own language, as the whole
/// of what is drawn — nothing of the block's own is, so nothing of it is there to be found, picked out or written in
/// (<see cref="MermaidFenceParser"/>). A header naming no diagram is shown as written, the keyword marked.
/// </summary>
/// <param name="standing">How the block stands where it is laid — which is how the diagram it holds stands there too.</param>
internal sealed class PortableMermaidFenceBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly,
                                                  ILayoutMedium medium, PortableNesting nesting, PortableStanding standing)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, nesting)
{
    protected override PortableLaid? Build() =>
        Nested(Reading.Root, Room, standing)?.Laid
        ?? AsSource([.. Reading.Root.SelfAndDescendants().Where(part => part.Trouble is not null).Select(part => (part, part.Trouble!))]);

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);
}
