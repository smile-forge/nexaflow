using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// One parsed <c>qr</c> fenced block: the string to encode, and how it should be drawn.
///
/// <para>
/// The block's <c>type:</c> and its fields are already resolved into <see cref="Payload"/> by
/// <see cref="QrPayload"/> — a Wi-Fi block and a URL block differ only in the text they produce, so
/// nothing downstream of the parser needs to know which one it came from. <see cref="Type"/> survives
/// for diagnostics and the tooltip.
/// </para>
/// <para>
/// The drawing settings are the ones every 2D block shares, in <see cref="MatrixSettings"/>; what is
/// QR's own is the error-correction level.
/// </para>
/// </summary>
public sealed class QrBlock
{
    /// <summary>The block's <c>type:</c>, lower-cased.</summary>
    public required string Type { get; init; }

    /// <summary>The string the symbol encodes — a URL, a <c>WIFI:</c> descriptor, a vCard.</summary>
    public required string Payload { get; init; }

    public QrErrorCorrection ErrorCorrection { get; init; } = QrErrorCorrection.Medium;

    /// <summary>How it is drawn — cell size, quiet zone, colours.</summary>
    public MatrixSettings Settings { get; init; } = MatrixSettings.Default;

    /// <summary>Device-independent pixels per module.</summary>
    public int CellSize => Settings.CellSize;

    /// <summary>Quiet zone in modules. The specification asks for four, and readers rely on it.</summary>
    public int Margin => Settings.Margin;

    public const int DefaultCellSize = MatrixSettings.DefaultCellSize;
    public const int DefaultMargin   = MatrixSettings.DefaultMargin;
    public const int MinCellSize     = MatrixSettings.MinCellSize;
    public const int MaxCellSize     = MatrixSettings.MaxCellSize;
    public const int MaxMargin       = MatrixSettings.MaxMargin;
}
