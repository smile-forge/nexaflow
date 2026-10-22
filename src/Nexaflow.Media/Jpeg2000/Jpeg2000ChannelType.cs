namespace Nexaflow.Media.Jpeg2000;

/// <summary>
/// What a component holds, as a JP2 file's channel definition box says (ISO/IEC 15444-1, I.5.3.6). A bare codestream, or
/// a file decoded without its colour boxes applied, says nothing of its components, and every one reads as colour.
/// </summary>
public enum Jpeg2000ChannelType
{
    /// <summary>A colour channel.</summary>
    Colour = 0,

    /// <summary>Opacity: 0 is transparent, the component's largest value opaque.</summary>
    Opacity = 1,

    /// <summary>Opacity the colour channels have already been multiplied by.</summary>
    PremultipliedOpacity = 2,

    /// <summary>A channel the file gives no meaning.</summary>
    Unspecified = 65535,
}
