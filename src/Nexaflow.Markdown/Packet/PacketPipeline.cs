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
    /// Which bits each field takes and what the front matter asks for, then a hole wherever something is still to be written
    /// where somebody is writing, then what every run of words is made of — a label's escapes as <see cref="PacketStandIns"/>
    /// reads them.
    /// </summary>
    /// <param name="tree">The block as <see cref="PacketParser.Parse"/> read it.</param>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static IReadOnlyList<IAstStage> Of(ContentNode tree, bool holes)
    {
        var config = PacketConfig.Read(MermaidBlock.Of(tree).Config);

        return
        [
            new PlaceFields(config),
            new WithConfig<PacketConfig>(config),
            .. holes ? [new WithHoles(PacketParser.Holds)] : Array.Empty<IAstStage>(),

            // What every run of words is made of, a label's escapes and line endings reading as what they stand for.
            new WithWordPieces(PacketStandIns.Decoded),
        ];
    }
}
