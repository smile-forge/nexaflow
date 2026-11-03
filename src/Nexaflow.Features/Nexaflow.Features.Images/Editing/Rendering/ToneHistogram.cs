namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>The colour channels of a picture, as a set: which of them clip at an end of the tones.</summary>
[Flags]
public enum ToneChannels
{
    None  = 0,
    Red   = 1,
    Green = 2,
    Blue  = 4,
    All   = Red | Green | Blue,
}

/// <summary>The ends of the tones, as a set: which of them are marked on the picture where they clip.</summary>
[Flags]
public enum ClipEnds
{
    None       = 0,
    Shadows    = 1,
    Highlights = 2,
}

/// <summary>What the display is asked to read of each frame: its tones at all, and which ends to mark where they clip.</summary>
public readonly record struct ToneAsk(bool Read, ClipEnds Marked = ClipEnds.None);

/// <summary>
/// How a picture as shown spreads over its tones: for each of <see cref="Levels"/> steps from black to white, how many of its
/// pixels lie there in red, green and blue, and in luminance — counted in the picture's own colour, at its own depth — and how
/// many lie at or beyond either end, where the picture keeps no detail.
/// </summary>
/// <remarks>
/// Levels are read on the sRGB curve, as a person reads tones, whatever curve the picture keeps them on. A pixel clips in a
/// channel when that channel is at or below black, or at or beyond white; an end counts as clipping in a channel once
/// <see cref="ClipShare"/> of the pixels clip there, so a stray pixel or two does not light the warning. Transparent pixels are
/// not counted: they are no part of what is seen.
/// </remarks>
public sealed class ToneHistogram
{
    /// <summary>The steps from black to white.</summary>
    public const int Levels = 256;

    /// <summary>The share of the pixels that must clip at an end, in a channel, for that end to count as clipping in it.</summary>
    public const double ClipShare = 1.0 / 5000;

    // Red, green, blue and luminance, Levels each.
    private readonly int[] _levels;
    // Pixels at black and at white, by channel: red, green, blue.
    private readonly long[] _atBlack;
    private readonly long[] _atWhite;

    internal ToneHistogram(int[] levels, long pixels, long[] atBlack, long[] atWhite, long shadowPixels, long highlightPixels)
    {
        _levels = levels;
        _atBlack = atBlack;
        _atWhite = atWhite;
        Pixels = pixels;
        ShadowPixels = shadowPixels;
        HighlightPixels = highlightPixels;
    }

    /// <summary>The pixels counted: every one not fully transparent.</summary>
    public long Pixels { get; }

    /// <summary>The pixels with at least one channel at or below black.</summary>
    public long ShadowPixels { get; }

    /// <summary>The pixels with at least one channel at or beyond white.</summary>
    public long HighlightPixels { get; }

    public ReadOnlySpan<int> Red => _levels.AsSpan(0, Levels);

    public ReadOnlySpan<int> Green => _levels.AsSpan(Levels, Levels);

    public ReadOnlySpan<int> Blue => _levels.AsSpan(2 * Levels, Levels);

    /// <summary>Luminance, weighed from red, green and blue as Rec. 709 weighs them.</summary>
    public ReadOnlySpan<int> Luminance => _levels.AsSpan(3 * Levels, Levels);

    /// <summary>The channels whose shadows clip.</summary>
    public ToneChannels ShadowsClipped => Clipping(_atBlack);

    /// <summary>The channels whose highlights clip.</summary>
    public ToneChannels HighlightsClipped => Clipping(_atWhite);

    private ToneChannels Clipping(long[] atEnd)
    {
        if (Pixels == 0) return ToneChannels.None;
        long least = Math.Max(1, (long)Math.Ceiling(Pixels * ClipShare));
        var clipped = ToneChannels.None;
        if (atEnd[0] >= least) clipped |= ToneChannels.Red;
        if (atEnd[1] >= least) clipped |= ToneChannels.Green;
        if (atEnd[2] >= least) clipped |= ToneChannels.Blue;
        return clipped;
    }
}
