using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Audio;

[TestClass]
[CoversNode("ai-host")]
public class PcmResamplerTests
{
    private static byte[] Pcm(params short[] samples)
        => samples.SelectMany(s => new[] { (byte)s, (byte)(s >> 8) }).ToArray();

    private static short[] Samples(byte[] pcm)
        => Enumerable.Range(0, pcm.Length / 2).Select(i => (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8))).ToArray();

    [TestMethod]
    public void SixteenToTwentyFourKilohertz_MakesThreeSamplesOfEveryTwo_OnTheLineBetweenThem()
    {
        var output = Samples(new PcmResampler(16_000, 24_000).Convert(Pcm(0, 300, 600, 900, 1200)));

        CollectionAssert.AreEqual(new short[] { 0, 200, 400, 600, 800, 1000 }, output);
    }

    [TestMethod]
    public void AStreamCutAnywhere_ComesOutAsIfConvertedWhole()
    {
        var input = Pcm(Enumerable.Range(0, 400).Select(i => (short)(Math.Sin(i / 7.0) * 8000)).ToArray());
        var whole = new PcmResampler(16_000, 24_000).Convert(input);

        foreach (var cut in new[] { 2, 6, 98, 314, 798 })
        {
            var resampler = new PcmResampler(16_000, 24_000);
            var pieces    = resampler.Convert(input.AsSpan(0, cut)).Concat(resampler.Convert(input.AsSpan(cut))).ToArray();
            CollectionAssert.AreEqual(whole, pieces, $"cut at byte {cut}");
        }
    }
}
