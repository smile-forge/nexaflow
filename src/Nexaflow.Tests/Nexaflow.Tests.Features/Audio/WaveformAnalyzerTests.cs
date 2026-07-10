using System.Linq;
using Nexaflow.Features.Audio.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio;

[TestClass]
[CoversNode("audio-waveform")]
public class WaveformAnalyzerTests
{
    [TestMethod]
    public void Analyze_ToneWav_ReturnsNormalisedPeakEnvelope()
    {
        var path = TestSampleData.Path("audio", "tone_stereo.wav");

        var peaks = WaveformAnalyzer.Analyze(path, buckets: 64);

        Assert.AreEqual(64, peaks.Length);
        Assert.IsTrue(peaks.Any(p => p > 0), "A loud tone should produce non-zero peaks.");
        Assert.IsTrue(peaks.All(p => p is >= 0 and <= 1), "Peaks must be normalised to 0..1.");
        Assert.AreEqual(1f, peaks.Max(), 1e-3, "The envelope is normalised by the overall peak.");
    }

    [TestMethod]
    public void Analyze_MissingFile_ReturnsEmpty()
    {
        var peaks = WaveformAnalyzer.Analyze(@"c:\does\not\exist.wav", buckets: 64);
        Assert.AreEqual(0, peaks.Length);
    }
}
