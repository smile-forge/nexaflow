namespace Nexaflow.Media.Jpeg2000;

/// <summary>How <see cref="Jpeg2000Decoder"/> decodes.</summary>
public sealed record Jpeg2000DecodeOptions
{
    /// <summary>Every option at its default.</summary>
    public static Jpeg2000DecodeOptions Default { get; } = new();

    /// <summary>
    /// Whether a palette in a JP2 file (its <c>pclr</c> box) is applied while decoding, so the components are the
    /// colours rather than indices into it. Off when the indices are what the caller wants — a PDF that declares a
    /// colour space of its own reads them through that.
    /// </summary>
    public bool ApplyPalette { get; init; } = true;

    /// <summary>Whether the decode spreads its work over the processors.</summary>
    public bool Parallel { get; init; } = true;
}
