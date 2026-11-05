using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// What a rendered frame changed from the frame its source drew just before it: a <see cref="Region"/> of the frame's pixels,
/// or — null — all of it. Whoever shows frames and still holds that earlier one need copy only the region, so a brush stroke
/// moves the brush's reach to the screen, not the whole picture; anyone else shows the frame whole.
/// </summary>
/// <param name="Source">What drew the frame; frames from different sources never build on one another.</param>
/// <param name="Frame">The frame's place in its source's run, counted from one.</param>
public readonly record struct FrameChange(object? Source, long Frame, SKRectI? Region)
{
    /// <summary>A frame owing nothing to any before it: shown whole.</summary>
    public static FrameChange Whole => default;

    /// <summary>The frame came from <paramref name="earlier"/>'s source, the very next it drew: only <see cref="Region"/> is new.</summary>
    public bool Continues(FrameChange earlier) => Region is not null && Source is not null && ReferenceEquals(Source, earlier.Source) && Frame == earlier.Frame + 1;

    /// <summary>The same frame, every pixel of it new — its look changed all over, whatever its layers did.</summary>
    public FrameChange AllOfIt() => this with { Region = null };
}
