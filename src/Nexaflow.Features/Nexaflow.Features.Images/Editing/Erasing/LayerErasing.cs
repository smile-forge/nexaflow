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
    /// <paramref name="height"/> canvas, 255 where erased — filled as <paramref name="fill"/> says.
    /// </summary>
    /// <exception cref="InvalidOperationException">Picture erasing is asked for and is not here, or could not do it.</exception>
    public static async Task<TiledPixels> EraseAsync(TiledPixels pixels, byte[] mask, int width, int height, EraseFill fill, MarkupPoint? healFrom,
                                                     IPictureErasing? erasing, IProgress<double>? progress, CancellationToken ct)
    {
        var area = new SKRectI(0, 0, width, height);
        var straight = Unpremultiplied(pixels.Read(area));
        var erased = fill switch
        {
            EraseFill.Ai => await ErasedPictures.ByErasingAsync(erasing ?? throw new InvalidOperationException("Picture erasing is not here."),
                                                                 new UprightPixels(straight, width, height, SKColorSpace.CreateSrgb()), mask, progress, ct),
            EraseFill.ContentAware => await Task.Run(() => ContentAwareFill.Fill(straight, width, height, mask, progress: progress, ct: ct), ct),
            EraseFill.Heal => await Task.Run(() => HealingBrush.Heal(straight, width, height, mask,
                                                                     (int)Math.Round(healFrom?.X ?? 0), (int)Math.Round(healFrom?.Y ?? 0), ct), ct),
            _ => throw new InvalidOperationException($"No way to fill by {fill}."),
        };
        return pixels.Written(area, Premultiplied(erased));
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
