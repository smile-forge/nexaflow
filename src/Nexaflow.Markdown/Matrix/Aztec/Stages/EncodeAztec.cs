using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Matrix.Aztec.Stages;

/// <summary>
/// Reads an <c>aztec</c> block's fields (<see cref="AztecBlockReader"/>) and encodes what they say (<see cref="AztecEncoder"/>),
/// leaving the block as the symbol it is drawn as (<see cref="MatrixSymbolNode"/>), from the middle out: the bullseye a scanner
/// finds it by, the ring round it that says how big the symbol is, and in a full-range symbol the reference grid that keeps a large
/// one square — with the data layers as its modules. A block that will not read or will not encode is left with the part at fault
/// saying why.
/// </summary>
public sealed class EncodeAztec : IAstStage
{
    /// <summary>The bullseye: nine modules across in a compact symbol, thirteen in a full-range one.</summary>
    public const string Finder = "Finder";

    /// <summary>The ring round the bullseye: its orientation marks, and the mode message giving the layers and data size.</summary>
    public const string ModeMessage = "ModeMessage";

    /// <summary>A full-range symbol's grid: the lines through the middle, and again every sixteen modules out.</summary>
    public const string ReferenceGrid = "ReferenceGrid";

    private const int GridPitch = 16;

    public string Name => "aztec:encode";

    public ContentNode Run(ContentNode tree)
    {
        if (!AztecBlockReader.TryRead(tree, out var block, out var wrong)) return MatrixBlockReader.Blamed(tree, wrong);
        if (!AztecEncoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return MatrixBlockReader.Refused(tree, trouble);

        return MatrixSymbolNode.Of(tree, symbol!, block.Settings, 1, Regions(symbol!));
    }

    private static IReadOnlyList<MatrixRegion> Regions(AztecSymbol symbol)
    {
        int middle = symbol.Size / 2;
        int core = AztecLayout.CoreRadius(symbol.Compact);

        return
        [
            new(Finder, (x, y) => Ring(x, y) < core),
            new(ModeMessage, (x, y) => Ring(x, y) == core),
            new(ReferenceGrid, (x, y) => !symbol.Compact && ((x - middle) % GridPitch == 0 || (y - middle) % GridPitch == 0)),
        ];

        // How many modules out from the middle a module is, square rather than round: the bullseye's rings are squares.
        int Ring(int x, int y) => Math.Max(Math.Abs(x - middle), Math.Abs(y - middle));
    }
}
