using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Says of each cell the number it reads as (<see cref="PlotCellNode"/>).
///
/// <para>
/// A cell that reads as no number is a category, and says nothing rather than saying so: a name is what
/// a cell already is, so there would be nothing to record. Which means "does this cell read as a number"
/// is the whole of the question everything after this asks, and a column is continuous exactly when its
/// cells do.
/// </para>
/// <para>
/// Nothing rounds here — what a value is shown as is the axis's business, and a tree that had already rounded
/// would have thrown away what the axis needs.
/// </para>
/// </summary>
public sealed class ResolveValues : IAstStage
{
    public string Name => "plot:values";

    public ContentNode Run(ContentNode tree) =>
        tree is not PlotBlockNode
            ? tree
            : AstRewrite.Each(tree, node =>
                  node.Kind == PlotKinds.Row && !node.IsHeader()
                      ? node.WithCells((cell, _) => Said(cell))
                      : node);

    private static ContentNode Said(ContentNode cell) =>
        SettingValues.Read(cell.Says()) is { } number ? PlotCellNode.Of(cell).Numbered(number) : cell;
}
