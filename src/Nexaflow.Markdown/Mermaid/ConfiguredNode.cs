using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A block as its stages leave it where all its diagram says of the block itself is what the front matter asks for
/// (<see cref="WithConfig{TConfig}"/>). It prints as the block it was written as.
/// </summary>
internal sealed class ConfiguredNode<TConfig> : ContentNode where TConfig : class
{
    internal ConfiguredNode(ContentNode written, TConfig config) : base(written) => this.Config = config;

    /// <summary>What the front matter asks for, with the diagram's own default wherever it asks nothing.</summary>
    public TConfig Config { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new ConfiguredNode<TConfig>(shape, this.Config);
}
