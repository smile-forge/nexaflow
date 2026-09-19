using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State.Stages;

/// <summary>
/// Says where a <c>class</c> or a <c>style</c> line names something nothing writes — a state written nowhere, or a class no
/// <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or below it, so whether what it names exists
/// is a fact about the whole block (<see cref="MermaidStyling.Resolve"/>).
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "state:styles";

    public ContentNode Run(ContentNode tree) =>
        StateGrammar.Styling.Resolve(tree, [StateKinds.Named], StateKinds.ClassDef, StateKinds.Class, StateKinds.Style,
                                     id => StateGrammar.Pseudo.Contains(id, StringComparer.Ordinal)
                                         ? null
                                         : $"No state {id} is written in this diagram.");
}
