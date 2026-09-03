using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// Reads PDF417 symbols made by other generators and checks they decode to the text in their file
/// name — the external check that keeps the encoder honest against something that is not itself.
///
/// <para>
/// This one matters more than the others in this suite. The symbol-character table cannot be derived
/// and had to be taken from elsewhere; these images are how we know it is the right table, laid out the
/// right way round, and that the row indicators, the field and the compaction all agree with what the
/// rest of the world builds.
/// </para>
/// <para>
/// Point <c>NEXAFLOW_BARCODE_IMAGES</c> at a folder of <c>*_PDF417.png</c> or <c>*_pdf417.png</c>
/// files, named <c>barcode_&lt;text&gt;_PDF417.png</c>. Inconclusive without it, so the suite never
/// depends on it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("pdf417-encoder")]
public class Pdf417ReferenceImageTests
{
    private static readonly string StartBits =
        Convert.ToString(Pdf417Codewords.StartPattern, 2).PadLeft(Pdf417Codewords.ModuleCount, '0');

    private static readonly string StopBits =
        Convert.ToString(Pdf417Codewords.StopPattern, 2).PadLeft(Pdf417Codewords.StopModuleCount, '0');

    [TestMethod]
    public void ReferenceImagesDecodeToTheirText()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_BARCODE_IMAGES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            Assert.Inconclusive($"set NEXAFLOW_BARCODE_IMAGES to the reference folder (got: {folder ?? "nothing"})");

        var files = Directory.GetFiles(folder, "*.png")
                             .Where(f => Path.GetFileNameWithoutExtension(f)
                                             .EndsWith("PDF417", StringComparison.OrdinalIgnoreCase))
                             .ToList();
        if (files.Count == 0) Assert.Inconclusive("no PDF417 reference images in the folder");

        var failures = new List<string>();
        int matched = 0;

        UiThread.Run(() =>
        {
            foreach (var path in files)
            {
                // barcode_<text>_PDF417.png
                string stem = Path.GetFileNameWithoutExtension(path);
                string expected = stem[..stem.LastIndexOf('_')];
                if (expected.StartsWith("barcode_", StringComparison.OrdinalIgnoreCase)) expected = expected["barcode_".Length..];

                try
                {
                    var decoded = Pdf417TestDecoder.Decode(ReadModules(path));
                    if (string.Equals(decoded.Text, expected, StringComparison.Ordinal)) matched++;
                    else failures.Add($"{Path.GetFileName(path)}: decoded '{decoded.Text}', expected '{expected}'");
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        });

        Assert.AreEqual(0, failures.Count,
                        $"matched {matched}:\nunmatched {failures.Count}:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The symbol's modules, sampled from a picture of it.
    /// <para>
    /// The module width comes from the start pattern's leading bar, which is eight modules by
    /// definition — not from the narrowest run, because a generator that anti-aliases its edges leaves
    /// one-pixel slivers that would set the width far too small. The row count is then whatever count
    /// of the form 17k+18 actually samples to the known start and stop patterns, which is a check
    /// rather than a guess.
    /// </para>
    /// </summary>
    private static IModuleMatrix ReadModules(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var gray  = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);

        int w = gray.PixelWidth, h = gray.PixelHeight;
        var px = new byte[w * h];
        gray.CopyPixels(px, w, 0);
        bool Dark(int x, int y) => px[y * w + x] < 128;

        var lines = new List<(int Y, int Left, int Right)>();
        for (int y = 0; y < h; y++)
        {
            int l = -1, r = -1;
            for (int x = 0; x < w; x++) if (Dark(x, y)) { if (l < 0) l = x; r = x; }
            if (l >= 0) lines.Add((y, l, r));
        }

        if (lines.Count == 0) throw new InvalidDataException("no dark pixels");

        // The symbol's rows are the widest ones; a caption underneath is narrower.
        int widest = lines.Max(i => i.Right - i.Left);
        var symbolLines = lines.Where(i => i.Right - i.Left > widest * 0.9).ToList();

        int left  = symbolLines.Min(i => i.Left);
        int right = symbolLines.Max(i => i.Right);
        double width = right - left + 1;

        int modules = -1;
        double pitch = 0;
        for (int k = 4; k <= 33; k++)
        {
            int m = 17 * k + 18;
            double p = width / m;
            // As text, not as an integer: a row is over a hundred modules and would not fit in one.
            string candidate = SampleBits(symbolLines[0].Y, left, p, m, Dark);
            if (candidate.StartsWith(StartBits, StringComparison.Ordinal)
                && candidate.EndsWith(StopBits, StringComparison.Ordinal))
            {
                modules = m; pitch = p; break;
            }
        }

        if (modules < 0) throw new InvalidDataException("no module count sampled to the start and stop patterns");

        // Distinct rows, in order — a row read between two real ones is a sampling artefact and differs
        // from both, so keeping only rows whose characters all land in the cluster the cycle expects
        // drops them without needing to know the row height.
        var rows = new List<string>();
        string? previous = null;
        foreach (var line in symbolLines)
        {
            string bits = SampleBits(line.Y, left, pitch, modules, Dark);
            if (bits == previous) continue;
            previous = bits;

            if (InCluster(bits, modules, Pdf417Codewords.Clusters[rows.Count % 3])) rows.Add(bits);
        }

        return new Sampled(rows, modules);
    }

    /// <summary>Whether every character of the row is a pattern of the cluster the row should be in.</summary>
    private static bool InCluster(string bits, int modules, int cluster)
    {
        for (int at = 17; at + 17 <= modules - Pdf417Codewords.StopModuleCount; at += 17)
        {
            var widths = new List<int>();
            char last = bits[at];
            int run = 0;
            for (int i = at; i < at + 17; i++) { if (bits[i] == last) run++; else { widths.Add(run); last = bits[i]; run = 1; } }
            widths.Add(run);

            if (bits[at] != '1' || widths.Count < 7) return false;
            int b4 = widths.Count > 6 ? widths[6] : 0;
            if ((((widths[0] - widths[2] + widths[4] - b4) % 9) + 9) % 9 != cluster) return false;
        }
        return true;
    }


    private static string SampleBits(int y, int left, double pitch, int modules, Func<int, int, bool> dark)
    {
        var sb = new System.Text.StringBuilder(modules);
        for (int m = 0; m < modules; m++) sb.Append(dark(left + (int)((m + 0.5) * pitch), y) ? '1' : '0');
        return sb.ToString();
    }

    private sealed class Sampled(List<string> rows, int width) : IModuleMatrix
    {
        public int Width  => width;
        public int Height => rows.Count;
        public bool this[int x, int y] => rows[y][x] == '1';
    }
}
