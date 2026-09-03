using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// Reads Aztec symbols made by other generators and checks two things against them: that they decode to
/// the text in their file name, and that our encoder, told the family and size they chose, produces the
/// same symbol module for module.
///
/// <para>
/// The second is the check that matters. Aztec's geometry cannot be validated by a round trip — the
/// encoder and a decoder that share a placement walk agree with each other whether or not the walk is
/// right — so the orientation marks, the mode-message ring, the data spiral's direction and starting
/// corner, the first Reed–Solomon root and the leading pad bits were all settled by comparing against
/// pictures this code did not make. Nothing else in the suite can catch a symbol that is
/// self-consistent and unreadable.
/// </para>
/// <para>
/// Point <c>NEXAFLOW_BARCODE_IMAGES</c> at a folder of <c>barcode_&lt;text&gt;_Aztec.png</c> or
/// <c>.gif</c> files. Inconclusive without it, so the suite never depends on it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("aztec-encoder")]
[CoversNode("aztec-layout")]
public class AztecReferenceImageTests
{
    [TestMethod]
    public void ReferenceImagesDecodeToTheirText()
    {
        Check((path, expected, failures) =>
        {
            var decoded = AztecTestDecoder.Decode(ReadModules(path));
            if (!string.Equals(decoded.Text, expected, StringComparison.Ordinal))
                failures.Add($"{Path.GetFileName(path)}: decoded '{decoded.Text}', expected '{expected}'");
        });
    }

    [TestMethod]
    public void ReferenceImagesMatchOurEncoderModuleForModule()
    {
        Check((path, expected, failures) =>
        {
            var theirs  = ReadModules(path);
            var decoded = AztecTestDecoder.Decode(theirs);

            // Told which family and size they used, the symbol is fully determined by the message —
            // whatever error-correction level they asked for, every codeword the message leaves over
            // becomes a check word. So there is nothing left to agree about but the encoding itself.
            var options = new AztecOptions
            {
                Format = decoded.Compact ? AztecFormat.Compact : AztecFormat.Full,
                Layers = decoded.Layers,
            };

            if (!AztecEncoder.TryEncode(expected, options, out var ours, out string? error))
            {
                failures.Add($"{Path.GetFileName(path)}: {error}");
                return;
            }

            if (ours!.DataCodewords != decoded.DataCodewords)
            {
                // Their high-level encoding of the same text came to a different number of codewords,
                // so the symbols cannot be compared bit for bit. Not a defect here — a generator is
                // free to encode suboptimally — but there is nothing to assert either.
                failures.Add($"{Path.GetFileName(path)}: their encoding is {decoded.DataCodewords} "
                           + $"codewords, ours {ours.DataCodewords} — not comparable");
                return;
            }

            var different = Enumerable.Range(0, ours.Size * ours.Size)
                                      .Where(i => ours[i % ours.Size, i / ours.Size]
                                               != theirs[i % ours.Size, i / ours.Size])
                                      .Select(i => $"({i % ours.Size},{i / ours.Size})")
                                      .Take(8)
                                      .ToList();

            if (different.Count > 0)
                failures.Add($"{Path.GetFileName(path)}: modules differ at {string.Join(" ", different)}");
        });
    }

    private static void Check(Action<string, string, List<string>> inspect)
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_BARCODE_IMAGES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            Assert.Inconclusive($"set NEXAFLOW_BARCODE_IMAGES to the reference folder (got: {folder ?? "nothing"})");

        var files = Directory.GetFiles(folder)
                             .Where(f => Path.GetExtension(f) is ".png" or ".gif" or ".bmp")
                             .Where(f => Path.GetFileNameWithoutExtension(f)
                                             .EndsWith("Aztec", StringComparison.OrdinalIgnoreCase))
                             .ToList();
        if (files.Count == 0) Assert.Inconclusive("no Aztec reference images in the folder");

        var failures = new List<string>();
        int checked_ = 0;

        UiThread.Run(() =>
        {
            foreach (var path in files)
            {
                // barcode_<text>_Aztec.png
                string stem = Path.GetFileNameWithoutExtension(path);
                string expected = stem[..stem.LastIndexOf('_')];
                if (expected.StartsWith("barcode_", StringComparison.OrdinalIgnoreCase))
                    expected = expected["barcode_".Length..];

                try
                {
                    inspect(path, expected, failures);
                    checked_++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        });

        Assert.AreEqual(0, failures.Count,
                        $"read {checked_} of {files.Count}:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The symbol's modules, sampled from a picture of it.
    /// <para>
    /// The module count is not guessed from the pixel size: it is whichever odd count in Aztec's range
    /// samples to a symbol whose core really is alternating rings, which is a check rather than an
    /// estimate and survives a generator that pads or anti-aliases.
    /// </para>
    /// </summary>
    private static IModuleMatrix ReadModules(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var gray  = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);

        int w = gray.PixelWidth, h = gray.PixelHeight;
        int stride = (w + 3) / 4 * 4;
        var px = new byte[stride * h];
        gray.CopyPixels(px, stride, 0);
        bool Dark(int x, int y) => px[y * stride + x] < 128;

        int left = w, right = -1, top = h, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Dark(x, y))
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }

        if (right < 0) throw new InvalidDataException("no dark pixels");

        double width = right - left + 1, height = bottom - top + 1;

        foreach (int size in Candidates())
        {
            if (width / size < 1 || height / size < 1) continue;

            var modules = new bool[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    modules[x, y] = Dark(left + (int)((x + 0.5) * width / size),
                                         top  + (int)((y + 0.5) * height / size));

            var sampled = new Sampled(modules);
            if (HasAztecCore(sampled, size)) return sampled;
        }

        throw new InvalidDataException("no module count sampled to an Aztec core");
    }

    /// <summary>Every size either family can be, largest first so a big symbol is not read as a small one.</summary>
    private static IEnumerable<int> Candidates() =>
        Enumerable.Range(1, AztecOptions.MaxFullLayers).Select(l => AztecLayout.Size(false, l))
                  .Concat(Enumerable.Range(1, AztecOptions.MaxCompactLayers).Select(l => AztecLayout.Size(true, l)))
                  .Distinct()
                  .OrderByDescending(size => size);

    /// <summary>
    /// Whether the middle of the symbol is a bullseye: alternating rings out to radius four, which every
    /// Aztec symbol of either family has and a wrongly sampled grid will not.
    /// </summary>
    private static bool HasAztecCore(IModuleMatrix matrix, int size)
    {
        int centre = size / 2;

        for (int r = 0; r <= 4; r++)
        {
            bool dark = r % 2 == 0;
            for (int d = -r; d <= r; d++)
                if (matrix[centre + d, centre - r] != dark || matrix[centre + d, centre + r] != dark
                 || matrix[centre - r, centre + d] != dark || matrix[centre + r, centre + d] != dark)
                    return false;
        }

        return true;
    }

    private sealed class Sampled(bool[,] modules) : IModuleMatrix
    {
        public int Width  => modules.GetLength(0);
        public int Height => modules.GetLength(1);
        public bool this[int x, int y] => modules[x, y];
    }
}
