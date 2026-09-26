using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The name first writing something a diagram draws, as its styling lines leave it (<see cref="MermaidStyling.Style"/>): what
/// it is drawn with. It prints as the name written.
/// </summary>
internal sealed class StyledNode : ContentNode
{
    internal StyledNode(ContentNode written, MermaidStyle style) : base(written) => this.Style = style;

    public MermaidStyle Style { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new StyledNode(shape, this.Style);
}
