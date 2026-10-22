namespace Nexaflow.Media.Jpeg2000;

/// <summary>How <see cref="Jpeg2000Decoder"/> decodes.</summary>
public sealed record Jpeg2000DecodeOptions
{
    /// <summary>Every option at its default.</summary>
    public static Jpeg2000DecodeOptions Default { get; } = new();

    /// <summary>
    /// Whether the colour information in a JP2 file's header is applied: its palette (<c>pclr</c>), the component
    /// mapping through it (<c>cmap</c>) and the channel definitions (<c>cdef</c>). Off when the codestream's own
    /// components are what the caller wants — a PDF that declares a colour space of its own reads them through that —
    /// and then none of those boxes is read, so a damaged one costs nothing; libopenjp2's
    /// <c>OPJ_DPARAMETERS_IGNORE_PCLR_CMAP_CDEF_FLAG</c>.
    /// </summary>
    public bool ApplyPalette { get; init; } = true;

    /// <summary>Whether the decode spreads its work over the processors.</summary>
    public bool Parallel { get; init; } = true;
}
