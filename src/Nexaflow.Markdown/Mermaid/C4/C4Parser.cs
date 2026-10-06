using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// What a C4 diagram lets be written into it, and a C4 sequence with it. The block is read by the shared
/// <see cref="MermaidParser"/>; what is C4's own is how an element's name is spelled.
///
/// <para>
/// A C4 element is named inside the brackets of a macro — <c>Person(customer, "Banking Customer")</c> — and every
/// relationship after it reaches the element by that name. A quote, a comma or a bracket written into the name closes
/// the macro early and leaves the rest of the line as something else, so none of them goes in.
/// </para>
/// <para>
/// C4 names its elements in <see cref="SequenceRoles.Id"/>, the role a sequence diagram names participants in, but the
/// two do not agree on what a name may hold: a sequence takes anything that is not one of its few stops, and C4 takes
/// only the letters an identifier is written with. A shared role is not a shared rule, which is why each spells its own.
/// </para>
/// </summary>
public sealed class C4Parser : ITranspile
{
    private C4Parser()
    {
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change, Spelled);

    /// <summary>A change as a C4 diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == SequenceRoles.Id
            ? text.All(C4Grammar.Bare) ? text : null
            : MermaidParser.Spelled(part, text);
}
