using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Says under each cell the number it reads as.
///
/// <para>
/// A cell that reads as no number is a category, and says nothing rather than saying so: a name is what
/// a cell already is, so there would be nothing to record. Which means "has this cell a number under it"
/// is the whole of the question everything after this asks, and a column is continuous exactly when its
/// cells have one.
/// </para>
/// <para>
/// The number is written out so it reads back as itself, because a fact carries characters. Nothing
/// rounds here — what a value is shown as is the axis's business, and a tree that had already rounded
/// would have thrown away what the axis needs.
/// </para>
/// </summary>
public sealed class ResolveValues : IAstStage
{
    public string Name => "plot:values";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Each(tree, node =>
            node.Kind == PlotKinds.Row && !node.IsHeader()
                ? node.WithCells((cell, _) => Said(cell))
                : node);

    private static ContentNode Said(ContentNode cell) =>
        PlotNumber.Read(cell.Says()) is { } number
            ? cell.Telling((PlotKinds.Fact, PlotRoles.Number, PlotNumber.Written(number)))
            : cell;
}
