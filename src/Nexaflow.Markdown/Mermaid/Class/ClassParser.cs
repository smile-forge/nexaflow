using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>
/// What a class diagram lets be written into it. The block is read by the shared <see cref="MermaidParser"/>, whose
/// stretch reads the members between a class's braces; what is this diagram's own is how its names are spelled.
///
/// <para>
/// A class is named where it is declared and reached by that name from every relation, note and <c>cssClass</c> line, so
/// a character that would end the name is refused. A name written between backticks holds whatever a bare one cannot,
/// and there the only character refused is the backtick that would close it early.
/// </para>
/// <para>
/// An annotation — <c>&lt;&lt;interface&gt;&gt;</c> — is not a name but is spelled all the same, because the angle
/// brackets that close it are written inside the line rather than around a quoted run.
/// </para>
/// </summary>
public sealed class ClassParser : ITranspile
{
    private ClassParser()
    {
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a class diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) => part.Role switch
    {
        ClassRoles.Id or ClassRoles.Class =>
            Backed(part)
                ? text.Contains(ClassGrammar.Backtick, System.StringComparison.Ordinal) ? null : text
                : text.All(ClassGrammar.Bare) ? text : null,

        ClassRoles.Kind => text.All(Annotated) ? text : null,

        _ => MermaidParser.Spelled(part, text),
    };

    /// <summary>Whether a character may stand in an annotation, which the angle brackets around it end.</summary>
    private static bool Annotated(char character) => character is not ('<' or '>' or '"');

    /// <summary>Whether a name is written between backticks, which hold anything a bare name cannot.</summary>
    private static bool Backed(ContentPart part) =>
        part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == ClassGrammar.Backtick) ?? false;
}
