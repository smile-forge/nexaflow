namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// A colour written in a <c>qr</c> block's <c>dark:</c> or <c>light:</c> setting.
///
/// <para>
/// Kept as plain bytes rather than a WPF <c>Color</c> so the block model, and everything that parses
/// into it, stays free of a UI thread ΓÇö the renderer is where this becomes a brush.
/// </para>
/// </summary>
public readonly record struct QrColor(byte A, byte R, byte G, byte B);

/// <summary>
/// One parsed <c>qr</c> fenced block: the string to encode, and how it should be drawn.
///
/// <para>
/// The block's <c>type:</c> and its fields are already resolved into <see cref="Payload"/> by
/// <see cref="QrPayload"/> ΓÇö a Wi-Fi block and a URL block differ only in the text they produce, so
/// nothing downstream of the parser needs to know which one it came from. <see cref="Type"/> survives
/// for diagnostics and the tooltip.
/// </para>
/// </summary>
public sealed class QrBlock
{
    /// <summary>The block's <c>type:</c>, lower-cased.</summary>
    public required string Type { get; init; }

    /// <summary>The string the symbol encodes ΓÇö a URL, a <c>WIFI:</c> descriptor, a vCard.</summary>
    public required string Payload { get; init; }

    public QrErrorCorrection ErrorCorrection { get; init; } = QrErrorCorrection.Medium;

    /// <summary>Device-independent pixels per module.</summary>
    public int CellSize { get; init; } = DefaultCellSize;

    /// <summary>Quiet zone in modules. The specification asks for four, and readers rely on it.</summary>
    public int Margin { get; init; } = DefaultMargin;

    /// <summary>Dark-module colour, or null to take the palette's.</summary>
    public QrColor? Dark { get; init; }

    /// <summary>Background colour, or null to take the palette's.</summary>
    public QrColor? Light { get; init; }

    public const int DefaultCellSize = 4;
    public const int DefaultMargin   = 4;

    // A module below 1px cannot be drawn, and above 64 one code would fill any page. The margin is
    // capped for the same reason; zero is allowed because a code inside a framed card supplies its own.
    public const int MinCellSize = 1;
    public const int MaxCellSize = 64;
    public const int MaxMargin   = 32;
}
