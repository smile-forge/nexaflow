using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAI;

namespace Nexaflow.Tests.Providers;

/// <summary>A provider type's kind comes from the contract it implements — what the pool and the grid key it by.</summary>
[TestClass]
public class ModelKindsTests
{
    [TestMethod]
    public void OpenAI_ShipsALanguageModelAndASpeechToTextModel_UnderOneName()
    {
        CollectionAssert.AreEqual(new[] { ModelKind.Language },     ModelKinds.Of(typeof(OpenAILlmProvider)).ToArray());
        CollectionAssert.AreEqual(new[] { ModelKind.SpeechToText }, ModelKinds.Of(typeof(OpenAISpeechToTextProvider)).ToArray());
        Assert.AreEqual(OpenAILlmProvider.ProviderName,
            new OpenAISpeechToTextProvider(null!, new OpenAIConfig(), new ProviderModel("")).Name);
    }

    [TestMethod]
    public void ATypeThatIsNoProvider_ServesNothing()
        => Assert.AreEqual(0, ModelKinds.Of(typeof(string)).Count);
}
