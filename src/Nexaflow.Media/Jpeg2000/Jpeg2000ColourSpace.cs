namespace Nexaflow.Media.Jpeg2000;

/// <summary>
/// The colour space a decoded image's components are in — the JP2 enumerated colour spaces (ISO/IEC 15444-1
/// Table I.10, with the JPX additions), numbered as the standard numbers them.
/// </summary>
public enum Jpeg2000ColourSpace
{
    Unknown = -2,
    Unspecified = -1,
    Bilevel = 0,
    YCbCr1 = 1,
    YCbCr2 = 2,
    YCbCr3 = 4,
    PhotoYcc = 9,
    Cmy = 11,
    Cmyk = 12,
    Ycck = 13,
    CieLab = 14,
    Srgb = 16,
    Grey = 17,
    Sycc = 18,
    Eycc = 24,
}
