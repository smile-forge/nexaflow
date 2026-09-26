using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Matrix.Qr.Stages;

/// <summary>
/// Reads a <c>qr</c> block's fields (<see cref="QrBlockReader"/>) and encodes what they say (<see cref="QrEncoder"/>), leaving the
/// block as the symbol it is drawn as (<see cref="MatrixSymbolNode"/>): a finder in three of its corners, the timing lines that run
/// between them, and everything else its modules. A block that will not read or will not encode is left with the part at fault
/// saying why.
/// </summary>
public sealed class EncodeQr : IAstStage
{
    /// <summary>A seven-module square in a corner: top left, top right and bottom left.</summary>
    public const string Finder = "Finder";

    /// <summary>The alternating line along row six or column six, between two finders, that gives the module pitch.</summary>
    public const string Timing = "Timing";

    private const int FinderSize = 7;
    private const int TimingLine = 6;

    public string Name => "qr:encode";

    public ContentNode Run(ContentNode tree)
    {
        if (!QrBlockReader.TryRead(tree, out var block, out var wrong)) return MatrixBlockReader.Blamed(tree, wrong);
        if (!QrEncoder.TryEncode(block!.Payload, block.ErrorCorrection, out var matrix, out var trouble)) return MatrixBlockReader.Refused(tree, trouble);

        return MatrixSymbolNode.Of(tree, matrix!, block.Settings, 1, Regions(matrix!));
    }

    private static IReadOnlyList<MatrixRegion> Regions(QrMatrix symbol)
    {
        int far = symbol.Size - FinderSize;

        // A timing line runs between the separators that ring two finders, so it starts a module past one
        // finder's edge and stops a module short of the next.
        return
        [
            new(Finder, (x, y) => x < FinderSize && y < FinderSize),
            new(Finder, (x, y) => x >= far && y < FinderSize),
            new(Finder, (x, y) => x < FinderSize && y >= far),
            new(Timing, (x, y) => y == TimingLine && x > FinderSize && x < far - 1),
            new(Timing, (x, y) => x == TimingLine && y > FinderSize && y < far - 1),
        ];
    }
}
