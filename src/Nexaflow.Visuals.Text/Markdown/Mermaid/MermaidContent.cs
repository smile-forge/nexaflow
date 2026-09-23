using System;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A diagram shown on its own, as a piece of content: how its source is laid out, and — the same rules a diagram inside
/// a document is edited by — what writing into it means (<see cref="MermaidEdits"/>).
/// </summary>
/// <param name="lay">
/// Lays the state out at a width, told whether anybody can write in it — the builder, as the element asks for it.
/// </param>
internal sealed class MermaidContent(Func<EditState, double, bool, Laid> lay) : IContent
{
    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, bool readOnly) => lay(state, room, readOnly);

    /// <inheritdoc/>
    public EditState? Typing(Landing landing, string text) => MermaidEdits.Instance.Typing(Whole(landing), text);

    /// <inheritdoc/>
    public EditState Settle(Landing landing, string separator) =>
        MermaidEdits.Instance.Settling(Whole(landing), separator) ?? landing.State.Write(separator);

    /// <inheritdoc/>
    public EditState? Erasing(Landing landing, bool forward) => MermaidEdits.Instance.Erasing(Whole(landing), forward);

    /// <inheritdoc/>
    public EditState Edited(Landing before, EditState after) => MermaidEdits.Instance.Edited(Whole(before), after);

    /// <summary>The whole of what is shown, which is all diagram.</summary>
    private static ContentEdit Whole(Landing landing) => new(landing, 0, landing.State.Source.Length);
}
