using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[NoCoverage("ConfigManager instance-registration plumbing — no single product node")]
public class ConfigManagerRegisterTests
{
    private sealed class DummyConfig { public int Value { get; set; } }

    // App.xaml.cs pre-registers some global configs (ExternalAppsConfig/FileMapConfig) before
    // FeatureManager re-registers them with its own fresh instance. Register must hand back the
    // FIRST (canonical) instance so both sides share one object — otherwise the feature mutates a
    // shadow copy and the Options panel never sees the change.
    [TestMethod]
    public void Register_DuplicateName_ReturnsCanonicalFirstInstance()
    {
        const string name = "test-canonical-dummy";
        var first  = new DummyConfig { Value = 1 };
        var second = new DummyConfig { Value = 2 };

        var r1 = ConfigManager.Instance.Register(first,  name);
        var r2 = ConfigManager.Instance.Register(second, name);

        Assert.AreSame(first, r1);
        Assert.AreSame(first, r2);   // second registration gets the canonical first instance back
    }
}
