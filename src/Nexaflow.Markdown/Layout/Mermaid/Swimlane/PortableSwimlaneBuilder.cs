using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid.Flowchart;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Swimlane;

namespace Nexaflow.Markdown.Layout.Mermaid.Swimlane;

/// <summary>
/// Draws a <c>swimlane-beta</c> diagram: a flowchart whose outermost subgraphs are lanes — a band each, running the whole length of
/// the chart with the lane's own name at the near end of it — so that what is drawn says who owns each step as well as what follows
/// what. A lane's cells come one to a rank, and a link handed to another lane goes across rather than on.
///
/// <para>
/// A swimlane is read as a flowchart and drawn in the flowchart's own drawing (<see cref="PortableFlowchartDrawing"/>), which is how
/// Mermaid reads and draws it: the grammar, the stages, the shapes, the links and the styling are all a flowchart's, and only the way
/// it is laid out differs. What the lanes themselves ask for is <see cref="SwimlaneConfig"/>, which its stages hang on the block with the
/// flowchart's own inside it.
/// </para>
/// </summary>
internal sealed class PortableSwimlaneBuilder : PortableContentBuilder
{
    private readonly Func<ContentPart, LayoutPicture?> _pictures;

    /// <param name="pictures">The picture a node names, as the host found it — or null where it found none.</param>
    internal PortableSwimlaneBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                     PortableNesting nesting, Func<ContentPart, LayoutPicture?> pictures)
        : base(reading, state, style, isReadOnly, medium, nesting)
    {
        _pictures = pictures;
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
        var lanes = PortableDiagramConfig.Of(Reading, SwimlaneConfig.Default);
        var drawing = new PortableBuilder(Medium);
        var body = new PortableFlowchartDrawing(Reading, State, Style, Medium, Ink, Words, Sheet, Folded, _pictures, (lanes.Sideways, lanes.Ordered),
                                                lanes.Chart)
            .Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);
}
