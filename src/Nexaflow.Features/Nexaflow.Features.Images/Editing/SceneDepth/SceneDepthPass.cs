using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// The pass of a render that draws from the picture's scene depth (<see cref="ImageRecipe.AsksSceneDepth"/>): over the
/// picture as the geometry pass drew it, its haze, then its lens's blur — the order air and a lens lay them — each in
/// premultiplied linear light, with the depth carried onto the canvas through the same matrix as the picture. A blur's width
/// is a share of the photo's longer side, so a preview shows the save's blur at the preview's size.
/// </summary>
public static class SceneDepthPass
{
    /// <summary>The long edge a photo is looked at to find its distance's colour.</summary>
    private const int FarColourEdge = 256;

    /// <summary>Where the pass's canvas-sized buffers come from and go back to.</summary>
    private static readonly System.Buffers.ArrayPool<float> Pool = System.Buffers.ArrayPool<float>.Shared;

    /// <summary>
    /// <paramref name="drawn"/> — the geometry pass's picture of a <paramref name="photo"/>-sized upright photo, which
    /// <paramref name="photoToCanvas"/> drew it by — with <paramref name="recipe"/>'s haze and lens blur, into a picture of
    /// <paramref name="into"/>'s format. The caller owns it.
    /// </summary>
    /// <param name="distance">The colour of the photo's distance in linear light (<see cref="FarColour"/>), for a haze with none of its own.</param>
    public static SKImage Draw(SKImage drawn, ImageRecipe recipe, SceneDepthPlane plane, SKMatrix photoToCanvas, SKSizeI photo,
                               (float R, float G, float B) distance, SKImageInfo into)
    {
            int width = drawn.Width, height = drawn.Height, count = width * height;
            var linear = new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
            // The canvas-sized buffers come from a pool and go back to it: a slider drag redraws this pass many times a second.
            var rgba = Pool.Rent(count * 4);
            var nearness = Pool.Rent(count);
            try
            {
                Read(drawn, linear, rgba);
                plane.OnCanvas(photoToCanvas, photo, new SKSizeI(width, height), nearness);
                if (recipe.DepthHaze is { } haze)
                {
                    var distances = Pool.Rent(count);
                    plane.Distances(nearness, distances, count);
                    DepthHaze.Apply(rgba, distances, width, height, (float)haze.Density, (float)haze.Start, haze.Colour is { } colour ? Linear(colour) : distance);
                    Pool.Return(distances);
                }
                if (recipe.LensBlur is { } blur)
                {
                    float focus = plane.FocusAt(blur.FocusX, blur.FocusY, photo);
                    double canvasPerPhoto = 1 / CropGeometry.SourcePixelsPerCanvasPixel(photoToCanvas);
                    float widest = (float)(Math.Clamp(blur.Aperture, 0, 1) * RecipeLensBlur.WidestBlur * Math.Max(photo.Width, photo.Height) * canvasPerPhoto);
                    LensBlur.Apply(rgba, nearness, width, height, focus, (float)Math.Clamp(blur.Range, 0, RecipeLensBlur.WidestRange), widest, blur.Shape);
                }
                return Drawn(rgba, linear, into);
            }
            finally
            {
                Pool.Return(rgba);
                Pool.Return(nearness);
            }
        }

        /// <summary>The pass's linear light — the first values of <paramref name="rgba"/> — drawn into a picture of
        /// <paramref name="into"/>'s format.</summary>
        private static SKImage Drawn(float[] rgba, SKImageInfo linear, SKImageInfo into)
        {
            int width = linear.Width, height = linear.Height;
            using var made = SKImage.FromPixelCopy(linear, MemoryMarshal.AsBytes(rgba.AsSpan(0, width * height * 4)))
                         ?? throw new InvalidOperationException($"No memory for a {width}×{height} picture.");
        var bitmap = new SKBitmap(into.WithSize(width, height));
        if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {width}×{height} picture.");
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            canvas.DrawImage(made, 0, 0, SKSamplingOptions.Default, paint);
        return ImageDecoders.Seal(bitmap);
    }

    /// <summary>
    /// The colour of <paramref name="source"/>'s distance in linear light — its farthest parts' (<see cref="DepthHaze.FarthestColour"/>)
    /// — read from the photo whole, at a small size, so it is the same however the photo is cropped or shown.
    /// </summary>
    public static (float R, float G, float B) FarColour(DecodedImage source, SceneDepthPlane plane)
    {
        var photo = new SKSizeI((int)Math.Round(source.SourceSize.Width), (int)Math.Round(source.SourceSize.Height));
        double scale = Math.Min(1, (double)FarColourEdge / Math.Max(photo.Width, photo.Height));
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(photo.Width * scale)), Math.Max(1, (int)Math.Round(photo.Height * scale)));
        var photoToSmall = SKMatrix.CreateScale((float)size.Width / photo.Width, (float)size.Height / photo.Height);
        var linear = new SKImageInfo(size.Width, size.Height, SKColorType.RgbaF32, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
        using var surface = SKSurface.Create(linear) ?? throw new InvalidOperationException("No memory to read the photo's distance.");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SetMatrix(source.PixelsToSource.PostConcat(photoToSmall));
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            surface.Canvas.DrawImage(source.Pixels, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        using var small = surface.Snapshot();
        var rgba = new float[size.Width * size.Height * 4];
        Read(small, linear, rgba);
        return DepthHaze.FarthestColour(rgba, plane.OnCanvas(photoToSmall, photo, size));
    }

    /// <summary>The picture's pixels as <paramref name="info"/> lays them out, four floats a pixel, into the first values of
    /// <paramref name="values"/>.</summary>
    private static void Read(SKImage image, SKImageInfo info, float[] values)
    {
        var pin = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, pin.AddrOfPinnedObject(), info.Width * 16, 0, 0))
                throw new InvalidOperationException("The picture could not be read for its scene depth.");
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>A 32-bit ARGB colour, as markup's ink is, in linear light.</summary>
    private static (float R, float G, float B) Linear(uint argb)
    {
        static float Channel(uint value)
        {
            float c = value / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return (Channel((argb >> 16) & 0xFF), Channel((argb >> 8) & 0xFF), Channel(argb & 0xFF));
    }
}
