using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Pie.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// The actors a <c>pie</c> block runs between the parser and the builder.
///
/// <para>
/// What a slice is drawn in is written in the front matter by position rather than on the slice, so it has to be worked
/// out and hung underneath. And on a surface being written on, a label or a value with nothing written in it yet gets a
/// hole, as a formula's empty argument does — the reading says one belongs there, and the builder draws it. Everything
/// else a pie says, it says in its own characters.
/// </para>
/// </summary>
public static class PiePipeline
{
    /// <summary>The tree to draw a block from: what was written, with each slice's colour and highlight under it.</summary>
    /// <param name="holes">
    /// Whether a label or a value not yet written gets a hole standing in it. Asked for by a surface being written on, where
    /// the hole is how a reader sees there is something still to write and how they aim at it.
    /// </param>
    public static ContentNode Read(string? block, bool holes = false)
    {
        var tree = MermaidParser.Parse(block);
        return Of(PieConfig.Read(MermaidBlock.Of(tree).Config), holes).Run(tree);
    }

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of(PieConfig config, bool holes = false) =>
        new AstPipeline(new ResolveSlices(config)).Then(holes ? new WithHoles(Holds) : null);

    /// <summary>Where something belongs in a pie: between a label's quotes, and after a slice's colon.</summary>
    private static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is PieKinds.Label or PieKinds.Worth;
}
