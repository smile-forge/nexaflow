using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Something naming an icon (<see cref="MermaidKinds.Icon"/>), as <see cref="WithIcons"/> read it: the glyph it is drawn with,
/// and the family of the font that glyph is in — or none for an emoji, which takes the font of the words round it. It prints
/// as what was written.
/// </summary>
internal sealed class RenderedIconNode : ContentNode
{
    internal RenderedIconNode(ContentNode written, string? family, string glyph) : base(written) => (this.Family, this.Glyph) = (family, glyph);

    public string? Family { get; }

    public string Glyph { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new RenderedIconNode(shape, this.Family, this.Glyph);
}
