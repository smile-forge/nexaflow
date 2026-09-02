namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// How much of a QR code may be destroyed and still read back ΓÇö the <c>ec:</c> setting of a
/// <c>qr</c> block. Higher correction spends capacity on redundancy, so the same payload needs a
/// larger symbol; it is what makes a code survive a logo over its middle or a scuffed print.
/// </summary>
public enum QrErrorCorrection
{
    /// <summary>~7% recoverable. The default's smaller sibling ΓÇö for a screen, where nothing damages it.</summary>
    Low = 0,
    /// <summary>~15% recoverable. The default: what most printed codes use.</summary>
    Medium = 1,
    /// <summary>~25% recoverable.</summary>
    Quartile = 2,
    /// <summary>~30% recoverable. For a code that will be printed small, on a curve, or covered in part.</summary>
    High = 3,
}
