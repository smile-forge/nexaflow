using Nexaflow.Markdown.Ast;
using Nexaflow.Icons;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Something naming an icon (<see cref="MermaidKinds.Icon"/>), as <see cref="WithIcons"/> read it: the icon the app draws it with —
/// an emoji, a Fluent icon, or a pack's. It prints as what was written.
/// </summary>
internal sealed class RenderedIconNode : ContentNode
{
    internal RenderedIconNode(ContentNode written, IconRef icon) : base(written) => this.Icon = icon;

    public IconRef Icon { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new RenderedIconNode(shape, this.Icon);
}
