using Nexaflow.Providers.Common;
using Nexaflow.Providers.Ollama;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>Network-free surface of the Ollama provider plus the "never throws" model-list contract.</summary>
[TestClass]
public class OllamaLlmProviderTests
{
    // Discard port on loopback → connection refused immediately, so the catch-all returns [] fast.
    // Typed as ILlmProvider so default-interface members (SupportsImages) resolve.
    private static ILlmProvider Unreachable(string model = "")
        => new OllamaLlmProvider(new FakeActivityManager(),
               new OllamaConfig { Url = "http://127.0.0.1:9" },
               new ProviderModel(model));

    [TestMethod]
    public void Name_IsOllama()
        => Assert.AreEqual("Ollama", Unreachable("llama3").Name);

    [TestMethod]
    public void SupportsImages_IsFalse_ByDefault()
        => Assert.IsFalse(Unreachable("llama3").SupportsImages);

    [TestMethod]
    public async Task GetAvailableModels_ReturnsEmpty_WhenBackendUnreachable_AndNeverThrows()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var models = await Unreachable().GetAvailableModelsAsync(cts.Token);
        Assert.AreEqual(0, models.Count);
    }

    [TestMethod]
    public async Task GetModelInfo_IsNull_WhenUnbound_WithoutNetwork()
        => Assert.IsNull(await Unreachable("").GetModelInfoAsync());
}
