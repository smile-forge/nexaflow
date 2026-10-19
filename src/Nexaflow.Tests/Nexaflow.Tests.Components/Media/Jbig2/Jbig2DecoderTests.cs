using Nexaflow.Media.Jbig2;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// The JBIG2 decoder against SerenityOS's JBIG2 test files (<c>Media/Jbig2/Fixtures</c>): files its own encoder made to
/// exercise each coding path in turn — every generic region template with and without typical prediction and adaptive
/// pixels, MMR, refinement of a region, of the page and of symbols, text regions Huffman and arithmetically coded in
/// every corner and transposed, symbol dictionaries with aggregation, refinement, custom tables and carried contexts,
/// halftone regions and pattern dictionaries, striped pages, random access — every one a coding of the same picture,
/// which each must decode to bit for bit. Every JBIG2 image in the PDF corpus is held to jbig2dec by
/// <c>Jbig2ComparisonSweep</c> as well; these are the cases named, small enough to run on every build.
/// </summary>
[TestClass]
public class Jbig2DecoderTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Media", "Jbig2", "Fixtures");

    public static IEnumerable<object[]> Every =>
        Directory.EnumerateFiles(Folder, "*.jbig2").Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(name => new object[] { name! });

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Folder, name));

    /// <summary>The picture every fixture codes, from SerenityOS's <c>bitmap.bmp</c>: a P4 PBM, 1 for black.</summary>
    private static (int Width, int Height, byte[] Bits) Expected()
    {
        var file = File.ReadAllBytes(Path.Combine(Folder, "expected.pbm"));
        var header = System.Text.Encoding.ASCII.GetString(file, 0, 32).Split((char[])['\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var (width, height) = (int.Parse(header[1]), int.Parse(header[2]));
        return (width, height, file[^(((width + 7) >> 3) * height)..]);
    }

    private static void AssertThePicture(Jbig2Image image)
    {
        var (width, height, bits) = Expected();
        Assert.AreEqual((width, height), (image.Width, image.Height), "the page's size");
        var decoded = image.Bits.Span;
        var differ = 0;
        for (var i = 0; i < bits.Length; i++) differ += System.Numerics.BitOperations.PopCount((uint)(bits[i] ^ decoded[i]));
        Assert.AreEqual(0, differ, "pixels that differ from the picture");
    }

    [TestMethod]
    [DynamicData(nameof(Every))]
    [CoversNode("media-jbig2-generic")]
    [CoversNode("media-jbig2-refinement")]
    [CoversNode("media-jbig2-text")]
    [CoversNode("media-jbig2-halftone")]
    [CoversNode("media-jbig2-segments")]
    public void AFile_DecodesToThePictureItCodes(string name)
    {
        var image = Jbig2Decoder.Decode(Fixture(name));

        AssertThePicture(image);
        Assert.AreEqual(0, image.Warnings.Count, $"warnings: {string.Join("; ", image.Warnings)}");
    }

    [TestMethod]
    [DataRow("bitmap-symbol-global.jbig2")]
    [DataRow("bitmap-halftone-global.jbig2")]
    [CoversNode("media-jbig2-segments")]
    public void AnEmbeddedStream_DecodesWithTheSegmentsOfItsGlobals(string name)
    {
        var (globals, page) = Jbig2Streams.Split(Fixture(name));
        Assert.AreNotEqual(0, globals.Length, "the file has segments of no page");

        AssertThePicture(Jbig2Decoder.Decode(page, Jbig2Globals.Read(globals)));
    }

    [TestMethod]
    [CoversNode("media-jbig2-segments")]
    public void AStreamWithoutTheGlobalsItRefersTo_LeavesOutWhatNeedsThem_AndSaysSo()
    {
        var (_, page) = Jbig2Streams.Split(Fixture("bitmap-symbol-global.jbig2"));

        var image = Jbig2Decoder.Decode(page);

        Assert.IsTrue(image.Warnings.Any(w => w.Contains("not there", StringComparison.Ordinal)), $"warnings: {string.Join("; ", image.Warnings)}");
    }

    [TestMethod]
    [CoversNode("media-jbig2-segments")]
    public void OneGlobals_ServesManyDecodesAtOnce()
    {
        var (globals, page) = Jbig2Streams.Split(Fixture("bitmap-symbol-global.jbig2"));
        var shared = Jbig2Globals.Read(globals);

        var images = new Jbig2Image[32];
        Parallel.For(0, images.Length, i => images[i] = Jbig2Decoder.Decode(page, shared));

        foreach (var image in images) AssertThePicture(image);
        Assert.IsTrue(shared.DecodedBytes > 0, "the globals' dictionary was decoded");
    }

    [TestMethod]
    [DataRow("bitmap.jbig2")]
    [DataRow("bitmap-mmr.jbig2")]
    [DataRow("bitmap-symbol.jbig2")]
    [DataRow("bitmap-symbol-symhuff-texthuff.jbig2")]
    [DataRow("bitmap-halftone.jbig2")]
    [CoversNode("media-jbig2-damage")]
    public void AFileCutShort_DecodesAsFarAsItGoes_AndSaysSo(string name)
    {
        var whole = Fixture(name);
        foreach (var share in new[] { 0.9, 0.6 })
        {
            var cut = whole[..(int)(whole.Length * share)];
            Jbig2Image image;
            try
            {
                image = Jbig2Decoder.Decode(cut);
            }
            catch (Jbig2Exception)
            {
                // Cut before the page information: there is no page to decode into, and that is the one thing refused.
                continue;
            }

            Assert.IsTrue(image.Warnings.Count > 0, $"{name} cut to {share:P0} decodes without a warning");
        }
    }

    [TestMethod]
    [CoversNode("media-jbig2-damage")]
    public void EveryByteOfAFileFlipped_GivesAPageOrAJbig2Exception()
    {
        foreach (var name in new[] { "bitmap-symbol-symbolrefine-textrefine.jbig2", "bitmap-halftone-10bpp-mmr.jbig2", "bitmap-symbol-symhuffrefine-textrefine.jbig2" })
        {
            var whole = Fixture(name);
            for (var at = 0; at < whole.Length; at++)
            {
                var damaged = (byte[])whole.Clone();
                damaged[at] ^= 0x5A;
                try
                {
                    Jbig2Decoder.Decode(damaged, options: new Jbig2DecodeOptions { MemoryLimit = 64L * 1024 * 1024 });
                }
                catch (Jbig2Exception)
                {
                }
            }
        }
    }

    [TestMethod]
    [CoversNode("media-jbig2-damage")]
    public void APageClaimingMoreThanTheMemoryLimit_IsRefusedBeforeItIsAllocated()
    {
        var file = Fixture("bitmap.jbig2");
        var (_, page) = Jbig2Streams.Split(file);

        // The page information segment comes first: its data starts after an eleven-byte header, width then height.
        var huge = (byte[])page.Clone();
        huge[11] = 0x40;
        huge[15] = 0x40;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var refusal = Assert.ThrowsExactly<Jbig2Exception>(() => Jbig2Decoder.Decode(huge));

        StringAssert.Contains(refusal.Message, "MB");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(1), $"refused after {clock.Elapsed}");
    }

    [TestMethod]
    [CoversNode("media-jbig2-damage")]
    public void ALowerMemoryLimit_RefusesAPageThatNeedsMore()
    {
        Assert.ThrowsExactly<Jbig2Exception>(() => Jbig2Decoder.Decode(Fixture("bitmap.jbig2"), options: new Jbig2DecodeOptions { MemoryLimit = 4096 }));
    }

    [TestMethod]
    [CoversNode("media-jbig2-segments")]
    public void DataThatIsNotJbig2_IsRefused()
    {
        Assert.ThrowsExactly<Jbig2Exception>(() => Jbig2Decoder.Decode("%PDF-1.7 not a JBIG2 stream"u8.ToArray()));
        Assert.ThrowsExactly<Jbig2Exception>(() => Jbig2Decoder.Decode(Array.Empty<byte>()));
    }
}
