using Nexaflow.Providers.Local.Catalog;

namespace Nexaflow.Providers.Local.Harness;

/// <summary>Picks the prompt/tool harness for a model family.</summary>
public static class HarnessFactory
{
    public static IModelHarness Create(ModelFamily family) => family switch
    {
        ModelFamily.Qwen => new QwenHarness(),
        _                => new GemmaHarness(),
    };
}
