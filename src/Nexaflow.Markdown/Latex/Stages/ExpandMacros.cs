using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Hangs what each shorthand name means underneath it.
///
/// <para>
/// <c>\neq</c> is one command the writer typed and a slash over an equals sign to whoever sets it. The tree
/// carries both: the command, with its expansion beneath it as a <see cref="Roles.Derived"/> part. A reader
/// of the tree asks what was written and gets <c>\neq</c>; a builder walks down and finds the two things to
/// draw. The expansion is no characters wide and prints as nothing, so the source is untouched.
/// </para>
/// <para>
/// A stage rather than something the parser does as it reads, because it says what the text amounts to
/// rather than what it is: the parser finds a name, and <see cref="TexMacros"/> says what that name stands
/// for.
/// </para>
/// <para>
/// A definition may name another macro — <c>\iff</c> reaches <c>\Longleftrightarrow</c>, which is itself one
/// of these — so an expansion is expanded in turn, to a bound. Six is well past the deepest real chain and
/// shallow enough that a table naming itself stops rather than fills the stack.
/// </para>
/// </summary>
public sealed class ExpandMacros : IAstStage
{
    private const int Deepest = 6;

    public string Name => "latex:macros";

    public ContentNode Run(ContentNode tree) => Expanded(tree, 0);

    private static ContentNode Expanded(ContentNode tree, int depth) =>
        AstRewrite.Each(tree, node => Resolved(node, depth));

    /// <summary>
    /// The same command with what it stands for hung underneath it, where it is shorthand for anything.
    /// <para>
    /// Only a name the command table has no entry for can be one: nothing in the table takes arguments
    /// <em>and</em> is shorthand for something. And never twice, so running this over its own output
    /// changes nothing.
    /// </para>
    /// </summary>
    private static ContentNode Resolved(ContentNode node, int depth)
    {
        if (node.Kind != TexKinds.Command || depth >= Deepest) return node;
        if (node.Part(Roles.Derived) is not null) return node;
        if (node.Part(Roles.Name)?.Text is not { } name) return node;
        if (TexCommands.Lookup(name) is not null || TexMacros.Lookup(name) is not { } definition) return node;

        var expansion = Expanded(TexParser.Parse(definition), depth + 1).As(Roles.Derived);
        return node.With([.. node.Children, expansion]);
    }
}
