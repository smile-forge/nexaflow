using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Requirement.Stages;

/// <summary>
/// Says where a <c>class</c> or a <c>style</c> line names something nothing writes — a requirement written nowhere, or a class no
/// <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or below it, so whether what it names exists
/// is a fact about the whole block (<see cref="MermaidStyling.Resolve"/>).
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "requirement:styles";

    public ContentNode Run(ContentNode tree) =>
        RequirementGrammar.Styling.Resolve(tree, [RequirementKinds.Named], RequirementKinds.ClassDef, RequirementKinds.CssClass,
                                           RequirementKinds.Style, id => $"No requirement {id} is written in this diagram.");
}
