using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Er.Stages;

/// <summary>
/// Works out what styles each entity, and says where a <c>class</c> or a <c>style</c> line names something nothing writes — an
/// entity written nowhere, or a class no <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or
/// below it, so both are facts about the whole block (<see cref="MermaidStyling"/>): what styles an entity is said on the name
/// first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "er:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = ErGrammar.Styling;

        tree = styling.Resolve(tree, [ErKinds.Named], ErKinds.ClassDef, ErKinds.CssClass, ErKinds.Style,
                               id => $"No entity {id} is written in this diagram.");

        return styling.Style(tree, [ErKinds.Named], ErKinds.ClassDef, ErKinds.CssClass, ErKinds.Style, Given);
    }

    /// <summary>The classes <c>:::</c> gives an entity where it is named: <c>A:::blue,bold</c>.</summary>
    private static IEnumerable<(string Id, string Class)> Given(ContentNode node)
    {
        if (node.Kind != ErKinds.Named) yield break;

        var id = node.SelfAndDescendants().FirstOrDefault(words => words.Kind == MermaidKinds.Words && words.Role == ErRoles.Id)?.Text;
        if (id is not { Length: > 0 }) yield break;

        foreach (var given in node.SelfAndDescendants().Where(words => words.Kind == MermaidKinds.Words && words.Role == ErRoles.Class))
            if (given.Text is { Length: > 0 } name) yield return (id, name);
    }
}
