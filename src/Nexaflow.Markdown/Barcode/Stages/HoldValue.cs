using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Barcode.Stages;

/// <summary>
/// Puts a hole where a barcode's value goes and none is written — <c>value:</c> with nothing after its colon — for a block
/// somebody is writing in, so they can see where it goes and type it there.
///
/// <para>
/// <see cref="WithHoles"/> puts a hole inside a piece that holds nothing yet. A value never written has no piece to be inside,
/// so this gives the line one, holding the hole: a piece that prints as nothing, standing where the value will.
/// </para>
/// </summary>
public sealed class HoldValue : IAstStage
{
    /// <summary>What the hole says for itself when a reader asks.</summary>
    public const string Says = "The value this barcode encodes goes here.";

    public string Name => "barcode:hold-value";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, node =>
        SpellValue.Values(node) && !node.Children.Any(child => child.Role == MatrixRoles.Value)
            ? node.With([.. node.Children, ContentNode.Branch(MatrixKinds.Value, [ContentNode.Leaf(Kinds.Hole, string.Empty, Roles.Element, Says)], MatrixRoles.Value)])
            : node);
}
