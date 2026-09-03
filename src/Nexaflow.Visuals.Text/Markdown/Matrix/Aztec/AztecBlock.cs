namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// One parsed <c>aztec</c> fenced block: the string to encode, what the encoder must be told about it,
/// and how it should be drawn.
/// </summary>
public sealed class AztecBlock
{
    /// <summary>The block's <c>type:</c>, lower-cased.</summary>
    public required string Type { get; init; }

    /// <summary>The string the symbol encodes.</summary>
    public required string Payload { get; init; }

    /// <summary>Family, layer count, error-correction level and flags.</summary>
    public AztecOptions Options { get; init; } = AztecOptions.Default;

    /// <summary>How it is drawn — cell size, quiet zone, colours.</summary>
    public MatrixSettings Settings { get; init; } = MatrixSettings.Default;
}
