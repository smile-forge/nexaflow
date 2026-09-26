using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Requirement.Stages;

/// <summary>
/// Works out what styles each requirement and element, and says where a <c>class</c> or a <c>style</c> line names something
/// nothing writes — a requirement written nowhere, or a class no <c>classDef</c> declares. Mermaid lets the styling be written
/// above what it styles or below it, so both are facts about the whole block (<see cref="MermaidStyling"/>): what styles a box
/// is said on the name first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "requirement:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = RequirementGrammar.Styling;

        tree = styling.Resolve(tree, [RequirementKinds.Named], RequirementKinds.ClassDef, RequirementKinds.CssClass,
                               RequirementKinds.Style, id => $"No requirement {id} is written in this diagram.");

        return styling.Style(tree, [RequirementKinds.Named], RequirementKinds.ClassDef, RequirementKinds.CssClass, RequirementKinds.Style, Given);
    }

    /// <summary>The class <c>:::</c> gives a requirement where it is named: <c>A:::blue</c>.</summary>
    private static IEnumerable<(string Id, string Class)> Given(ContentNode node)
    {
        if (node.Kind != RequirementKinds.Named) yield break;

        var id = node.SelfAndDescendants().FirstOrDefault(words => words.Kind == MermaidKinds.Words && words.Role == RequirementRoles.Id)?.Text;
        var given = node.SelfAndDescendants().FirstOrDefault(words => words.Kind == MermaidKinds.Words && words.Role == RequirementRoles.Class)?.Text;

        if (id is { Length: > 0 } && given is { Length: > 0 }) yield return (id, given);
    }
}
