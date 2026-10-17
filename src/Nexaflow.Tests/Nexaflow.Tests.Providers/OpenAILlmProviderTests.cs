using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAI;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>Network-free surface of the OpenAI provider plus the "never throws" model-list contract.</summary>
[TestClass]
public class OpenAILlmProviderTests
{
    private readonly DeadBackend _backend = new();

    [TestCleanup]
    public void StopBackend() => _backend.Dispose();

    // Typed as ILlmProvider so default-interface members (GetModelInfoAsync) resolve.
    private ILlmProvider Unreachable(string model = "")
        => new OpenAILlmProvider(new FakeActivityManager(),
               new OpenAIConfig { ApiKey = "test", BaseUrl = _backend.Url + "/v1" },
               new ProviderModel(model));

    [TestMethod]
    public void Name_IsOpenAI()
        => Assert.AreEqual("OpenAI", Unreachable("gpt-4o").Name);

    [TestMethod]
    public void SupportsImages_IsTrue()
        => Assert.IsTrue(Unreachable("gpt-4o").SupportsImages);

    [TestMethod]
    public async Task GetAvailableModels_ReturnsEmpty_WhenBackendUnreachable_AndNeverThrows()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var provider = Unreachable();
        var models = await provider.GetAvailableModelsAsync(cts.Token);
        Assert.AreEqual(0, models.Count);
        Assert.IsNotNull(provider.LastModelListError, "The failure reason should be surfaced out-of-band.");
    }

    [TestMethod]
    public async Task GetModelInfo_UsesFamilyTable_NullForUnknown()
    {
        var known = await Unreachable("gpt-4o").GetModelInfoAsync();
        Assert.AreEqual(128_000, known!.ContextWindowTokens);

        Assert.IsNull(await Unreachable("some-custom-model").GetModelInfoAsync());
    }
}
