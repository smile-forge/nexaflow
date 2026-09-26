using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class.Stages;

/// <summary>
/// Gathers each namespace with the lines written in it, so the tree says which box every class is drawn in. That depends on
/// every brace written above it, and so is a fact about the whole block rather than about any one line
/// (<see cref="MermaidNesting.Nest"/>).
/// </summary>
public sealed class ResolveNamespaces : IAstStage
{
    public string Name => "class:namespaces";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [ClassKinds.Namespace], [ClassKinds.Ends],
                            stray: "This brace closes a namespace, and none is open here.",
                            unclosed: "This namespace is never closed: } closes it.");
}
