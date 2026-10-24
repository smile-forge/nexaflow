using System.Text.Json.Nodes;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.XAI;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The xAI provider asks xAI what it serves and what each model can do, rather than guessing from a model's name — a
/// backend with known answers stands in for xAI, so no request reaches the network.
/// </summary>
[TestClass]
public class XAILlmProviderTests
{
    private const string Key = "xai-test";

    private static readonly Dictionary<string, string> Answers = new()
    {
        ["/v1/language-models"]                = """{"models":[{"id":"grok-code-fast","input_modalities":["text"]},{"id":"grok-4.3","input_modalities":["text","image"]}]}""",
        ["/v1/models"]                         = """{"object":"list","data":[{"id":"grok-4.3"},{"id":"grok-code-fast"},{"id":"grok-imagine-image"}]}""",
        ["/v1/language-models/grok-4.3"]       = """{"id":"grok-4.3","input_modalities":["text","image"],"output_modalities":["text"]}""",
        ["/v1/models/grok-4.3"]                = """{"id":"grok-4.3","object":"model","context_length":1000000}""",
        ["/v1/language-models/grok-code-fast"] = """{"id":"grok-code-fast","input_modalities":["text"],"output_modalities":["text"]}""",
        ["/v1/models/grok-code-fast"]          = """{"id":"grok-code-fast","object":"model","context_length":256000}""",
    };

    private readonly CannedBackend _xai  = new(Answers);
    private readonly DeadBackend   _dead = new();

    [TestCleanup]
    public void StopBackends()
    {
        _xai.Dispose();
        _dead.Dispose();
    }

    // Typed as ILlmProvider so default-interface members resolve.
    private static ILlmProvider Against(string url, string model, string key = Key)
        => new XAILlmProvider(new FakeActivityManager(), new XAIConfig { ApiKey = key, BaseUrl = url + "/v1" }, new ProviderModel(model));

    [TestMethod]
    public void ItIsXAI_ALanguageModelCallingTools_WhoseRequestsLeaveTheMachine()
    {
        var provider = new XAILlmProvider(new FakeActivityManager(), new XAIConfig { ApiKey = Key }, new ProviderModel(""));

        Assert.AreEqual(("xAI", DataResidency.LeavesMachine), (provider.Name, provider.Residency));
        CollectionAssert.AreEqual(new[] { ModelKind.Language }, ModelKinds.Of(typeof(XAILlmProvider)).ToArray());
        Assert.IsTrue(provider.SupportsToolCalls);
    }

    [TestMethod]
    public async Task WithoutAKey_ItIsUnavailable_AndAsksXAINothing()
    {
        var provider = Against(_xai.Url, "grok-4.3", key: "");

        Assert.IsFalse(provider.IsAvailable);
        Assert.IsNull(await provider.GetModelInfoAsync());
        Assert.AreEqual(0, _xai.Requests.Count);
    }

    [TestMethod]
    public async Task TheModelsOffered_AreItsLanguageModels_NotItsPictureModels_AskedWithTheKey()
    {
        var models = await Against(_xai.Url, "").GetAvailableModelsAsync();

        CollectionAssert.AreEqual(new[] { "grok-4.3", "grok-code-fast" }, models.ToArray());
        Assert.IsTrue(_xai.Requests.All(r => r.Authorization == $"Bearer {Key}"));
    }

    [TestMethod]
    public async Task AModelXAISaysTakesPictures_IsSentThem_AndReadsWhatXAISaysItReads()
    {
        var provider = Against(_xai.Url, "grok-4.3");

        Assert.AreEqual(1_000_000, (await provider.GetModelInfoAsync())!.ContextWindowTokens);
        Assert.IsTrue(provider.SupportsImages);
    }

    [TestMethod]
    public async Task AModelThatTakesOnlyText_IsSentNoPictures()
    {
        var provider = Against(_xai.Url, "grok-code-fast");

        Assert.AreEqual(256_000, (await provider.GetModelInfoAsync())!.ContextWindowTokens);
        Assert.IsFalse(provider.SupportsImages);
    }

    [TestMethod]
    public async Task AModelXAIDoesNotServe_HasNoDetails_AndIsSentNoPictures()
    {
        var provider = Against(_xai.Url, "grok-gone");

        Assert.IsNull(await provider.GetModelInfoAsync());
        Assert.IsFalse(provider.SupportsImages);
    }

    [TestMethod]
    public async Task GetAvailableModels_ReturnsEmpty_WhenBackendUnreachable_AndNeverThrows()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var provider = Against(_dead.Url, "");

        Assert.AreEqual(0, (await provider.GetAvailableModelsAsync(cts.Token)).Count);
        Assert.IsNotNull(provider.LastModelListError, "The failure reason should be surfaced out-of-band.");
    }

    [TestMethod]
    public void WhatXAIWrites_IsRead_AndWhatItLeavesOut_IsNotGuessed()
    {
        Assert.IsTrue(XAIModelCatalog.TakesImages(JsonNode.Parse("""{"input_modalities":["text","image"]}""")));
        Assert.IsFalse(XAIModelCatalog.TakesImages(JsonNode.Parse("""{"input_modalities":["text"]}""")));
        Assert.IsFalse(XAIModelCatalog.TakesImages(JsonNode.Parse("{}")));

        Assert.AreEqual(131_072, XAIModelCatalog.ContextLength(JsonNode.Parse("""{"context_length":131072}""")));
        Assert.IsNull(XAIModelCatalog.ContextLength(JsonNode.Parse("{}")));

        Assert.AreEqual(0, XAIModelCatalog.ReadLanguageModels(JsonNode.Parse("{}")).Count);
    }
}
