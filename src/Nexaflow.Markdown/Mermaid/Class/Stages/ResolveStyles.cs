using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class.Stages;

/// <summary>
/// Says where a <c>cssClass</c> or a <c>style</c> line names something nothing writes — a class declared nowhere, or a class no
/// <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or below it, so whether what it names exists
/// is a fact about the whole block (<see cref="MermaidStyling.Resolve"/>).
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "class:styles";

    public ContentNode Run(ContentNode tree) =>
        ClassGrammar.Styling.Resolve(tree, [ClassKinds.Named], ClassKinds.ClassDef, ClassKinds.CssClass, ClassKinds.Style,
                                     id => $"No class {id} is written in this diagram.");
}
