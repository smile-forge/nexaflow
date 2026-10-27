using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// How much of a graph-shaped diagram is drawn, as a <c>config: nexaflow:</c> block and the reader's own folding ask: which nodes
/// are drawn at all, the chip a node folds by, and the node offering what is left of an over-wide set of children.
///
/// <para>
/// A builder whose diagram can be folded makes one and hands it the nodes and the links between them (<see cref="Fold"/>) before
/// it places anything, so a node that is not drawn is never given a cell — rather than taken out afterwards, which would leave a
/// hole where it stood and a line running to nothing.
/// </para>
/// </summary>
internal sealed class DiagramFolding(ContentReading reading, LayoutStyle style, DiagramText text, DiagramInk ink)
{
    /// <summary>The room a node offering leftovers keeps round its words.</summary>
    private const double SpillPad = 10;

    /// <summary>How big a node offering leftovers says what it offers.</summary>
    private const double SpillSize = 11;

    /// <summary>What the front matter asked to be folded away (<see cref="WithFolds"/>).</summary>
    public NexaflowConfig Folds => this.folds ??= WithFolds.Of(reading.Root.Node);

    private NexaflowConfig? folds;

    /// <summary>How much of the diagram is drawn — everything, until <see cref="Fold"/> works it out.</summary>
    public DiagramExpansion Folding { get; private set; } = DiagramExpansion.None;

    /// <summary>Works out how much of the diagram is drawn, from its nodes and the links between them.</summary>
    public void Fold(DiagramChart chart) => Folding = DiagramExpansion.Of(Folds, chart, style.Expansion);

    /// <summary>Whether a node is drawn at all. Everything is, unless something asked otherwise.</summary>
    public bool Draws(string id) => Folding.Draws(id);

    /// <summary>The producer's name for a node — what an opening is remembered under, never the positional id.</summary>
    public string KeyFor(string id) => Folds.KeyFor(id);

    /// <summary>
    /// Draws the chip a node carries, where it carries one: over the top right-hand corner of what it fills, as a piece
    /// of its own, so the node's body and its folding are two separate things to press.
    /// </summary>
    public void Chipped(LayoutBuilder build, string id, LayoutRect bounds, ISourcePart? part, string? label = null)
    {
        if (Folding.FoldOf(id) is not { } fold) return;

        DiagramChip.Draw(build, DiagramChip.On(bounds), part, fold, KeyFor(id), label,
                                 text.Worked(DiagramChip.Says(fold), part as ContentPart, DiagramChip.TextSize, style.Text),
                                 ink.Surface, new DiagramStroke(style.CodeBorder, 1));
    }

    /// <summary>
    /// The nodes offering what is left of each over-wide set of children, ready to be laid out with the rest and drawn
    /// afterwards. Nothing, where nothing is over-wide.
    /// </summary>
    /// <param name="cells">The cell a node was given, or null for one this diagram did not lay out.</param>
    public DiagramSpill Spilled(Func<string, DiagramCell?> cells) =>
        DiagramSpill.Of(Folding, cells, More, SpillPad);

    /// <summary>What a node offering <paramref name="count"/> children says.</summary>
    private DiagramWords More(int count) => text.Worked($"+{count} more", null, SpillSize, style.TextMuted);
}
