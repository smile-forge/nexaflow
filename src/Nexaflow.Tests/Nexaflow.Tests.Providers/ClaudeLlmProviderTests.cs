using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Network-free surface of the Claude provider: identity, capabilities, the static model list,
/// and the bound/unbound model-info logic. The completion path needs the Anthropic API and is
/// out of scope here (covered by the Tier-B request-mapping seam).
/// </summary>
[TestClass]
public class ClaudeLlmProviderTests
{
    private static ClaudeLlmProvider Make(string model)
        => new(new FakeActivityManager(), new ClaudeConfig(), new ProviderModel(model));

    [TestMethod]
    public void Name_IsClaude()
        => Assert.AreEqual("Claude", Make("claude-opus-4-7").Name);

    [TestMethod]
    public void SupportsImages_IsTrue()
        => Assert.IsTrue(Make("claude-opus-4-7").SupportsImages);

    [TestMethod]
    public async Task GetAvailableModels_ReturnsStaticList_WithoutNetwork()
    {
        var activity = new FakeActivityManager();
        var provider = new ClaudeLlmProvider(activity, new ClaudeConfig(), new ProviderModel(""));

        var models = await provider.GetAvailableModelsAsync();

        Assert.IsTrue(models.Count > 0);
        CollectionAssert.Contains(models.ToList(), "claude-opus-4-7");
        Assert.AreEqual(0, activity.StartedCount, "model enumeration must not report activity");
    }

    [TestMethod]
    public async Task GetModelInfo_ReturnsContextWindow_ForBoundModel()
    {
        var info = await Make("claude-sonnet-4-6").GetModelInfoAsync();

        Assert.IsNotNull(info);
        Assert.AreEqual(200_000, info!.ContextWindowTokens);
        Assert.AreEqual("claude-sonnet-4-6", info.DisplayName);
    }

    [TestMethod]
    public async Task GetModelInfo_IsNull_WhenUnbound()
        => Assert.IsNull(await Make("").GetModelInfoAsync());
}
