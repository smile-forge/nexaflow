namespace Nexaflow.Media.Jbig2;

/// <summary>A decoded JBIG2 page: one bit a pixel, 1 for black as JBIG2 codes it, row after row.</summary>
public sealed class Jbig2Image
{
    internal Jbig2Image(int width, int height, byte[] bits, IReadOnlyList<string> warnings)
    {
        Width = width;
        Height = height;
        Bits = bits;
        Warnings = warnings;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The bytes a row takes: the width in bits, rounded up to a whole byte.</summary>
    public int Stride => (Width + 7) >> 3;

    /// <summary>The pixels, most significant bit first, <see cref="Stride"/> bytes a row; 1 is black. The bits past the width in a row's last byte are 0.</summary>
    public Memory<byte> Bits { get; }

    /// <summary>
    /// What was wrong with the data and decoded past: a segment cut short and decoded as far as it goes, or one left out
    /// of the page because it could not be decoded at all. Empty for a sound stream.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}
