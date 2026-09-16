using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Venn.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>
/// The actors a <c>venn-beta</c> block runs between the parser and the builder.
///
/// <para>
/// A set or a union is gathered with the items indented under it into one region (<see cref="GroupRegions"/>), so the
/// tree holds what the diagram is made of rather than only the lines it was written on. Which region each part stands
/// for — the sets a union overlaps, the region an item written on its own sits in, what a style styles — is a fact about
/// the lines above it rather than its own, so it is worked out and hung underneath (<see cref="ResolveRegions"/>). And
/// on a surface being written on, a name or a label with nothing written in it yet gets a hole, as a pie's label and value
/// do; a size gets none, being drawn as how much room a region takes rather than as anything to write in.
/// </para>
/// </summary>
public static class VennPipeline
{
    /// <summary>The tree to draw a block from: what was written, with the region each line stands for under it.</summary>
    /// <param name="holes">Whether a name or a label not yet written gets a hole standing in it.</param>
    public static ContentNode Read(string? block, bool holes = false) => Of(holes).Run(MermaidParser.Parse(block));

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of(bool holes = false) =>
        new AstPipeline(new GroupRegions(), new ResolveRegions()).Then(holes ? new WithHoles(Holds) : null);

    /// <summary>Where something belongs in a Venn diagram: between a name's quotes or after the comma before it, and between a label's brackets or its quotes.</summary>
    private static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is VennKinds.Id or VennKinds.Label
        || (node.Kind == VennKinds.Quoted && holder?.Kind == VennKinds.Label);
}
