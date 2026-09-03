using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// Reads Data Matrix symbols made by other generators and checks they decode to the text in their
/// file name — the same external check the barcodes have, and the one that keeps an encoder honest
/// against something that is not itself.
///
/// <para>
/// Point <c>NEXAFLOW_MATRIX_IMAGES</c> at a folder of <c>datamatrix_&lt;text&gt;.png</c> files. The
/// symbol is found as the bounding box of the dark pixels, its module pitch from the alternating edge
/// along its top, and each module sampled at its centre — enough for a clean generator image, which
/// is all this is for. Inconclusive without the folder, so the suite never depends on it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("datamatrix-encoder")]
public class DataMatrixReferenceImageTests
{
    [TestMethod]
    public void ReferenceImagesDecodeToTheirText()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_MATRIX_IMAGES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            Assert.Inconclusive($"set NEXAFLOW_MATRIX_IMAGES to the reference folder (got: {folder ?? "nothing"})");

        var files = Directory.GetFiles(folder, "datamatrix_*.png");
        if (files.Length == 0) Assert.Inconclusive("no datamatrix_*.png in the folder");

        var failures = new List<string>();
        int matched = 0;

        UiThread.Run(() =>
        {
            foreach (var path in files)
            {
                string expected = Path.GetFileNameWithoutExtension(path)["datamatrix_".Length..];
                try
                {
                    var decoded = DataMatrixTestDecoder.Decode(ReadModules(path));
                    if (decoded.Text == expected) matched++;
                    else failures.Add($"{Path.GetFileName(path)}: decoded '{decoded.Text}'");
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        });

        Assert.AreEqual(0, failures.Count, $"matched {matched}:\nunmatched {failures.Count}:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>The symbol's modules, sampled from a picture of it.</summary>
    private static IModuleMatrix ReadModules(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var gray  = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);

        int w = gray.PixelWidth, h = gray.PixelHeight;
        var px = new byte[w * h];
        gray.CopyPixels(px, w, 0);

        bool Dark(int x, int y) => px[y * w + x] < 128;

        // The bounding box of the ink is the symbol: its L runs the full left and bottom edges.
        int left = w, right = -1, top = h, bottom = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (Dark(x, y)) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }

        if (right < 0) throw new InvalidDataException("no dark pixels");

        // The top edge alternates dark and light one module at a time, so its run count is the module
        // count across — from which the pitch follows.
        int columns = CountRuns(x => Dark(x, top + (bottom - top) / 100), left, right);
        int rows    = CountRuns(y => Dark(right - (right - left) / 100, y), top, bottom);

        // A multi-region symbol has interior finder lines that break the alternation; the region
        // count is small enough to recover by trying the sizes the standard defines.
        double pitchX = (right - left + 1.0) / columns, pitchY = (bottom - top + 1.0) / rows;

        if (!DataMatrixEncoder.TryGetSize(rows, columns, out _))
            throw new InvalidDataException($"read {rows}×{columns} modules, which is not a Data Matrix size");

        var modules = new bool[columns, rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
            modules[x, y] = Dark((int)(left + (x + 0.5) * pitchX), (int)(top + (y + 0.5) * pitchY));

        return new Sampled(modules);
    }

    private static int CountRuns(Func<int, bool> dark, int from, int to)
    {
        int runs = 0;
        bool? last = null;
        for (int i = from; i <= to; i++)
        {
            bool d = dark(i);
            if (last != d) { runs++; last = d; }
        }
        return runs;
    }

    private sealed class Sampled(bool[,] modules) : IModuleMatrix
    {
        public int Width  => modules.GetLength(0);
        public int Height => modules.GetLength(1);
        public bool this[int x, int y] => modules[x, y];
    }
}
