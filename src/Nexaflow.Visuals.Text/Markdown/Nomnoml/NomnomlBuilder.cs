using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

namespace Nexaflow.Visuals.Text.Markdown.Nomnoml;

/// <summary>
/// Draws a <c>nomnoml</c> block.
///
/// <para>
/// Nomnoml is UML class notation written shorter, so it is drawn by the class diagram's own builder and there is
/// nothing here but what differs: the grammar the block is read by, since its fence's language names it rather than
/// its first line, and the reader that turns what that grammar read into a <see cref="ClassDiagram"/>. Everything
/// about how a class, a compartment, a relation or a group is laid out and drawn belongs to
/// <see cref="ClassBuilder"/>, and a fix there is a fix to both.
/// </para>
/// </summary>
internal sealed class NomnomlBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
    : ClassBuilder(reading, state, style, isReadOnly)
{
    /// <inheritdoc/>
    protected override ClassDiagram Of(MermaidBlock block) => NomnomlDiagram.Of(block);
}
