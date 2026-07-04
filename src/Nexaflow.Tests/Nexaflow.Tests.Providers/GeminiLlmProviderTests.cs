using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Network-free surface of the Gemini provider. Model enumeration hits Google's endpoint (no overridable
/// base URL) so it is not exercised here; the identity/capability/model-info surface is.
/// </summary>
[TestClass]
public class GeminiLlmProviderTests
{
    // Typed as ILlmProvider so default-interface members (GetModelInfoAsync) resolve.
    private static ILlmProvider Make(string model)
        => new GeminiLlmProvider(new FakeActivityManager(), new GeminiConfig { ApiKey = "test" }, new ProviderModel(model));

    [TestMethod]
    public void Name_IsGemini()
        => Assert.AreEqual("Gemini", Make("gemini-2.0-flash").Name);

    [TestMethod]
    public void SupportsImages_IsTrue()
        => Assert.IsTrue(Make("gemini-2.0-flash").SupportsImages);

    [TestMethod]
    public async Task GetModelInfo_IsNull_ByDefault()
        => Assert.IsNull(await Make("gemini-2.0-flash").GetModelInfoAsync());
}
