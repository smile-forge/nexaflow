namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// How a matrix symbol is drawn — the settings every 2D block shares, whatever it encodes.
///
/// <para>
/// A block's own symbology adds what only it has (a QR code's error-correction level, an Aztec's layer
/// count); these four are the ones that mean the same thing on all of them, and the shared reader
/// parses them so no block has to.
/// </para>
/// </summary>
public sealed record MatrixSettings
{
    /// <summary>Device-independent pixels per module.</summary>
    public int CellSize { get; init; } = DefaultCellSize;

    /// <summary>Quiet zone in modules. QR asks for four; Data Matrix and Aztec are content with one, but a reader is never hurt by more.</summary>
    public int Margin { get; init; } = DefaultMargin;

    /// <summary>Dark-module colour, or null to take the palette's.</summary>
    public HexColor? Dark { get; init; }

    /// <summary>Background colour, or null to take the palette's.</summary>
    public HexColor? Light { get; init; }

    public const int DefaultCellSize = 4;
    public const int DefaultMargin   = 4;

    // A module below 1px cannot be drawn, and above 64 one code would fill any page. The margin is
    // capped for the same reason; zero is allowed because a code inside a framed card supplies its own.
    public const int MinCellSize = 1;
    public const int MaxCellSize = 64;
    public const int MaxMargin   = 32;

    public static readonly MatrixSettings Default = new();
}
