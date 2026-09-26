using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4.Stages;

/// <summary>
/// Hangs what a C4 sequence's front matter asks for on the block, with a foot box under each lifeline or not as the last
/// <c>SHOW_FOOT_BOXES()</c> says — a switch that may be written anywhere in the block, and so is the whole block's to say.
/// </summary>
/// <param name="config">What the front matter asks for (<see cref="C4Config.Read"/>).</param>
public sealed class ResolveFootBoxes(SequenceConfig config) : IAstStage
{
    public string Name => "c4:foot-boxes";

    public ContentNode Run(ContentNode tree) =>
        new ConfiguredNode<SequenceConfig>(tree, config with { Mirrored = C4Said.Read(tree).FootBoxes });
}
