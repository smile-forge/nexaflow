using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

/// <summary>
/// A rectangle in the pixels Win32 reports to this process, its edges as a <c>RECT</c> holds them (right and
/// bottom exclusive). Window placement is kept in these units so it goes back through <c>SetWindowPlacement</c>
/// exactly as <c>GetWindowPlacement</c> gave it.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore] public int Width  => Right - Left;
    [JsonIgnore] public int Height => Bottom - Top;
    [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    /// <summary>The area, in square pixels, this rectangle shares with <paramref name="other"/>; 0 when they are apart.</summary>
    public long OverlapArea(PixelRect other)
    {
        long width  = Math.Min(Right, other.Right)   - Math.Max(Left, other.Left);
        long height = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
