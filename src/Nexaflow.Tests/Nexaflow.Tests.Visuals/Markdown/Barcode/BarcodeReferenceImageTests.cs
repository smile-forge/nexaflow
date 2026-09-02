using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The encoders against barcodes drawn by something else.
///
/// <para>
/// This is the only test here that can catch a wrong pattern table. Everything else either round-trips
/// through the same tables — which agree with themselves however wrong they are — or checks a width and
/// a check digit, which a table can get right while still drawing the wrong bars. Comparing against a
/// symbol produced by an unrelated generator is what closes that gap.
/// </para>
///
/// <para>
/// The corpus is a folder of PNGs named <c>barcode_&lt;value&gt;_&lt;format&gt;.png</c>. It is not in the
/// repository — the images are someone else's output, and that is exactly the point — so this is
/// inconclusive without it. Point <c>NEXAFLOW_BARCODE_IMAGES</c> at a folder to run it.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("external reference corpus")]
public class BarcodeReferenceImageTests
{
    [TestMethod]
    public void EncodedBarsMatchTheReferenceImages()
    {
        string? folder = Environment.GetEnvironmentVariable("NEXAFLOW_BARCODE_IMAGES");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            Assert.Inconclusive($"set NEXAFLOW_BARCODE_IMAGES to the reference folder (got: {folder ?? "nothing"})");

        var matched  = new List<string>();
        var skipped  = new List<string>();
        var failures = new List<string>();

        foreach (string path in Directory.GetFiles(folder!, "*.png").OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(path);

            // barcode_<value>_<format>: a value may itself contain underscores, so the format is what
            // follows the last one and the value is everything between the first and the last.
            int first = name.IndexOf('_');
            int last  = name.LastIndexOf('_');
            if (first < 0 || last <= first) { skipped.Add($"{name}: not barcode_<value>_<format>"); continue; }

            string value  = name[(first + 1)..last];
            string format = name[(last + 1)..];

            if (Symbology(format) is not { } symbology)
            {
                skipped.Add($"{name}: {format} is not a format this block offers");
                continue;
            }

            string reference = Trim(ModulesFrom(path));

            if (BarcodeEncoder.TryEncode(symbology, value, out var pattern, out string? error)
                && Trim(pattern!.ToString()) == reference)
            {
                matched.Add(name);
                continue;
            }

            failures.Add($"{name}: {Diagnose(symbology, value, reference, error)}");
        }

        string report =
            $"matched {matched.Count}:\n  {string.Join("\n  ", matched)}\n\n"
          + $"skipped {skipped.Count}:\n  {string.Join("\n  ", skipped)}\n\n"
          + $"unmatched {failures.Count}:\n  {string.Join("\n  ", failures)}\n";

        Console.WriteLine(report);
        if (Environment.GetEnvironmentVariable("NEXAFLOW_BARCODE_REPORT") is { Length: > 0 } reportPath)
            File.WriteAllText(reportPath, report);

        Assert.AreEqual(0, failures.Count,
            $"{failures.Count} of {matched.Count + failures.Count} did not match:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Says what the reference actually drew, rather than only that it differs.
    ///
    /// <para>
    /// Two generators can disagree about a barcode without either being wrong — over which Code 128
    /// subset to use for text that fits more than one, or how much wider a wide bar is — and the
    /// difference only becomes readable when you can say which of those it was. So the value is tried
    /// against every other format too, and whichever reproduces the picture is named.
    /// </para>
    /// </summary>
    private static string Diagnose(BarcodeSymbology asked, string value, string reference, string? error)
    {
        // Refused: nearly always a check digit the reference did not mind. Say whether it recomputed one.
        if (error is not null)
        {
            if (value.Length > 1
                && BarcodeEncoder.TryEncode(asked, value[..^1], out var recomputed, out _)
                && Trim(recomputed!.ToString()) == reference)
                return $"the reference recomputed the check digit instead of using the one in the value, "
                     + $"drawing {recomputed.Text}; we refuse a wrong one — {error}";

            return $"our encoder refused it — {error}";
        }

        foreach (var other in Enum.GetValues<BarcodeSymbology>())
        {
            if (other == asked) continue;
            if (BarcodeEncoder.TryEncode(other, value, out var alternative, out _)
                && Trim(alternative!.ToString()) == reference)
                return $"the reference drew this as {other}, not {asked}";
        }

        BarcodeEncoder.TryEncode(asked, value, out var ours, out _);
        string oursModules = Trim(ours!.ToString());

        string ourStart = oursModules[..Math.Min(70, oursModules.Length)];
        string refStart = reference[..Math.Min(70, reference.Length)];

        return oursModules.Length != reference.Length
            ? $"we drew {oursModules.Length} modules, the reference has {reference.Length}\n      ours {ourStart}\n      ref_ {refStart}"
            : $"same width, differs from module {oursModules.Zip(reference).TakeWhile(p => p.First == p.Second).Count()}\n      ours {ourStart}\n      ref_ {refStart}";
    }

    private static string Trim(string modules) => modules.Trim('0');

    private static BarcodeSymbology? Symbology(string name) =>
        name.ToUpperInvariant().Replace("-", string.Empty) switch
        {
            "CODE128"  => BarcodeSymbology.Code128,
            "CODE128A" => BarcodeSymbology.Code128A,
            "CODE128B" => BarcodeSymbology.Code128B,
            "CODE128C" => BarcodeSymbology.Code128C,
            "EAN13" or "GTIN13" => BarcodeSymbology.Ean13,
            "EAN8"     => BarcodeSymbology.Ean8,
            "EAN5"     => BarcodeSymbology.Ean5,
            "EAN2"     => BarcodeSymbology.Ean2,
            "UPCA"     => BarcodeSymbology.Upc,
            "UPCE"     => BarcodeSymbology.UpcE,
            "CODE39"   => BarcodeSymbology.Code39,
            "ITF"      => BarcodeSymbology.Itf,
            "ITF14"    => BarcodeSymbology.Itf14,
            "MSI"      => BarcodeSymbology.Msi,
            "MSI10"    => BarcodeSymbology.Msi10,
            "MSI11"    => BarcodeSymbology.Msi11,
            "MSI1010"  => BarcodeSymbology.Msi1010,
            "MSI1110"  => BarcodeSymbology.Msi1110,
            "PHARMA" or "PHARMACODE" => BarcodeSymbology.Pharmacode,
            "CODABAR"  => BarcodeSymbology.Codabar,
            _          => null,   // ISBN, ISSN, ISMN, CODE39EXT, PDF417 — beyond what this block offers
        };

    // ── Reading bars out of a picture ──────────────────────────────────────

    /// <summary>
    /// Reads an image back to modules, by reading every row and keeping the reading most of them agree on.
    ///
    /// <para>
    /// Picking one row does not work. The obvious choice — the row crossing the most edges — lands in the
    /// human-readable digits on any image drawn large, because a row of glyphs has far more edges than a
    /// row of bars, and the hairlines inside a glyph then set the module width and the reading comes out
    /// many times too wide. Taking a row near the top fails too: an EAN add-on prints its digits
    /// <em>above</em> its bars.
    /// </para>
    /// <para>
    /// Agreement settles it without any of that guesswork. Every row through the bars reads identically
    /// and there are many of them, while rows through text disagree with one another. So the most common
    /// reading is the barcode, wherever in the picture it sits and whatever else is around it.
    /// </para>
    /// </summary>
    private static string ModulesFrom(string path)
    {
        var decoder = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var gray = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Gray8, null, 0);

        int width = gray.PixelWidth, height = gray.PixelHeight;
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);

        var readings = new Dictionary<string, int>();
        for (int y = 0; y < height; y++)
        {
            string reading = RowModules(pixels, width, y);
            if (reading.Length < 8) continue;   // too little to be a symbol

            readings[reading] = readings.GetValueOrDefault(reading) + 1;
        }

        return readings.Count == 0
            ? string.Empty
            : readings.OrderByDescending(r => r.Value).First().Key;
    }

    /// <summary>
    /// One row's dark and light runs, each divided by the narrowest run on the row.
    ///
    /// <para>
    /// One unit for the whole row, not one per colour. Measuring ink and paper separately looks like it
    /// would absorb the deliberate bar-widening some printed barcodes use, and instead breaks any format
    /// whose spaces are all the same width — pharmacode's gaps are uniform, so their own narrowest is the
    /// gap itself and every one of them reads as a single module.
    /// </para>
    /// </summary>
    private static string RowModules(byte[] pixels, int width, int y)
    {
        int start = 0, end = width - 1;
        while (start < width && !Dark(pixels[y * width + start])) start++;
        while (end > start && !Dark(pixels[y * width + end])) end--;
        if (end <= start) return string.Empty;

        var runs = new List<(bool Ink, int Length)>();
        for (int x = start; x <= end;)
        {
            bool ink = Dark(pixels[y * width + x]);
            int run = 0;
            while (x + run <= end && Dark(pixels[y * width + x + run]) == ink) run++;

            runs.Add((ink, run));
            x += run;
        }

        double unit = runs.Min(r => r.Length);
        var modules = new System.Text.StringBuilder();
        foreach (var (ink, length) in runs)
            modules.Append(new string(ink ? '1' : '0', Math.Max((int)Math.Round(length / unit), 1)));

        return modules.ToString();
    }

    private static bool Dark(byte level) => level < 128;
}
