using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Matrix.DataMatrix.Stages;

/// <summary>
/// Reads a <c>datamatrix</c> block's fields (<see cref="DataMatrixBlockReader"/>) and encodes what they say
/// (<see cref="DataMatrixEncoder"/>), leaving the block as the symbol it is drawn as (<see cref="MatrixSymbolNode"/>): the border
/// every data region wears — the solid L a scanner finds it by, and the alternating clock track opposite that gives the module
/// pitch — with the data inside as its modules. A large symbol is several regions side by side and one above another, each with
/// a border of its own, so the borders between regions are finder and clock too. A block that will not read or will not encode
/// is left with the part at fault saying why.
/// </summary>
public sealed class EncodeDataMatrix : IAstStage
{
    /// <summary>The solid left column and bottom row of a region.</summary>
    public const string Finder = "Finder";

    /// <summary>The alternating top row and right column of a region.</summary>
    public const string Clock = "Clock";

    public string Name => "datamatrix:encode";

    public ContentNode Run(ContentNode tree)
    {
        if (!DataMatrixBlockReader.TryRead(tree, out var block, out var wrong)) return MatrixBlockReader.Blamed(tree, wrong);
        if (!DataMatrixEncoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return MatrixBlockReader.Refused(tree, trouble);

        return MatrixSymbolNode.Of(tree, symbol!, block.Settings, 1, Regions(symbol!));
    }

    private static IReadOnlyList<MatrixRegion> Regions(DataMatrixSymbol symbol)
    {
        // A region is its data with a module of border on every side.
        int down = symbol.Size.RegionRows + 2;
        int across = symbol.Size.RegionColumns + 2;

        return
        [
            new(Finder, (x, y) => x % across == 0 || y % down == down - 1),
            new(Clock, (x, y) => y % down == 0 || x % across == across - 1),
        ];
    }
}
