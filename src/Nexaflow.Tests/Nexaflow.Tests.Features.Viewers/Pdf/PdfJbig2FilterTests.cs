using System.IO;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Media.Jbig2;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// <c>JBIG2Decode</c> as a PDF reads it: the page decoded with the <c>JBIG2Globals</c> its decode parameters name,
/// JBIG2's 1 for black inverted to the 0 a one-bit image reads as black, and the page laid out at the size the image
/// dictionary gives. The decoder itself is held to its own fixtures in <c>Jbig2DecoderTests</c>.
/// </summary>
[TestClass]
[CoversNode("pdf-image-filters")]
public class PdfJbig2FilterTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestSampleData.RepoRoot,
        "src", "Nexaflow.Tests", "Nexaflow.Tests.Components", "Media", "Jbig2", "Fixtures", name));

    private static DictionaryToken ImageDictionary(int width, int height, byte[]? globals)
    {
        var entries = new Dictionary<NameToken, IToken>
        {
            [NameToken.Filter] = NameToken.Jbig2Decode,
            [NameToken.Width] = new NumericToken(width),
            [NameToken.Height] = new NumericToken(height),
        };
        if (globals != null)
        {
            entries[NameToken.DecodeParms] = new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                [NameToken.Jbig2Globals] = new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>()), globals),
            });
        }
        return new DictionaryToken(entries);
    }

    [TestMethod]
    public void AStream_DecodesWithTheGlobalsItsParametersName_BlackAs0()
    {
        var (globals, page) = Jbig2Streams.Split(Fixture("bitmap-symbol-global.jbig2"));
        var expected = Jbig2Decoder.Decode(Fixture("bitmap.jbig2"));

        var samples = new PdfJbig2Filter().Decode(page, ImageDictionary(expected.Width, expected.Height, globals),
            DefaultFilterProvider.Instance, 0).ToArray();

        var bits = expected.Bits.ToArray();
        Assert.AreEqual(bits.Length, samples.Length);
        for (var i = 0; i < bits.Length; i++)
        {
            if ((byte)~bits[i] != samples[i]) Assert.Fail($"byte {i} is {samples[i]:X2}, not the inverse of {bits[i]:X2}");
        }
    }

    [TestMethod]
    public void APageWiderAndTallerThanTheDictionarySays_IsCropped()
    {
        // A 12 x 2 page: the first row all black, the second black only in its last four pixels.
        var image = new Jbig2Image(12, 2, [0xFF, 0xF0, 0x00, 0xF0], []);

        var samples = PdfJbig2Filter.Samples(image, 10, 1);

        // 10 pixels of the first row, black as 0; the rest of the byte white.
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x3F }, samples);
    }

    [TestMethod]
    public void APageNarrowerAndShorterThanTheDictionarySays_IsPaddedWhite()
    {
        var image = new Jbig2Image(4, 1, [0xF0], []);

        var samples = PdfJbig2Filter.Samples(image, 12, 2);

        CollectionAssert.AreEqual(new byte[] { 0x0F, 0xFF, 0xFF, 0xFF }, samples);
    }

    [TestMethod]
    public void TheSameGlobals_AreReadOnce()
    {
        var (globals, _) = Jbig2Streams.Split(Fixture("bitmap-symbol-global.jbig2"));
        var cache = new Jbig2GlobalsCache();

        var first = cache.Get(globals);
        var again = cache.Get((byte[])globals.Clone());

        Assert.AreSame(first, again);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public void TheGlobalsKept_AreHeldToTheCachesCapacity()
    {
        var (globals, _) = Jbig2Streams.Split(Fixture("bitmap-symbol-global.jbig2"));
        var cache = new Jbig2GlobalsCache();

        // Each a different stream: the same segments, then a byte that follows them.
        for (var i = 0; i <= Jbig2GlobalsCache.Capacity; i++) cache.Get((byte[])[.. globals, (byte)i]);

        Assert.AreEqual(Jbig2GlobalsCache.Capacity, cache.Count);
    }
}
