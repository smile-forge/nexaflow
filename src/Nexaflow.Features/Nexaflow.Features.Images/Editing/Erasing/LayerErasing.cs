using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.IO.Common.Imaging;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Erasing;

/// <summary>
/// Erasing a pixel layer: what is marked — a mask over the canvas — filled from what is round it by picture erasing,
/// content-aware fill or the healing brush, straight into the layer's pixels, as the brush works on pixels. A picture
/// layer's erasing stays a step of its recipe instead.
/// </summary>
public static class LayerErasing
{
    /// <summary>
    /// <paramref name="pixels"/> with what <paramref name="mask"/> marks — a byte a pixel over a <paramref name="width"/> ×
    /// <paramref name="height"/> canvas, 255 where erased — filled as <paramref name="fill"/> says. Content-aware fill and the
    /// healing brush work on the pixels' own values, as deep as they are held; picture erasing's model works on 8 bits, so on a
    /// deeper picture it is handed them as sRGB and what it fills is carried back up, only where it filled.
    /// </summary>
    /// <exception cref="InvalidOperationException">Picture erasing is asked for and is not here, or could not do it.</exception>
    public static async Task<TiledPixels> EraseAsync(TiledPixels pixels, byte[] mask, int width, int height, EraseFill fill, MarkupPoint? healFrom,
                                                     IPictureErasing? erasing, IProgress<double>? progress, CancellationToken ct)
    {
        var area = new SKRectI(0, 0, width, height);
        int fromX = (int)Math.Round(healFrom?.X ?? 0), fromY = (int)Math.Round(healFrom?.Y ?? 0);
        if (pixels.Format == TileFormat.Rgb8)
        {
            var straight = Unpremultiplied(pixels.Read(area));
            var erased = fill switch
            {
                EraseFill.Ai => await ErasedPictures.ByErasingAsync(erasing ?? throw new InvalidOperationException("Picture erasing is not here."),
                                                                     new UprightPixels(straight, width, height, SKColorSpace.CreateSrgb()), mask, progress, ct),
                EraseFill.ContentAware => await Task.Run(() => ContentAwareFill.Fill(straight, width, height, mask, progress: progress, ct: ct), ct),
                EraseFill.Heal => await Task.Run(() => HealingBrush.Heal(straight, width, height, mask, fromX, fromY, ct), ct),
                _ => throw new InvalidOperationException($"No way to fill by {fill}."),
            };
            return pixels.Written(area, Premultiplied(erased));
        }

        var colour = pixels.Format.Colour;
        var values = Straight(pixels.ReadPremul(area));
        var filled = fill switch
        {
            EraseFill.Ai => await ByErasingDeepAsync(erasing ?? throw new InvalidOperationException("Picture erasing is not here."),
                                                     values, colour, width, height, mask, progress, ct),
            EraseFill.ContentAware => await Task.Run(() => ContentAwareFill.Fill(values, width, height, mask, progress: progress, ct: ct), ct),
            EraseFill.Heal => await Task.Run(() => HealingBrush.Heal(values, width, height, mask, fromX, fromY, ct), ct),
            _ => throw new InvalidOperationException($"No way to fill by {fill}."),
        };
        return pixels.WrittenPremul(area, Premultiplied(filled));
    }

    /// <summary>
    /// Picture erasing on a deeper picture: its values — straight, four a pixel — handed to the model as 8-bit sRGB, and only the
    /// pixels the mask marks taken back from what it made, carried up into the picture's values; every other pixel as it was.
    /// </summary>
    private static async Task<float[]> ByErasingDeepAsync(IPictureErasing erasing, float[] values, PictureColour colour, int width, int height,
                                                          byte[] mask, IProgress<double>? progress, CancellationToken ct)
    {
        var eight = new byte[values.Length];
        for (int p = 0; p < values.Length; p += 4)
        {
            var shown = colour.ToSrgb(new SKColorF(values[p], values[p + 1], values[p + 2], values[p + 3]));
            eight[p] = ToByte(shown.Red);
            eight[p + 1] = ToByte(shown.Green);
            eight[p + 2] = ToByte(shown.Blue);
            eight[p + 3] = ToByte(shown.Alpha);
        }
        var erased = await ErasedPictures.ByErasingAsync(erasing, new UprightPixels(eight, width, height, SKColorSpace.CreateSrgb()), mask, progress, ct);
        var result = (float[])values.Clone();
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            int p = i * 4;
            var made = colour.FromSrgb(new SKColorF(erased[p] / 255f, erased[p + 1] / 255f, erased[p + 2] / 255f, 1));
            (result[p], result[p + 1], result[p + 2]) = (made.Red, made.Green, made.Blue);
        }
        return result;
    }

    private static byte ToByte(float unit) => (byte)Math.Clamp((int)MathF.Round(unit * 255), 0, 255);

    /// <summary>Premultiplied values, four a pixel, not premultiplied: colour divided by alpha, none where there is none.</summary>
    private static float[] Straight(float[] premul)
    {
        var straight = new float[premul.Length];
        for (int p = 0; p < premul.Length; p += 4)
        {
            float a = premul[p + 3];
            straight[p + 3] = a;
            if (a <= 0) continue;
            for (int c = 0; c < 3; c++) straight[p + c] = premul[p + c] / a;
        }
        return straight;
    }

    /// <summary>Straight values, four a pixel, premultiplied by their alpha.</summary>
    private static float[] Premultiplied(float[] straight)
    {
        var premul = new float[straight.Length];
        for (int p = 0; p < straight.Length; p += 4)
        {
            float a = straight[p + 3];
            premul[p + 3] = a;
            for (int c = 0; c < 3; c++) premul[p + c] = straight[p + c] * a;
        }
        return premul;
    }

    private static byte[] Unpremultiplied(byte[] premul)
    {
        var straight = new byte[premul.Length];
        for (int p = 0; p < premul.Length; p += 4)
        {
            int a = premul[p + 3];
            straight[p + 3] = (byte)a;
            if (a == 0) continue;
            for (int c = 0; c < 3; c++) straight[p + c] = (byte)Math.Min(255, (premul[p + c] * 255 + a / 2) / a);
        }
        return straight;
    }

    private static byte[] Premultiplied(byte[] straight)
    {
        var premul = new byte[straight.Length];
        for (int p = 0; p < straight.Length; p += 4)
        {
            int a = straight[p + 3];
            premul[p + 3] = (byte)a;
            for (int c = 0; c < 3; c++) premul[p + c] = (byte)((straight[p + c] * a + 127) / 255);
        }
        return premul;
    }
}
