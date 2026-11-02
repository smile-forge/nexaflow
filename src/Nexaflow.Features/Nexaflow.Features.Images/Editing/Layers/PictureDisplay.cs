using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// A layered picture's composite made ready to be shown — never to be saved: its values read through the ICC profile its
/// file named, and a 32-bit picture's light scaled by a view exposure, as Photoshop previews one, since light past white
/// would otherwise all show as white.
/// </summary>
public static class PictureDisplay
{
    private static readonly ConditionalWeakTable<byte[], SKColorSpace> Spaces = new();

    /// <summary>
    /// The space <paramref name="picture"/>'s values are in, as shown: its profile's where it names one Skia reads — linear
    /// for 32 bits — else null, the composite's own.
    /// </summary>
    public static SKColorSpace? ShownSpace(LayeredPicture picture)
    {
        if (picture.Profile.IsEmpty) return null;
        var bytes = ImmutableCollectionsMarshal.AsArray(picture.Profile)!;
        var space = Spaces.GetValue(bytes, b => SKColorSpace.CreateIcc(b) ?? SKColorSpace.CreateSrgb());
        if (space.IsSrgb) return null;
        return picture.Colour.IsLinear ? space.ToLinearGamma() : space;
    }

    /// <summary>
    /// <paramref name="composite"/> — <paramref name="picture"/> composited — as it is shown with <paramref name="exposure"/>
    /// stops of view exposure, handed to <paramref name="consume"/> while it is valid. Nothing is copied where nothing is to
    /// be done.
    /// </summary>
    public static void Show(SKImage composite, LayeredPicture picture, double exposure, Action<SKImage> consume)
    {
        var shownSpace = ShownSpace(picture);
        bool exposed = picture.Colour.IsLinear && exposure != 0;
        if (shownSpace is null && !exposed)
        {
            consume(composite);
            return;
        }
        using var pixmap = composite.PeekPixels()
                           ?? throw new InvalidOperationException("The composite could not be read to be shown.");
        using var tagged = shownSpace is null ? null : SKImage.FromPixels(pixmap.WithColorSpace(shownSpace));
        var shown = tagged ?? composite;
        if (!exposed)
        {
            consume(shown);
            return;
        }
        // Exposure scales light, so it is applied where the values are linear: on a linear surface, through a colour matrix.
        var info = new SKImageInfo(shown.Width, shown.Height, SKColorType.RgbaF16, SKAlphaType.Premul, shownSpace ?? picture.Colour.ColourSpace);
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("No memory to show the picture.");
        float gain = (float)Math.Pow(2, exposure);
        using var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Src,
            ColorFilter = SKColorFilter.CreateColorMatrix([gain, 0, 0, 0, 0, 0, gain, 0, 0, 0, 0, 0, gain, 0, 0, 0, 0, 0, 1, 0]),
        };
        surface.Canvas.DrawImage(shown, 0, 0, paint);
        using var exposedImage = surface.Snapshot();
        consume(exposedImage);
    }
}
