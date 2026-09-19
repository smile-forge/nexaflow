using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class.Stages;

/// <summary>
/// Says which namespace each line is written in, and which one each <c>namespace … {</c> line opens. Which box a class is drawn in
/// depends on every brace written above it, and so is a fact about the whole block rather than about any one line
/// (<see cref="MermaidNesting"/>).
/// </summary>
public sealed class ResolveNamespaces : IAstStage
{
    public string Name => "class:namespaces";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, ClassKinds.Namespace, ClassKinds.Ends,
                              [ClassKinds.Class, ClassKinds.Body, ClassKinds.Says, ClassKinds.Relation, ClassKinds.Annotation,
                               ClassKinds.Note, ClassKinds.Direction, ClassKinds.CssClass, ClassKinds.ClassDef, ClassKinds.Style,
                               ClassKinds.Click],
                              ClassKinds.Fact, ClassRoles.Inside, ClassRoles.Opened,
                              stray: "This brace closes a namespace, and none is open here.",
                              unclosed: "This namespace is never closed: } closes it.");
}
