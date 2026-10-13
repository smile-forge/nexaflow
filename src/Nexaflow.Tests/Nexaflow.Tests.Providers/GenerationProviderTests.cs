using System.Text;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Providers.OpenAI;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The image and video models OpenAI and Gemini serve: which of a key's models each lists, what each takes, and how a
/// request is put and an answer read — everything short of the network.
/// </summary>
[TestClass]
public class GenerationProviderTests
{
    private static readonly MediaBytes Png = new([1, 2, 3], "image/png");

    [TestMethod]
    public void EachBackend_ShipsItsImageAndVideoModels_UnderItsOwnName()
    {
        CollectionAssert.AreEqual(new[] { ModelKind.ImageGeneration }, ModelKinds.Of(typeof(OpenAIImageGenerationProvider)).ToArray());
        CollectionAssert.AreEqual(new[] { ModelKind.VideoGeneration }, ModelKinds.Of(typeof(OpenAIVideoGenerationProvider)).ToArray());
        CollectionAssert.AreEqual(new[] { ModelKind.ImageGeneration }, ModelKinds.Of(typeof(GeminiImageGenerationProvider)).ToArray());
        CollectionAssert.AreEqual(new[] { ModelKind.VideoGeneration }, ModelKinds.Of(typeof(GeminiVideoGenerationProvider)).ToArray());

        var openAI = new OpenAIConfig();
        var gemini = new GeminiConfig();
        var none   = new FakeActivityManager();
        Assert.AreEqual(OpenAILlmProvider.ProviderName, new OpenAIImageGenerationProvider(none, openAI, new ProviderModel("gpt-image-1")).Name);
        Assert.AreEqual(OpenAILlmProvider.ProviderName, new OpenAIVideoGenerationProvider(none, openAI, new ProviderModel("sora-2")).Name);
        Assert.AreEqual(GeminiLlmProvider.ProviderName, new GeminiImageGenerationProvider(none, gemini, new ProviderModel("imagen-4.0-generate-001")).Name);
        Assert.AreEqual(GeminiLlmProvider.ProviderName, new GeminiVideoGenerationProvider(none, gemini, new ProviderModel("veo-3.0-generate-001")).Name);
        Assert.IsTrue(new IAiProvider[]
        {
            new OpenAIImageGenerationProvider(none, openAI, new ProviderModel("gpt-image-1")),
            new GeminiVideoGenerationProvider(none, gemini, new ProviderModel("veo-3.0-generate-001")),
        }.All(p => !p.IsAvailable), "nothing is made without a key");
    }

    [TestMethod]
    public void OpenAI_ListsTheModelsThatDraw_LeavingOutTheSmallSquareOne()
    {
        string[] models = ["gpt-4o", "gpt-image-1", "gpt-image-1-mini", "dall-e-2", "dall-e-3", "sora-2", "tts-1"];

        CollectionAssert.AreEqual(new[] { "gpt-image-1", "gpt-image-1-mini", "dall-e-3" }, models.Where(OpenAIImageGenerationProvider.IsImageModel).ToArray());
        CollectionAssert.AreEqual(new[] { "sora-2" }, models.Where(OpenAIVideoGenerationProvider.IsVideoModel).ToArray());
    }

    [TestMethod]
    public void OpenAI_GptImageWorksFromAPictureUnderAMask_WhileDallE3OnlyDraws()
    {
        var none = new FakeActivityManager();
        var gpt  = new OpenAIImageGenerationProvider(none, new OpenAIConfig(), new ProviderModel("gpt-image-1"));
        var dall = new OpenAIImageGenerationProvider(none, new OpenAIConfig(), new ProviderModel("dall-e-3"));

        Assert.AreEqual((1, true), (gpt.MaxSources, gpt.Masks));
        Assert.AreEqual((0, false), (dall.MaxSources, dall.Masks));
    }

    [TestMethod]
    [DataRow("gpt-image-1", MediaShape.Square, "1024x1024")]
    [DataRow("gpt-image-1", MediaShape.Landscape, "1536x1024")]
    [DataRow("gpt-image-1", MediaShape.Portrait, "1024x1536")]
    [DataRow("dall-e-3", MediaShape.Landscape, "1792x1024")]
    [DataRow("dall-e-3", MediaShape.Portrait, "1024x1792")]
    public void OpenAI_DrawsEachShapeAtASizeItsModelTakes(string model, MediaShape shape, string size)
        => Assert.AreEqual(size, OpenAIImageGenerationProvider.Size(model, shape).ToString());

    [TestMethod]
    public void OpenAI_NamesAPictureForTheFormatItIs()
    {
        Assert.AreEqual("source.png", OpenAIImageGenerationProvider.FileName("source", Png));
        Assert.AreEqual("mask.jpg", OpenAIImageGenerationProvider.FileName("mask", new MediaBytes([], "image/jpeg")));
        Assert.AreEqual("frame.webp", OpenAIImageGenerationProvider.FileName("frame", new MediaBytes([], "image/webp")));
    }

    [TestMethod]
    public async Task OpenAI_RefusesMoreSourcesThanTheModelTakes_BeforeAskingAnything()
    {
        var activities = new FakeActivityManager();
        var dall = new OpenAIImageGenerationProvider(activities, new OpenAIConfig { ApiKey = "unused" }, new ProviderModel("dall-e-3"));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => dall.GenerateImageAsync(new ImageGenerationRequest("a fox") { Sources = [Png] }));
        Assert.AreEqual(0, activities.StartedCount);
    }

    [TestMethod]
    public async Task Sora_IsAskedInAForm_CarryingThePictureToStartFrom()
    {
        var request = new VideoGenerationRequest("a fox runs", MediaShape.Portrait, 8) { FirstFrame = Png };

        var (body, contentType) = await OpenAIVideoGenerationProvider.RequestBodyAsync("sora-2", request, default);
        var form = Encoding.UTF8.GetString(body.ToArray());

        StringAssert.StartsWith(contentType, "multipart/form-data; boundary=");
        foreach (var (field, value) in new[] { ("model", "sora-2"), ("prompt", "a fox runs"), ("seconds", "8"), ("size", "720x1280") })
            StringAssert.Matches(form, new System.Text.RegularExpressions.Regex($"name=\"?{field}\"?\\r\\n(?:[^\\r]+\\r\\n)*\\r\\n{value}\\r\\n"), field);
        StringAssert.Matches(form, new System.Text.RegularExpressions.Regex("name=\"?input_reference\"?; filename=\"?frame\\.png\"?"));
    }

    [TestMethod]
    public void Sora_MakesFourEightOrTwelveSecondsAt720p_AndNoSquareClips()
    {
        var sora = new OpenAIVideoGenerationProvider(new FakeActivityManager(), new OpenAIConfig(), new ProviderModel("sora-2"));

        CollectionAssert.AreEqual(new[] { 4, 8, 12 }, sora.Lengths.ToArray());
        Assert.AreEqual((1280, 720), sora.FirstFrameSize(MediaShape.Landscape));
        Assert.AreEqual((720, 1280), sora.FirstFrameSize(MediaShape.Portrait));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sora.FirstFrameSize(MediaShape.Square));
    }

    [TestMethod]
    public void Sora_ReadsHowFarAJobHasGot_AndWhyOneFailed()
    {
        var running = OpenAIVideoGenerationProvider.ReadJob(BinaryData.FromString("""{"id":"video_1","object":"video","status":"in_progress","progress":42}"""));
        var failed  = OpenAIVideoGenerationProvider.ReadJob(BinaryData.FromString("""{"id":"video_1","status":"failed","error":{"code":"moderation","message":"blocked by moderation"}}"""));

        Assert.AreEqual(new OpenAIVideoGenerationProvider.VideoJob("video_1", "in_progress", 42, null), running);
        Assert.AreEqual(("failed", "blocked by moderation"), (failed.Status, failed.Error));
    }

    [TestMethod]
    public void Gemini_ListsImagenAndItsOwnImageModels_AndVeo()
    {
        string[] models = ["gemini-2.5-flash", "gemini-2.5-flash-image", "gemini-3-pro-image-preview", "imagen-4.0-generate-001", "veo-3.0-generate-001", "embedding-001"];

        CollectionAssert.AreEqual(new[] { "gemini-2.5-flash-image", "gemini-3-pro-image-preview", "imagen-4.0-generate-001" },
                                  models.Where(GeminiImageGenerationProvider.IsImageModel).ToArray());
        CollectionAssert.AreEqual(new[] { "veo-3.0-generate-001" }, models.Where(GeminiVideoGenerationProvider.IsVideoModel).ToArray());
    }

    [TestMethod]
    public void Gemini_ItsOwnImageModelsWorkFromPictures_ImagenOnlyDraws_AndNeitherTakesAMask()
    {
        var none   = new FakeActivityManager();
        var flash  = new GeminiImageGenerationProvider(none, new GeminiConfig(), new ProviderModel("gemini-2.5-flash-image"));
        var imagen = new GeminiImageGenerationProvider(none, new GeminiConfig(), new ProviderModel("imagen-4.0-generate-001"));

        Assert.AreEqual((3, false), (flash.MaxSources, flash.Masks));
        Assert.AreEqual((0, false), (imagen.MaxSources, imagen.Masks));
    }

    [TestMethod]
    public void Gemini_IsAskedWithThePromptThenThePicturesItSpeaksOf()
    {
        var ask = GeminiImageGenerationProvider.Ask(new ImageGenerationRequest("make it night") { Sources = [Png] });

        Assert.AreEqual("user", ask.Role);
        Assert.AreEqual("make it night", ask.Parts![0].Text);
        CollectionAssert.AreEqual(Png.Bytes, ask.Parts[1].InlineData!.Data);
        Assert.AreEqual("image/png", ask.Parts[1].InlineData!.MimeType);
    }

    [TestMethod]
    public void Gemini_TakesThePictureFromItsReply_WithWhatItSaidBesideIt()
    {
        var made = GeminiImageGenerationProvider.Read(Reply(
            new Part { Text = "thinking it over", Thought = true },
            new Part { Text = "Here is the fox at night." },
            new Part { InlineData = new Blob { Data = [9, 9], MimeType = "image/png" } }));

        CollectionAssert.AreEqual(new byte[] { 9, 9 }, made.Media.Bytes);
        Assert.AreEqual("Here is the fox at night.", made.Note);
    }

    [TestMethod]
    public void Gemini_AReplyWithNoPicture_SaysWhatTheModelSaidInstead()
    {
        var ex = Assert.ThrowsExactly<LlmProviderException>(() => GeminiImageGenerationProvider.Read(Reply(new Part { Text = "I can't draw that." })));

        StringAssert.Contains(ex.Message, "I can't draw that.");
    }

    [TestMethod]
    public void Veo_MakesTheLengthsEachGenerationOffers_InAnyShapeButSquare()
    {
        CollectionAssert.AreEqual(new[] { 4, 6, 8 }, GeminiVideoGenerationProvider.LengthsOf("veo-3.0-generate-001").ToArray());
        CollectionAssert.AreEqual(new[] { 5, 6, 7, 8 }, GeminiVideoGenerationProvider.LengthsOf("veo-2.0-generate-001").ToArray());
        Assert.AreEqual("9:16", GeminiVideoGenerationProvider.AspectRatio(MediaShape.Portrait));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GeminiVideoGenerationProvider.AspectRatio(MediaShape.Square));
        Assert.IsNull(new GeminiVideoGenerationProvider(new FakeActivityManager(), new GeminiConfig(), new ProviderModel("veo-3.0-generate-001"))
                          .FirstFrameSize(MediaShape.Landscape), "Veo takes a first frame of any size");
    }

    [TestMethod]
    public void Veo_AFinishedJob_GivesItsClip_OrWhyItMadeNone()
    {
        var clip = new GeneratedVideo { Video = new Video { Uri = "https://example.invalid/clip" } };

        Assert.AreSame(clip, GeminiVideoGenerationProvider.Made(new GenerateVideosOperation { Done = true, Response = new GenerateVideosResponse { GeneratedVideos = [clip] } }));
        StringAssert.Contains(Assert.ThrowsExactly<LlmProviderException>(() => GeminiVideoGenerationProvider.Made(
            new GenerateVideosOperation { Done = true, Error = new() { ["message"] = "quota exceeded" } })).Message, "quota exceeded");
        StringAssert.Contains(Assert.ThrowsExactly<LlmProviderException>(() => GeminiVideoGenerationProvider.Made(
            new GenerateVideosOperation { Done = true, Response = new GenerateVideosResponse { RaiMediaFilteredReasons = ["people"] } })).Message, "people");
    }

    private static GenerateContentResponse Reply(params Part[] parts) =>
        new() { Candidates = [new Candidate { Content = new Content { Role = "model", Parts = [.. parts] } }] };
}
