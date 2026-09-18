namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>
/// What a <c>block-beta</c> block's front matter asks for, under <c>config: block:</c>: the clear air inside every block,
/// which is also what goes between one block and the next.
///
/// <para>
/// Mermaid documents <c>useMaxWidth</c> beside it, which fits the drawing to the page it is on. Here the block is drawn at
/// the size its blocks come to and the document places it, so there is nothing for it to ask.
/// </para>
/// </summary>
public sealed record BlockConfig
{
    /// <summary>The clear air Mermaid leaves when the front matter asks for none.</summary>
    public const double Air = 8;

    public static BlockConfig Default { get; } = new();

    /// <summary>The clear air inside a block, and between it and whatever is beside it.</summary>
    public double Padding { get; init; } = Air;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static BlockConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static BlockConfig From(MermaidConfig config) => new()
    {
        Padding = config.Diagram("block").Number("padding") is { } padding and >= 0 ? padding : Air,
    };
}
