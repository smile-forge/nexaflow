namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>
/// One parsed <c>pdf417</c> fenced block: the string to encode, what the encoder must be told about
/// it, and how it should be drawn.
/// </summary>
public sealed class Pdf417Block
{
    /// <summary>The block's <c>type:</c>, lower-cased.</summary>
    public required string Type { get; init; }

    /// <summary>The string the symbol encodes.</summary>
    public required string Payload { get; init; }

    /// <summary>Columns, error-correction level and whether the symbol is truncated.</summary>
    public Pdf417Options Options { get; init; } = Pdf417Options.Default;

    /// <summary>How it is drawn — module width, quiet zone, colours.</summary>
    public MatrixSettings Settings { get; init; } = MatrixSettings.Default;

    /// <summary>
    /// How tall a row is drawn, as a multiple of the module width.
    /// <para>
    /// Its own setting because PDF417 is stacked rather than square: a row carries no information in
    /// its height, and the standard asks for at least three module widths so a scanner sweeping across
    /// the symbol stays inside one row. Three is the floor and the usual choice.
    /// </para>
    /// </summary>
    public double RowHeight { get; init; } = DefaultRowHeight;

    public const double DefaultRowHeight = 3;
    public const double MinRowHeight     = 2;
    public const double MaxRowHeight      = 20;
}
