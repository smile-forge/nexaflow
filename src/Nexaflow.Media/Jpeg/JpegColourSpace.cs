namespace Nexaflow.Media.Jpeg;

/// <summary>A colour space JPEG samples are coded or delivered in — the members of libjpeg's <c>J_COLOR_SPACE</c> met here.</summary>
public enum JpegColourSpace
{
    /// <summary>Components with no colour meaning known, delivered as they are coded.</summary>
    Unknown,
    Grey,
    Rgb,
    YCbCr,
    Cmyk,
    Ycck,
}
