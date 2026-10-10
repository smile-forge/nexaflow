using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>
/// What a sequence diagram lets be written into it. The block is read by the shared <see cref="MermaidParser"/>; what is
/// this diagram's own is how a participant's name is spelled.
///
/// <para>
/// A participant is named once and reached by that name from every message, note and activation after it, so a character
/// that would end the name is refused rather than written. What ends one here is its own small set — a sequence holds a
/// dash in a name, where a state diagram does not.
/// </para>
/// </summary>
public sealed class SequenceParser : ITranspile
{
    private SequenceParser()
    {
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a sequence diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == SequenceRoles.Id
            ? text.All(SequenceGrammar.Bare) ? text : null
            : MermaidParser.Spelled(part, text);
}
