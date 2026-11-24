using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host drawing with MOSS-Image's real parts, <c>moss-image</c> and its LoRAs from the test store (<see cref="TestStore"/>),
/// on the CUDA pack there, without which nothing is drawn. What is asserted is the shape of a drawing — its steps, its size,
/// that it is a picture, that a LoRA
/// changes it — not what it shows; each picture is saved with the run's results for a person to look at.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images-moss")]
public class MossImageModelTests
{
    private const string Fox = "A fox standing in a forest clearing at dawn, mist between the trees.";

    /// <summary>The model, as the catalog names it: the host draws with its parts where it places them.</summary>
    private const string Model = "moss-image";

    private static AiHostClient? _client;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
    _client = await TestStore.HostAsync();
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, string Model) Host()
    {
        if (_client is null) Assert.Inconclusive(TestStore.Missing());
        return (_client, Model);
    }

    /// <summary>The catalog's LoRA <paramref name="id"/> of MOSS-Image, as a request names it: by its id, the host finding its adapter
    /// and — for a distillation that draws in its own steps — its sampler in the store.</summary>
    private static DrawingLora Lora(string id, float strength)
    {
        var lora = ((HostedModel)HostCatalog.Find(Model)!).Loras.Single(l => l.Id == id);
        return new(lora.Id, strength);
    }

    /// <summary>Draws <paramref name="draw"/>'s picture: its PNG and the denoising steps reported, in order.</summary>
    private async Task<(byte[] Png, long[] Steps)> DrawAsync(Func<long, Draw> draw, string? saveAs = null)
    {
        var (client, _) = Host();
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(draw, TimeSpan.FromMinutes(5))) replies.Add(reply);
        var png = replies.OfType<Drawn>().Single().AsPng();
        if (saveAs is not null)
        {
            var saved = Path.Combine(TestContext.TestRunResultsDirectory!, saveAs);
            File.WriteAllBytes(saved, png);
            TestContext.AddResultFile(saved);   // to look at: the test checks its shape, a person checks what it shows
        }
        return (png, [.. replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done)]);
    }

    private static double Spread(PngReader.Picture picture)
    {
        double mean = picture.Rgb.Average(b => (double)b);
        return Math.Sqrt(picture.Rgb.Average(b => (b - mean) * (b - mean)));
    }

    /// <summary>How far apart two pictures of one size are: the mean of their bytes' differences, 0 to 255.</summary>
    private static double Distance(byte[] a, byte[] b)
    {
        var (x, y) = (PngReader.Read(a).Rgb, PngReader.Read(b).Rgb);
        return Enumerable.Range(0, x.Length).Average(i => Math.Abs(x[i] - y[i]));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task APrompt_IsDrawnInFiftyGuidedSteps_AsAPictureOfTheSizeAskedFor()
    {
        var (_, model) = Host();

        var (png, steps) = await DrawAsync(id => new Draw(id, model, Fox, 1344, 768, 7, RequestPriority.Job), "moss-fox.png");

        CollectionAssert.AreEqual(Enumerable.Range(1, 50).Select(i => (long)i).ToArray(), steps, "each guided step is reported as it finishes");
        var picture = PngReader.Read(png);
        Assert.AreEqual((1344, 768), (picture.Width, picture.Height));
        Assert.IsTrue(Spread(picture) > 20, $"a picture, not a flat field (spread {Spread(picture):F1})");
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheSameSeedAndPrompt_DrawTheSamePicture_AndANegativePromptChangesIt()
    {
        var (_, model) = Host();
        const string lighthouse = "A lighthouse at dusk.";

        var first  = await DrawAsync(id => new Draw(id, model, lighthouse, 1024, 1024, 99, RequestPriority.Job));
        var again  = await DrawAsync(id => new Draw(id, model, lighthouse, 1024, 1024, 99, RequestPriority.Job));
        var kept   = await DrawAsync(id => new Draw(id, model, lighthouse, 1024, 1024, 99, RequestPriority.Job, NegativePrompt: "clouds, boats"),
                                     "moss-lighthouse-no-clouds.png");

        CollectionAssert.AreEqual(PngReader.Read(first.Png).Rgb, PngReader.Read(again.Png).Rgb);
        Assert.IsTrue(Distance(first.Png, kept.Png) > 1, "the negative prompt is read: it changes the picture");
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task AStyleLora_IsDrawnInTheModelsOwnSteps_AndChangesThePicture()
    {
        var (_, model) = Host();
        const string seinen = "moss_seinen_style, " + Fox;

        var plain = await DrawAsync(id => new Draw(id, model, seinen, 1024, 1024, 3, RequestPriority.Job));
        var anime = await DrawAsync(id => new Draw(id, model, seinen, 1024, 1024, 3, RequestPriority.Job, Lora: Lora("moss-anime", 1f)), "moss-fox-anime.png");

        Assert.AreEqual(50, anime.Steps.Length, "a style LoRA draws with the model's own sampler");
        Assert.IsTrue(Distance(plain.Png, anime.Png) > 5, $"the LoRA changes the picture (distance {Distance(plain.Png, anime.Png):F1})");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TheDistillation_DrawsInItsOwnEightSteps()
    {
        var (_, model) = Host();

        var (png, steps) = await DrawAsync(id => new Draw(id, model, Fox, 1024, 1024, 11, RequestPriority.Job, Lora: Lora("moss-fast-8step", 1f)),
                                           "moss-fox-fast.png");

        CollectionAssert.AreEqual(Enumerable.Range(1, 8).Select(i => (long)i).ToArray(), steps, "its own sampler's steps, one pass each");
        Assert.IsTrue(Spread(PngReader.Read(png)) > 20, "a picture, not a flat field");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task TheDistillationWithANegativePrompt_IsRefused()
    {
        var (client, model) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, model, Fox, 1024, 1024, 1, RequestPriority.Job, NegativePrompt: "people",
                                                                   Lora: Lora("moss-fast-8step", 1f)), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "no negative prompt");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ALoraOrANegativePrompt_ForAModelThatTakesNeither_IsRefusedBeforeAnythingLoads()
    {
        var (client, _) = Host();
        // Nothing of Z-Image's need be here: the request is refused before a part is asked for.
        const string zImage = "z-image-turbo";

        var lora = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, zImage, Fox, 1024, 1024, 1, RequestPriority.Job, Lora: Lora("moss-anime", 1f)),
                                                     TimeSpan.FromMinutes(1))) { }
        });
        var negative = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, zImage, Fox, 1024, 1024, 1, RequestPriority.Job, NegativePrompt: "people"),
                                                     TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(lora.Message, "takes no LoRA");
        StringAssert.Contains(negative.Message, "told nothing to keep out");
    }
}
