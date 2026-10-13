using System.Text;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Providers.OpenAI;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Pictures and clips made by the real OpenAI and Gemini APIs, which bill for each. A test runs only when its key and
/// model are named: <c>NEXAFLOW_TEST_OPENAI_KEY</c> with <c>NEXAFLOW_TEST_OPENAI_IMAGE_MODEL</c> or
/// <c>NEXAFLOW_TEST_OPENAI_VIDEO_MODEL</c>; <c>NEXAFLOW_TEST_GEMINI_KEY</c> with <c>NEXAFLOW_TEST_GEMINI_IMAGE_MODEL</c> or
/// <c>NEXAFLOW_TEST_GEMINI_VIDEO_MODEL</c>. Unset, each is inconclusive.
/// </summary>
[TestClass]
public class CloudGenerationTests
{
    private static (string Key, string Model) Named(string keyVariable, string modelVariable)
    {
        var key   = Environment.GetEnvironmentVariable(keyVariable);
        var model = Environment.GetEnvironmentVariable(modelVariable);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model)) Assert.Inconclusive($"{keyVariable} and {modelVariable} are not both set.");
        return (key, model);
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task OpenAI_DrawsAPicture_AndWorksFromIt()
    {
        var (key, model) = Named("NEXAFLOW_TEST_OPENAI_KEY", "NEXAFLOW_TEST_OPENAI_IMAGE_MODEL");
        await DrawsAndWorksFromItAsync(new OpenAIImageGenerationProvider(new FakeActivityManager(), new OpenAIConfig { ApiKey = key }, new ProviderModel(model)));
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task Gemini_DrawsAPicture_AndWorksFromIt()
    {
        var (key, model) = Named("NEXAFLOW_TEST_GEMINI_KEY", "NEXAFLOW_TEST_GEMINI_IMAGE_MODEL");
        await DrawsAndWorksFromItAsync(new GeminiImageGenerationProvider(new FakeActivityManager(), new GeminiConfig { ApiKey = key }, new ProviderModel(model)));
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task OpenAI_MakesAClip()
    {
        var (key, model) = Named("NEXAFLOW_TEST_OPENAI_KEY", "NEXAFLOW_TEST_OPENAI_VIDEO_MODEL");
        await MakesAClipAsync(new OpenAIVideoGenerationProvider(new FakeActivityManager(), new OpenAIConfig { ApiKey = key }, new ProviderModel(model)));
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task Gemini_MakesAClip()
    {
        var (key, model) = Named("NEXAFLOW_TEST_GEMINI_KEY", "NEXAFLOW_TEST_GEMINI_VIDEO_MODEL");
        await MakesAClipAsync(new GeminiVideoGenerationProvider(new FakeActivityManager(), new GeminiConfig { ApiKey = key }, new ProviderModel(model)));
    }

    private static async Task DrawsAndWorksFromItAsync(IImageGenerationProvider provider)
    {
        var drawn = await provider.GenerateImageAsync(new ImageGenerationRequest("A small red circle on a white background.", MediaShape.Landscape));
        AssertPicture(drawn.Media);
        if (provider.MaxSources == 0) return;

        var redrawn = await provider.GenerateImageAsync(new ImageGenerationRequest("Make the circle blue.") { Sources = [drawn.Media] });
        AssertPicture(redrawn.Media);
    }

    private static async Task MakesAClipAsync(IVideoGenerationProvider provider)
    {
        var made = await provider.GenerateVideoAsync(new VideoGenerationRequest("A red ball rolls slowly across a white floor.", MediaShape.Landscape, provider.Lengths[0]));

        Assert.AreEqual("video/mp4", made.Media.MimeType);
        Assert.AreEqual("ftyp", Encoding.ASCII.GetString(made.Media.Bytes, 4, 4), "an MP4 opens with its ftyp box");
    }

    private static void AssertPicture(MediaBytes picture)
    {
        StringAssert.StartsWith(picture.MimeType, "image/");
        Assert.IsTrue(picture.Bytes.Length > 1000, $"a picture, not {picture.Bytes.Length} bytes");
    }
}
