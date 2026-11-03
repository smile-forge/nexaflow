using Nexaflow.Features.Images.Editing.Rendering;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Wpf;

/// <summary>
/// Where the frame on screen clips, as two masks the canvas tints over the picture — its shadows' and its highlights' —
/// written on the UI thread from what a <see cref="ToneReader"/> marked, into bitmaps replaced only when the frame's size
/// changes. Both read the same byte a pixel, each through its own palette: opaque where its end's mark is set, clear elsewhere.
/// The masks carry no colour of their own; the canvas fills them with the theme's.
/// </summary>
public sealed class ClipMaskBitmaps
{
    private BitmapPalette? _shadowPalette;
    private BitmapPalette? _highlightPalette;

    /// <summary>Opaque where the shadows clip.</summary>
    public WriteableBitmap? Shadows { get; private set; }

    /// <summary>Opaque where the highlights clip.</summary>
    public WriteableBitmap? Highlights { get; private set; }

    /// <summary>Writes the ends <paramref name="clipped"/> marked; true when the bitmaps are new ones.</summary>
    public bool Write(ClippedPixels clipped)
    {
        bool replaced = Shadows is null || Shadows.PixelWidth != clipped.Width || Shadows.PixelHeight != clipped.Height;
        if (replaced)
        {
            _shadowPalette ??= MaskPalette(ClippedPixels.Shadow);
            _highlightPalette ??= MaskPalette(ClippedPixels.Highlight);
            Shadows = new WriteableBitmap(clipped.Width, clipped.Height, 96, 96, PixelFormats.Indexed8, _shadowPalette);
            Highlights = new WriteableBitmap(clipped.Width, clipped.Height, 96, 96, PixelFormats.Indexed8, _highlightPalette);
        }
        var area = new Int32Rect(0, 0, clipped.Width, clipped.Height);
        if (clipped.Ends.HasFlag(ClipEnds.Shadows)) Shadows!.WritePixels(area, clipped.Marks, clipped.Width, 0);
        if (clipped.Ends.HasFlag(ClipEnds.Highlights)) Highlights!.WritePixels(area, clipped.Marks, clipped.Width, 0);
        return replaced;
    }

    /// <summary>A palette indexed by a pixel's marks: opaque at every index with <paramref name="mark"/> set.</summary>
    private static BitmapPalette MaskPalette(byte mark)
    {
        var entries = new Color[4];
        for (int marks = 0; marks < entries.Length; marks++)
            entries[marks] = Color.FromArgb((marks & mark) != 0 ? (byte)255 : (byte)0, 0, 0, 0);
        return new BitmapPalette(entries);
    }
}
