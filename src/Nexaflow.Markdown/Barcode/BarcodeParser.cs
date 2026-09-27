using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// Reads the body of a <c>barcode</c> block into a tree. It is written as every code block is — a <c>key: value</c> field a
/// line, which <see cref="MatrixParser"/> reads — and its value is spelled out a character at a time, because a barcode prints
/// its value back a character at a time and each character it prints has to be able to say which one of the value it is.
/// </summary>
public static class BarcodeParser
{
    public static ContentNode Parse(string? source) => AstRewrite.Each(MatrixParser.Parse(source, "barcode"), node =>
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