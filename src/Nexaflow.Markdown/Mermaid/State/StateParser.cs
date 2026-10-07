using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// What a state diagram lets be written into it.
///
/// <para>
/// The block itself is read by the shared <see cref="MermaidParser"/>: a state diagram's statement ends where its line
/// does, and a note running over several lines is a stretch the shared reader already reads. What belongs to this
/// diagram alone is how a name is spelled, which is why it owns a parser at all.
/// </para>
/// <para>
/// A state is named by its id, and a transition, a <c>class</c>, a <c>style</c> and a <c>note</c> line all reach a state
/// by that name. A character that would end a name is not written at all, because writing it would leave every line
/// naming the state pointing at something that is not there. A class is named the same way and reached the same way.
/// </para>
/// </summary>
public sealed class StateParser : ITranspile
{
    private StateParser()
    {
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change, Spelled);

    /// <summary>A change as a state diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
            part.Role is StateRoles.Id or StateRoles.Class
                ? text.All(StateGrammar.Bare) && !Keyworded(text) ? text : null
                : MermaidParser.Spelled(part, text);

        /// <summary>
        /// Whether a name would be read as the word a line is read by. The first word of a line says what kind of line it
        /// is, ignoring case, so a state called <c>state</c> turns its own line into a state declaration and whatever
        /// followed the name stops being read as a transition at all.
        /// </summary>
        private static bool Keyworded(string text) => StateGrammar.Keywords.Contains(text, StringComparer.OrdinalIgnoreCase);
}
