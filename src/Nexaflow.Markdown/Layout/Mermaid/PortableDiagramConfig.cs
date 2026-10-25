using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>What a diagram's front matter asks for, as its stages hung it on the block (<see cref="WithConfig{TConfig}"/>).</summary>
internal static class PortableDiagramConfig
{
    /// <summary>What the front matter asks for — or <paramref name="otherwise"/>, the diagram's defaults, where its stages hung nothing.</summary>
    public static TConfig Of<TConfig>(ContentReading reading, TConfig otherwise) where TConfig : class =>
        (reading.Root.Node as ConfiguredNode<TConfig>)?.Config ?? otherwise;
}
