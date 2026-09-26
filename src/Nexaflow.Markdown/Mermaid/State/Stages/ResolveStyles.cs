using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State.Stages;

/// <summary>
/// Works out what styles each state, and says where a <c>class</c> or a <c>style</c> line names something nothing writes — a state
/// written nowhere, or a class no <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or below it, so
/// both are facts about the whole block (<see cref="MermaidStyling"/>): what styles a state is said on the name first writing it,
/// a <c>[*]</c> being styled as the dot it is (<see cref="MarkerNode"/>).
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "state:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = StateGrammar.Styling;

        tree = styling.Resolve(tree, [StateKinds.Named], StateKinds.ClassDef, StateKinds.Class, StateKinds.Style,
                               id => StateGrammar.Pseudo.Contains(id, StringComparer.Ordinal)
                                   ? null
                                   : $"No state {id} is written in this diagram.");

        return styling.Style(tree, [StateKinds.Named], StateKinds.ClassDef, StateKinds.Class, StateKinds.Style, Given, Marked);
    }

    private static string? Marked(ContentNode named) => (named as MarkerNode)?.Id;

    /// <summary>The class <c>:::</c> gives a state where it is named: <c>one:::busy</c>.</summary>
    private static IEnumerable<(string Id, string Class)> Given(ContentNode node)
    {
        if (node.Kind != StateKinds.Named) yield break;

        var names = node.Children.Where(child => child.Kind == MermaidKinds.Name).ToList();
        if (names.Count < 2) yield break;

        if ((Marked(node) ?? names[0].Words()?.Text) is { Length: > 0 } id && names[1].Words()?.Text is { Length: > 0 } given)
            yield return (id, given);
    }
}
