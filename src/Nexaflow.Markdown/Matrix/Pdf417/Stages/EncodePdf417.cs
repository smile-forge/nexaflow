using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Matrix.Pdf417.Stages;

/// <summary>
/// Reads a <c>pdf417</c> block's fields (<see cref="Pdf417BlockReader"/>) and encodes what they say (<see cref="Pdf417Encoder"/>),
/// leaving the block as the symbol it is drawn as (<see cref="MatrixSymbolNode"/>): the columns every row of it is made of — the
/// start pattern, the left row indicator, the data codewords, the right row indicator and the stop pattern. A truncated symbol has
/// no right indicator and a stop one module wide. The one code whose rows are drawn taller than its modules are wide, by the
/// block's <c>rowHeight:</c>. A block that will not read or will not encode is left with the part at fault saying why.
/// </summary>
public sealed class EncodePdf417 : IAstStage
{
    public const string Start = "Start";

    /// <summary>The codeword at the left of every row that says which row it is, and how many there are.</summary>
    public const string LeftRowIndicator = "LeftRowIndicator";

    public const string Codewords = "Codewords";

    public const string RightRowIndicator = "RightRowIndicator";

    public const string Stop = "Stop";

    /// <summary>How many modules wide one codeword is — and the start pattern, and each row indicator.</summary>
    private const int Codeword = 17;

    public string Name => "pdf417:encode";

    public ContentNode Run(ContentNode tree)
    {
        if (!Pdf417BlockReader.TryRead(tree, out var block, out var wrong)) return MatrixBlockReader.Blamed(tree, wrong);
        if (!Pdf417Encoder.TryEncode(block!.Payload, block.Options, out var symbol, out var trouble)) return MatrixBlockReader.Refused(tree, trouble);

        return MatrixSymbolNode.Of(tree, symbol!, block.Settings, block.RowHeight, Regions(symbol!));
    }

    private static IReadOnlyList<MatrixRegion> Regions(Pdf417Symbol symbol)
    {
        int data = Codeword * 2;
        int right = data + Codeword * symbol.Columns;

        return
        [
            new(Start, (x, _) => x < Codeword),
            new(LeftRowIndicator, (x, _) => x < data),
            new(Codewords, (x, _) => x < right),
            new(RightRowIndicator, (x, _) => !symbol.Truncated && x < right + Codeword),
            new(Stop, (_, _) => true),
        ];
    }
}
