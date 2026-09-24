using System.Globalization;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Says how many columns of a table a row of dots stands across, and how far apart the dots are: what
/// <c>\hdotsfor[spacing]{n}</c> amounts to, hung under the command as a <see cref="TexDots"/>.
///
/// <para>
/// A stage rather than something the builder works out, because it is what the writing says rather than how it is
/// drawn: the parser finds the command and its arguments, and the numbers in them are read here, once, so whatever sets
/// the table is told how many squares the cell covers and never looks at the characters to find out. A count that is
/// not a whole number of at least one says nothing, and the command is shown as it was written.
/// </para>
/// </summary>
public sealed class SpanColumns : IAstStage
{
    public string Name => "latex:spans";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Spanned);

    private static ContentNode Spanned(ContentNode node)
    {
        if (node.Kind != TexKinds.Command || node.Part(Roles.Name)?.Text != @"\hdotsfor") return node;
        if (node.Part(Roles.Derived) is not null) return node;

        if (node.Part(TexRole.Argument) is not { } count
            || !int.TryParse(Inside(count), NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns)
            || columns < 1) return node;

        var spacing = 1.0;

        if (node.Part(TexRole.Option) is { } option
            && (!double.TryParse(Inside(option), NumberStyles.Float, CultureInfo.InvariantCulture, out spacing) || spacing < 0))
            return node;

        return node.Holding(TexKinds.Dots, Roles.Derived, new TexDots(columns, spacing));
    }

    /// <summary>What a braced or bracketed argument holds, without the marks round it.</summary>
    private static string Inside(ContentNode argument) =>
        argument.IsLeaf
            ? argument.Text
            : string.Concat(argument.Children.Where(child => child.Role is not (Roles.Open or Roles.Close)).Select(child => child.Print())).Trim();
}
