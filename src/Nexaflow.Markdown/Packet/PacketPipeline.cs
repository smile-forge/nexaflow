using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Packet;

/// <summary>The stages a parsed packet is worked over by.</summary>
public static class PacketPipeline
{
    /// <summary>
    /// What each escape in a label says, then which bits each field takes and what the front matter asks for, then a hole
    /// wherever something is still to be written where somebody is writing, then what every run of words is made of.
    /// </summary>
    /// <param name="tree">The block as <see cref="PacketParser.Parse"/> read it.</param>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static IReadOnlyList<IAstStage> Of(ContentNode tree, bool holes)
    {
        var config = PacketConfig.Read(MermaidBlock.Of(tree).Config);

        return
        [
            new SayEscapes(),
            new PlaceFields(config),
            new WithConfig<PacketConfig>(config),
            .. holes ? [new WithHoles(PacketParser.Holds)] : Array.Empty<IAstStage>(),

            // Last, once every escape says what it keeps, so the pieces of a label say it too.
            new WithWordPieces(),
        ];
    }
}
