using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.State;

/// <summary>What an edit means in a state diagram: what an id cannot hold is dropped. Every other key does what it does anywhere.</summary>
internal sealed class StateEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        // A rename does not carry here yet. The sweep itself is right — AndRenamingAStateCarriesToEveryTransitionThatNamesIt
        // is written against it — but switching it on raises the backspace floor in DiagramKeyTests from 271 to 274, on the
        // three names the fork sample writes twice. That sample's tree already gives caret stops on a closing brace and on
        // the fence, so what the rename lands on there is not yet worth trusting.
        return change;
    }

    /// <summary>
    /// What is written on a state or a transition runs to the end of its line and holds anything but a comment. An id, a class and a
    /// way are written bare and cannot be quoted at all, so what they cannot hold is dropped.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is StateRoles.Id or StateRoles.Class) return MermaidWriting.Only(caret, text, StateGrammar.Bare);
        if (part.Role is StateRoles.Towards or StateRoles.Kind or StateRoles.Side)
            return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        if (part.Role is StateRoles.Width) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }
}
