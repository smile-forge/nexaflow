using Nexaflow.AiHost.Checking;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host checking pictures with OwenElliott's image safety classifier, M. How a picture is laid out for it is checked
/// always; the classifier itself where <c>NEXAFLOW_TEST_PICTURE_CHECK</c> names a folder holding
/// <c>image-safety-classifier-m.onnx</c> — it runs on the CPU, so no CUDA pack is needed. What is asserted is the shape of a
/// verdict and that a plain picture is judged fit, not how well the classifier catches what it is for.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("opt-shell-ai-protection")]
public class PictureCheckingTests
{
    private static AiHostClient? _client;
    private static ModelSpec? _classifier;

    [ClassInitialize]
    public static void Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PICTURE_CHECK") is not { Length: > 0 } folder) return;
        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(null),
        };
        _classifier = new ModelSpec("image-safety-classifier-m", "picture-safety-classifier", folder, "image-safety-classifier-m.onnx", null, 0, 0);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, ModelSpec Classifier) Host()
    {
        if (_client is null || _classifier is null) Assert.Inconclusive("NEXAFLOW_TEST_PICTURE_CHECK names no image safety classifier on this machine.");
        return (_client, _classifier);
    }

    /// <summary>A plain outdoor scene, 640 × 480: a sky lightening toward the horizon over a strip of grass.</summary>
    private static byte[] Meadow()
    {
        const int width = 640, height = 480;
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float t = y / (float)height;
                var (r, g, b) = y > height * 2 / 3 ? (70, 140, 60) : ((int)(90 + 100 * t), (int)(150 + 80 * t), (int)(230 - 20 * t));
                int at = (y * width + x) * 3;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = ((byte)r, (byte)g, (byte)b);
            }
        return PngEncoder.EncodeRgb(rgb, width, height);
    }

    [TestMethod]
    public void APicture_IsScaledWhole_IntoThePlanesTheClassifierReads()
    {
        var picture = new RgbPicture(3, 2, [10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30]);

        var planes = PictureSafetyClassifier.Planes(picture);

        const int plane = PictureSafetyClassifier.Side * PictureSafetyClassifier.Side;
        Assert.AreEqual(3 * plane, planes.Length);
        CollectionAssert.AreEqual(new[] { 10f, 20f, 30f }, new[] { planes[0], planes[plane], planes[2 * plane] }, "red, then green, then blue, in 0 to 255");
        Assert.IsTrue(planes.Take(plane).All(v => v == 10f), "a picture of one colour stays one colour");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task APlainPicture_IsJudgedFit_ItsChancesSummingToOne()
    {
        var (client, classifier) = Host();

        PictureChecked? verdict = null;
        await foreach (var reply in client.SendAsync(id => new CheckPicture(id, classifier, Meadow()), TimeSpan.FromMinutes(1)))
            if (reply is PictureChecked done) verdict = done;

        Assert.IsNotNull(verdict);
        Assert.AreEqual(1f, verdict.Gore + verdict.Sexual + verdict.Neither, 1e-3f);
        Assert.IsTrue(verdict.Neither > 0.8f, $"a meadow is neither gore nor sexual (gore {verdict.Gore:F3}, sexual {verdict.Sexual:F3})");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task WhatIsNotAPicture_IsRefused()
    {
        var (client, classifier) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new CheckPicture(id, classifier, [1, 2, 3, 4]), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "could not be read");
    }
}
