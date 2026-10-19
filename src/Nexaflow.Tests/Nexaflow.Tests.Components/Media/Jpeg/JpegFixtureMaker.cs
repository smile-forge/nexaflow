using System.IO;
using System.Text.Json;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// Writes the JPEG fixtures into the source tree: each <see cref="JpegFixture"/>'s source encoded by libjpeg-turbo's
/// compressor and damaged as the fixture says, and what libjpeg-turbo's TurboJPEG library decodes it to, as a hash, in
/// <c>expected.json</c>. Opt-in: <c>NEXAFLOW_TURBOJPEG</c> names a <c>turbojpeg.dll</c> built as
/// <c>tools/bench/Run-DctBench.ps1</c> builds it; without it the test is inconclusive. Run it after changing the
/// fixture list, and commit what it writes.
/// </summary>
[TestClass]
[NoCoverage("writes the JPEG fixtures the decoder's tests read — opt-in, never in CI")]
public class JpegFixtureMaker
{
    [TestMethod]
    public void WriteTheFixtures()
    {
        using var turbo = LibJpegTurbo.FromEnvironment();
        if (turbo is null)
        {
            Assert.Inconclusive($"set {LibJpegTurbo.Variable} to a turbojpeg.dll");
            return;
        }

        var folder = Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.Components", "Media", "Jpeg", "Fixtures");
        Directory.CreateDirectory(folder);
        foreach (var stale in Directory.EnumerateFiles(folder))
            File.Delete(stale);

        var expected = new SortedDictionary<string, JpegExpectation>(StringComparer.Ordinal);
        foreach (var fixture in JpegFixture.All)
        {
            var jpeg = fixture.Damaged(turbo.Encode(fixture.Source(), fixture.Width, fixture.Height, fixture.Encoding));
            File.WriteAllBytes(Path.Combine(folder, fixture.FileName), jpeg);

            var decoded = turbo.Decode(jpeg);
            expected[fixture.Name] = decoded switch
            {
                { Samples: { } samples } => new(JpegFixture.HashOf(decoded.Width, decoded.Height, decoded.Components, samples), decoded.Warned, false),
                // A fault libjpeg finds only once the image is out: the decoder keeps what it wrote, and warns.
                { Written: { } written } => new(JpegFixture.HashOf(decoded.Width, decoded.Height, decoded.Components, written), true, false),
                _ => new(null, false, true),
            };
            Assert.IsTrue(fixture.Damage != JpegDamage.None || expected[fixture.Name] is { Warns: false, Refused: false },
                $"{fixture.Name}: libjpeg-turbo warned of or refused a sound image: {decoded.Error}");
        }

        File.WriteAllText(Path.Combine(folder, "expected.json"),
            JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
