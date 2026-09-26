using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class.Stages;

/// <summary>
/// Works out what each member of a class draws where that is not the characters written (<see cref="MemberNode"/>): type
/// parameters between tildes drawn between angle brackets, a <c>$</c> or a <c>*</c> taken off and said as how it is drawn,
/// what a method gives back set after a colon, and the <c>@@</c> link the Code feature writes taken off and said as where
/// pressing it leads.
/// </summary>
public sealed class ResolveMembers : IAstStage
{
    public string Name => "class:members";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Each(tree, node => node is { Kind: MermaidKinds.Words, Role: ClassRoles.Member, Width: > 0 } and not MemberNode
            ? MemberNode.Of(node)
            : node);
}
