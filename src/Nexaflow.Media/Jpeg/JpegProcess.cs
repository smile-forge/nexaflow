namespace Nexaflow.Media.Jpeg;

/// <summary>The coding process a JPEG's SOF marker names.</summary>
public enum JpegProcess
{
    /// <summary>Baseline sequential DCT (SOF0).</summary>
    Baseline,

    /// <summary>Extended sequential DCT (SOF1, or SOF9 arithmetic coded).</summary>
    ExtendedSequential,

    /// <summary>Progressive DCT (SOF2, or SOF10 arithmetic coded).</summary>
    Progressive,

    /// <summary>Lossless (SOF3, or SOF11 arithmetic coded).</summary>
    Lossless,
}
