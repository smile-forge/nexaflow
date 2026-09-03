namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Which of Aztec's two symbol families to build.
/// </summary>
public enum AztecFormat
{
    /// <summary>Compact while the message fits in one, then full range. What a block wants unless it says otherwise.</summary>
    Auto,

    /// <summary>
    /// The compact family: an eleven-module core, one to four layers, no reference grid. Smaller than a
    /// full symbol of the same capacity, and the whole family tops out at 608 bits.
    /// </summary>
    Compact,

    /// <summary>
    /// The full range: a fifteen-module core, one to thirty-two layers, and a reference grid through
    /// the larger sizes. Bigger for a short message, and the only family that can carry a long one.
    /// </summary>
    Full,
}

/// <summary>
/// What the Aztec encoder must be told beyond the message itself.
/// </summary>
public sealed record AztecOptions
{
    /// <summary>Compact, full range, or whichever is smaller.</summary>
    public AztecFormat Format { get; init; } = AztecFormat.Auto;

    /// <summary>
    /// A forced layer count, or null to take the smallest that fits. Forcing one is how a symbol is made
    /// to a fixed size — a printed form with a box to fill — and it fails rather than silently growing
    /// when the message will not fit.
    /// </summary>
    public int? Layers { get; init; }

    /// <summary>
    /// How much of the symbol's capacity must be error correction, as a percentage. The standard advises
    /// twenty-three; a symbol always ends up with more, because whatever capacity the message leaves over
    /// becomes check words rather than padding.
    /// </summary>
    public int ErrorCorrectionPercent { get; init; } = DefaultErrorCorrectionPercent;

    /// <summary>Whether the message is a GS1 element string, flagged with FNC1 as its first code.</summary>
    public bool Gs1 { get; init; }

    /// <summary>
    /// An ECI number declaring what character set the bytes are in, or null to leave it unsaid. Written
    /// as FLG(n) at the head of the message.
    /// </summary>
    public int? Eci { get; init; }

    public const int DefaultErrorCorrectionPercent = 23;
    public const int MinErrorCorrectionPercent = 0;

    // Past ninety-five per cent a symbol has no room left for a message worth reading, and the encoder
    // would answer every payload with the largest symbol there is.
    public const int MaxErrorCorrectionPercent = 95;

    /// <summary>Three check words on top of the percentage, which is the floor the standard sets.</summary>
    public const int MinimumCheckWords = 3;

    public const int MaxCompactLayers = 4;
    public const int MaxFullLayers = 32;

    /// <summary>The largest ECI number FLG(n) can carry: six digits.</summary>
    public const int MaxEci = 999999;

    public static readonly AztecOptions Default = new();
}
