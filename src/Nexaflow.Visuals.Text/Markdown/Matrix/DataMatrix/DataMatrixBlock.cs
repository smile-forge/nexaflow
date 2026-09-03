namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// One parsed <c>datamatrix</c> fenced block: the string to encode, what the encoder must be told
/// about it, and how it should be drawn.
/// </summary>
public sealed class DataMatrixBlock
{
    /// <summary>The block's <c>type:</c>, lower-cased.</summary>
    public required string Type { get; init; }

    /// <summary>The string the symbol encodes.</summary>
    public required string Payload { get; init; }

    /// <summary>Shape, forced size, GS1 and Macro — some from the block's settings, some decided by its type.</summary>
    public DataMatrixOptions Options { get; init; } = DataMatrixOptions.Default;

    /// <summary>How it is drawn — cell size, quiet zone, colours.</summary>
    public MatrixSettings Settings { get; init; } = MatrixSettings.Default;
}
