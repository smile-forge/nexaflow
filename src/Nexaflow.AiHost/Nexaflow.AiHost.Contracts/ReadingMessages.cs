namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts reading a page uses, by role.</summary>
public static class ReadingParts
{
    /// <summary>What reads a page into its blocks: Unlimited-OCR, a language model with its projector, asked with
    /// <see cref="Generate"/>.</summary>
    public const string Reader = "reader";

    /// <summary>What finds where a picture holds writing: PP-OCRv5's text detector, on the CPU.</summary>
    public const string TextFinder = "textFinder";
}

/// <summary>
/// Finds where a picture holds writing — PaddleOCR's PP-OCRv5 mobile detector, tens of milliseconds on the CPU, so it can
/// be asked of every picture shown. The picture goes by file, as <see cref="RestorePicture"/>'s does; one
/// <see cref="TextFoundInPicture"/> answers.
/// </summary>
/// <param name="Finder">The <see cref="ReadingParts.TextFinder"/>.</param>
/// <param name="Pixels">The file holding the picture: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit RGBA,
/// row by row, nothing else.</param>
public sealed record FindTextInPicture(long RequestId, ModelSpec Finder, string Pixels, int Width, int Height) : HostRequest(RequestId);

/// <summary>Where the picture holds writing — each region of text found, none for a picture without.</summary>
public sealed record TextFoundInPicture(long RequestId, IReadOnlyList<TextRegion> Regions) : HostReply(RequestId);

/// <summary>A region of text: its box in fractions of the picture's width and height, and the detector's mean
/// probability of text over it.</summary>
public sealed record TextRegion(float X, float Y, float Width, float Height, float Score);
