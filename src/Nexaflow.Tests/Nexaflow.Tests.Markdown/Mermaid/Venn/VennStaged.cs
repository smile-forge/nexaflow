using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Mermaid.Venn.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Tests.Markdown.Mermaid.Venn;

/// <summary>
/// The stages a Venn diagram's language offers the engine (<c>Shipped.Venn</c>), named here one by one as the language names them —
/// for a test about what they make of a block, with nothing laid out. What the engine adds for every language (what a host binds,
/// what runs of words are made of) is held where the engine reads a block.
/// </summary>
internal static class VennStaged
{
    /// <summary>The stages, for <paramref name="tree"/>.</summary>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new GroupRegions(),
        new ResolveRegions(),
        new ResolveStyles(),
        new WithConfig<VennConfig>(VennConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(VennParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block read by <see cref="VennParser"/> and worked over by the stages.</summary>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static ContentNode Read(string source, bool holes = false)
    {
        var tree = VennParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }
}
