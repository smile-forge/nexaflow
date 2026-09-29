using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// What a <c>mermaid</c> block is drawn as: the diagram its header names, laid out by that diagram's own language, as the whole
/// of what is drawn — nothing of the block's own is, so nothing of it is there to be found, picked out or written in
/// (<see cref="MermaidFenceParser"/>). A header naming no diagram is shown as written, the keyword marked.
/// </summary>
internal sealed class MermaidFenceBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
    : ContentBuilder(reading, state, style, isReadOnly, nesting)
{
    protected override Laid? Build() =>
        Nested(Reading.Root, Room)?.Laid
        ?? AsSource([.. Reading.Root.SelfAndDescendants().Where(part => part.Trouble is not null).Select(part => (part, part.Trouble!))]);

    protected override FormattedText Characters(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Style.Face(new FontFamily("Cascadia Code, Consolas, monospace")), 13,
            Style.Text, LayoutText.Density);
}
