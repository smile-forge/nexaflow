using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Hangs what a block's front matter asks for on the block (<see cref="ConfiguredNode{TConfig}"/>), read once, so what draws
/// the diagram takes it from the tree rather than reading the front matter's characters again.
///
/// <para>For a diagram with nothing else to say of the block; one that has says it in a block node of its own.</para>
/// </summary>
public sealed class WithConfig<TConfig>(TConfig config) : IAstStage where TConfig : class
{
    public string Name => "mermaid:config";

    public ContentNode Run(ContentNode tree) => new ConfiguredNode<TConfig>(tree, config);
}
