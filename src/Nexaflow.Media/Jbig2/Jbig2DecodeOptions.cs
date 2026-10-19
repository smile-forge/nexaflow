namespace Nexaflow.Media.Jbig2;

/// <summary>How <see cref="Jbig2Decoder"/> decodes.</summary>
public sealed record Jbig2DecodeOptions
{
    /// <summary>Every option at its default.</summary>
    public static Jbig2DecodeOptions Default { get; } = new();

    /// <summary>
    /// The most memory the bitmaps, dictionaries and tables of one decode may take, in bytes. Every size comes from the
    /// data, and a damaged header can claim a page or a dictionary of any size; past this the decode is refused rather
    /// than the memory found. The default, 512 MB, is some thirty times what the largest page in a PDF scan takes.
    /// </summary>
    public long MemoryLimit { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// The most work a decode may do, as a multiple of its page's pixels: every pixel it decodes, combines or copies
    /// counts once. A damaged header can claim a grid of millions of cells or a region of millions of symbols in a stream
    /// of a few bytes, and memory alone does not bound decoding them; past this the decode is refused rather than done. The
    /// default, 64, is eight times what the most demanding image in the PDF corpus needs; a page smaller than 1 megapixel may
    /// work as one of 1 megapixel may.
    /// </summary>
    public int WorkLimit { get; init; } = 64;
}
