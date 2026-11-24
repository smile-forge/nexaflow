using Nexaflow.AiHost.Checking;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host checking pictures with OwenElliott's image safety classifier, M. How a picture is laid out for it is checked
/// always; the classifier itself where the test store (<see cref="TestStore"/>) holds <c>image-safety-classifier-m</c> — it
/// runs on the CPU too, so no CUDA pack is needed. What is asserted is the shape of a
/// verdict and that a plain picture is judged fit, not how well the classifier catches what it is for.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("opt-shell-ai-protection")]
public class PictureCheckingTests
{
    /// <summary>The checking tool, as the catalog names it: the host runs its classifier where it places it.</summary>
    private const string Tool = "image-safety-classifier-m";

    private static AiHostClient? _client;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        _client = await TestStore.HostAsync(gpu: false);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, string Tool) Host()
    {
        if (_client is null) Assert.Inconclusive(TestStore.Missing(gpu: false));
        return (_client, Tool);
    }

    /// <summary>A plain outdoor scene, 640 × 480: a sky lightening toward the horizon over a strip of grass, as its RGB.</summary>
    private static byte[] Meadow()
    {
        var rgb = new byte[MeadowWidth * MeadowHeight * 3];
        for (int y = 0; y < MeadowHeight; y++)
            for (int x = 0; x < MeadowWidth; x++)
            {
                float t = y / (float)MeadowHeight;
                var (r, g, b) = y > MeadowHeight * 2 / 3 ? (70, 140, 60) : ((int)(90 + 100 * t), (int)(150 + 80 * t), (int)(230 - 20 * t));
                int at = (y * MeadowWidth + x) * 3;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = ((byte)r, (byte)g, (byte)b);
            }
        return rgb;
    }

    private const int MeadowWidth = 640, MeadowHeight = 480;

    [TestMethod]
    public void APicture_IsScaledWhole_IntoThePlanesTheClassifierReads()
    {
        byte[] rgb = [10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30];

        var planes = PictureSafetyClassifier.Planes(rgb, 3, 2);

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
        await foreach (var reply in client.SendAsync(id => new CheckPicture(id, classifier, MeadowWidth, MeadowHeight, SharedBytes.Of(Meadow())), TimeSpan.FromMinutes(1)))
            if (reply is PictureChecked done) verdict = done;

        Assert.IsNotNull(verdict);
        Assert.AreEqual(1f, verdict.Gore + verdict.Sexual + verdict.Neither, 1e-3f);
        Assert.IsTrue(verdict.Neither > 0.8f, $"a meadow is neither gore nor sexual (gore {verdict.Gore:F3}, sexual {verdict.Sexual:F3})");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PixelsThatDoNotFillThePicture_AreRefused()
    {
        var (client, classifier) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new CheckPicture(id, classifier, 2, 2, SharedBytes.Of([1, 2, 3, 4])), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "bytes of RGB");
    }
}
