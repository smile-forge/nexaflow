using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>What the 2D-code builder tests ask of a layout and of the picture it paints, written once.</summary>
internal static class MatrixLayouts
{
    /// <summary>Every piece of <paramref name="kind"/> in the layout.</summary>
    public static Piece[] Of(Laid laid, string kind) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    /// <summary>The light field the symbol is printed on.</summary>
    public static RuleMark Ground(Laid laid) => (RuleMark)Of(laid, MatrixPiece.Symbol).Single().Marks[0];

    /// <summary>The module ink of every part of the symbol, whichever part it belongs to.</summary>
    public static GeometryMark[] Ink(Laid laid) =>
        [.. laid.Root.SelfAndDescendants()
                .Where(piece => piece.Kind is not MatrixPiece.Symbol and not MatrixPiece.Strike and not MatrixPiece.Trouble)
                .SelectMany(piece => piece.Marks.ToArray())
                .OfType<GeometryMark>()];

    /// <summary>How much of the page the module ink covers.</summary>
    public static double InkedArea(Laid laid) => Ink(laid).Sum(mark => mark.Shape.GetArea(0.01, ToleranceType.Absolute));

    /// <summary>
    /// Lays <paramref name="element"/> out at 96 dpi, rasterises it, and samples the middle of each module back into
    /// a matrix — what a scanner does, minus finding the code in a photograph.
    /// </summary>
    public static IModuleMatrix ReadBack(FrameworkElement element, int width, int height, MatrixSettings settings,
                                         double rowHeight = 1)
    {
        element.Margin = default;   // the block spacing is layout, not part of the picture

        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.DesiredSize.Width), (int)Math.Ceiling(element.DesiredSize.Height),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        double cell = settings.CellSize;
        double pitch = cell * rowHeight;
        var modules = new bool[width, height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int px = (int)((settings.Margin + x + 0.5) * cell);
                int py = (int)(settings.Margin * cell + (y + 0.5) * pitch);
                int i = py * stride + px * 4;

                // Pbgra32: B, G, R, A. A dark module is far from the near-white ground, so the midpoint separates
                // them without needing to know either colour.
                modules[x, y] = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3 < 128;
            }

        return new Sampled(modules);
    }

    private sealed class Sampled(bool[,] modules) : IModuleMatrix
    {
        public int Width => modules.GetLength(0);
        public int Height => modules.GetLength(1);
        public bool this[int x, int y] => modules[x, y];
    }
}
