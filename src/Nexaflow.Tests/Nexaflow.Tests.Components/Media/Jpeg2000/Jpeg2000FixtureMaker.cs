using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// Writes the JPEG 2000 fixtures into the source tree: each <see cref="Jpeg2000Fixture"/>'s source as raw samples,
/// encoded by libopenjp2's <c>opj_compress</c>, and for the ones held to libopenjp2, the hash of what its
/// <c>opj_decompress</c> decodes them to, in <c>expected.json</c>. Opt-in: <c>NEXAFLOW_OPENJPEG</c> names an OpenJPEG
/// checkout built as <c>tools/bench/Run-JpxBench.ps1</c> builds it; without it the test is inconclusive. Run it after
/// changing the fixture list, and commit what it writes.
/// </summary>
[TestClass]
[NoCoverage("writes the JPEG 2000 fixtures the decoder's tests read — opt-in, never in CI")]
public class Jpeg2000FixtureMaker
{
    private const string OpenJpeg = "NEXAFLOW_OPENJPEG";

    [TestMethod]
    public void WriteTheFixtures()
    {
        var tools = Environment.GetEnvironmentVariable(OpenJpeg) is { Length: > 0 } checkout
            ? Path.Combine(checkout, "build", "bin", "Release")
            : null;
        if (tools is null || !File.Exists(Path.Combine(tools, "opj_compress.exe")))
        {
            Assert.Inconclusive($"set {OpenJpeg} to an OpenJPEG checkout with opj_compress and opj_decompress built");
            return;
        }

        var folder = Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.Components", "Media", "Jpeg2000", "Fixtures");
        Directory.CreateDirectory(folder);
        foreach (var stale in Directory.EnumerateFiles(folder))
            File.Delete(stale);

        var work = TestTemp.CreateSubdirectory("jpeg2000-fixtures-").FullName;
        var transform = Path.Combine(work, "mct.txt");
        File.WriteAllText(transform, Jpeg2000Fixture.CustomTransform);

        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var fixture in Jpeg2000Fixture.All)
        {
            var source = Path.Combine(work, fixture.Name + (fixture.Precision > 8 ? ".rawl" : ".raw"));
            File.WriteAllBytes(source, fixture.RawSource());

            var encoded = Path.Combine(folder, fixture.FileName);
            var format = $"{fixture.Width},{fixture.Height},{fixture.Components},{fixture.Precision},{(fixture.Signed ? 's' : 'u')}";
            Run(Path.Combine(tools, "opj_compress.exe"),
                $"-i \"{source}\" -o \"{encoded}\" -F {format} {fixture.Options.Replace("{mct}", $"\"{transform}\"")}");

            // Every image is decoded, so one opj_compress wrote but libopenjp2 refuses never becomes a fixture.
            var decoded = Path.Combine(work, fixture.Name + "-decoded.rawl");
            Run(Path.Combine(tools, "opj_decompress.exe"), $"-i \"{encoded}\" -o \"{decoded}\"");
            if (fixture.HeldToLibopenjp2)
                expected[fixture.Name] = Jpeg2000Fixture.HashOf(Planes(File.ReadAllBytes(decoded), fixture));
        }

        File.WriteAllText(Path.Combine(folder, "expected.json"),
            JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>
    /// <c>opj_decompress</c>'s raw output read back: plane after plane at the fixture's size, a byte a sample up to 8
    /// bits and two little-endian bytes past it. Only unsigned images are held to it, so no sign is undone.
    /// </summary>
    private static IEnumerable<(int Width, int Height, ReadOnlyMemory<int> Samples)> Planes(byte[] raw, Jpeg2000Fixture fixture)
    {
        Assert.IsFalse(fixture.Signed, $"{fixture.Name}: opj_decompress masks signed samples, so a signed image cannot be held to its output");
        var wide = fixture.Precision > 8;
        var size = fixture.Width * fixture.Height;
        Assert.AreEqual(fixture.Components * size * (wide ? 2 : 1), raw.Length, $"{fixture.Name}: opj_decompress wrote a different size");

        for (int c = 0, at = 0; c < fixture.Components; c++)
        {
            var samples = new int[size];
            for (var i = 0; i < size; i++)
                samples[i] = wide ? raw[at++] | raw[at++] << 8 : raw[at++];
            yield return (fixture.Width, fixture.Height, samples);
        }
    }

    private static void Run(string tool, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(tool, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"{Path.GetFileName(tool)} {arguments}\n{output.Result}\n{errors}");
    }
}
