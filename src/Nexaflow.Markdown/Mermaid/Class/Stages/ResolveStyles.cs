using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class.Stages;

/// <summary>
/// Works out what styles each class, and says where a <c>cssClass</c> or a <c>style</c> line names something nothing writes — a
/// class declared nowhere, or a class no <c>classDef</c> declares. Mermaid lets the styling be written above what it styles or
/// below it, so both are facts about the whole block (<see cref="MermaidStyling"/>): what styles a class is said on the name
/// first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "class:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = ClassGrammar.Styling;

        tree = styling.Resolve(tree, [ClassKinds.Named], ClassKinds.ClassDef, ClassKinds.CssClass, ClassKinds.Style,
                               id => $"No class {id} is written in this diagram.");

        return styling.Style(tree, [ClassKinds.Named], ClassKinds.ClassDef, ClassKinds.CssClass, ClassKinds.Style, Given);
    }

    /// <summary>The classes <c>:::</c> gives a class where it is named: <c>class A:::blue</c>.</summary>
    private static IEnumerable<(string Id, string Class)> Given(ContentNode node)
    {
        if (node.Kind != ClassKinds.Named) yield break;

        var id = node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)?.Words()?.Text;
        if (id is not { Length: > 0 }) yield break;

        foreach (var given in node.SelfAndDescendants().Where(words => words.Kind == MermaidKinds.Words && words.Role == ClassRoles.Class))
            if (given.Text is { Length: > 0 } name) yield return (id, name);
    }
}
