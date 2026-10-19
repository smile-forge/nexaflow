using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.Features.Images.Editing.Erasing;

/// <summary>
/// What one step of erasing erases, as a mask at the photo's full size — 255 where erased, 0 where kept: the thing it
/// picks, as picture selection picks it for a cut-out (kept in <see cref="CuttingOut.CutOutMasks"/>, so a pick is made
/// once whichever tool asked), grown by <see cref="PickGrowth"/> past its edge, then each stroke painted over it in the
/// order it was made — one that takes marking back clearing what it covers.
/// </summary>
public static class EraseMasks
{
    /// <summary>How far past a picked thing's edge it is erased, as a share of the photo's longer side — a picked mask
    /// hugs the thing, and a fill taken from just outside it would carry its fringe back in.</summary>
    public const double PickGrowth = 0.006;

    /// <summary>The fewest pixels a picked thing is grown by.</summary>
    public const int LeastPickGrowth = 3;

    /// <summary>How many pixels a thing picked in a <paramref name="width"/> × <paramref name="height"/> photo is grown by.</summary>
    public static int PickGrowthFor(int width, int height) => Math.Max(LeastPickGrowth, (int)Math.Round(Math.Max(width, height) * PickGrowth));

    /// <exception cref="InvalidOperationException">The step picks a thing and picture selection could not pick it, or the
    /// app has no picture selection.</exception>
    public static async Task<byte[]> MakeAsync(ImageEditingServices services, string photo, RecipeErase step, IPictureSelection? selection,
                                               int width, int height, CancellationToken ct)
    {
        var mask = new byte[width * height];
        if (step.Pick is { } pick)
        {
            if (selection is null) throw new InvalidOperationException("Picture selection is not here to pick what to erase.");
            var kept = await services.CutOuts.MakeAsync(services, photo, pick, selection, null, ct);
            var picked = await Task.Run(() => CuttingOut.CutOutMasks.ReadGrey(kept, width, height), ct);
            Grow(picked, width, height, PickGrowthFor(width, height));
            for (int i = 0; i < mask.Length; i++) mask[i] |= picked[i];
        }
        if (!step.Strokes.IsEmpty) Paint(mask, width, height, step.Strokes);
        return mask;
    }

    /// <summary>Every pixel within <paramref name="radius"/> of a marked one, each way, marked 255 too — a square, by running
    /// counts along the rows and then the columns; everything else 0.</summary>
    public static void Grow(byte[] mask, int width, int height, int radius)
    {
        var across = new byte[mask.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width, count = 0;
            for (int x = -radius; x < width; x++)
            {
                int add = x + radius, drop = x - radius - 1;
                if (add < width && mask[row + add] != 0) count++;
                if (drop >= 0 && mask[row + drop] != 0) count--;
                if (x >= 0) across[row + x] = count > 0 ? (byte)255 : (byte)0;
            }
        }
        for (int x = 0; x < width; x++)
        {
            int count = 0;
            for (int y = -radius; y < height; y++)
            {
                int add = y + radius, drop = y - radius - 1;
                if (add < height && across[add * width + x] != 0) count++;
                if (drop >= 0 && across[drop * width + x] != 0) count--;
                if (y >= 0) mask[y * width + x] = count > 0 ? (byte)255 : (byte)0;
            }
        }
    }

    /// <summary>The strokes painted over <paramref name="mask"/> in order: round, their width across, one that marks
    /// setting what it covers and one that takes marking back clearing it.</summary>
    public static void Paint(byte[] mask, int width, int height, IReadOnlyList<RecipeEraseStroke> strokes)
    {
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(mask, 0, bitmap.GetPixels(), mask.Length);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { IsAntialias = false, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
        {
            foreach (var stroke in strokes)
            {
                if (stroke.Points.IsEmpty) continue;
                paint.Color = stroke.Marks ? SKColors.White : SKColors.Black;
                if (stroke.Points.Length == 1)
                {
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle((float)stroke.Points[0].X, (float)stroke.Points[0].Y, (float)(stroke.Width / 2), paint);
                    continue;
                }
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = (float)stroke.Width;
                using var builder = new SKPathBuilder();
                builder.MoveTo((float)stroke.Points[0].X, (float)stroke.Points[0].Y);
                foreach (var point in stroke.Points.Skip(1)) builder.LineTo((float)point.X, (float)point.Y);
                using var path = builder.Detach();
                canvas.DrawPath(path, paint);
            }
        }
        var painted = bitmap.Bytes;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                mask[y * width + x] = painted[y * bitmap.RowBytes + x] >= 128 ? (byte)255 : (byte)0;
    }
}
