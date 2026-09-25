using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode.Stages;

/// <summary>
/// Spells a barcode's <c>value:</c> out a character at a time. What a symbology prints is a rendering of the value — a check
/// digit added, hyphens taken out, a start mark and a stop mark round it — and each character of the value it does print has
/// to be the one it came from, so the value is one piece per character rather than one run.
/// </summary>
public sealed class SpellValue : IAstStage
{
    public string Name => "barcode:spell-value";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, node =>
        Values(node)
            ? node.With([.. node.Children.Select(child => child.Role == MatrixRoles.Value && child.IsLeaf ? Spelled(child) : child)])
            : node);

    /// <summary>Whether <paramref name="node"/> is the <c>value:</c> line of a block.</summary>
    internal static bool Values(ContentNode node) =>
        node.Kind == MatrixKinds.Field
        && string.Equals(node.Children.FirstOrDefault(child => child.Role == Roles.Name)?.Text, "value", StringComparison.OrdinalIgnoreCase);

    private static ContentNode Spelled(ContentNode value) =>
        ContentNode.Branch(value.Kind, [.. value.Text.Select(letter => ContentNode.Leaf(BarcodeKinds.Character, letter.ToString()))], value.Role);
}
