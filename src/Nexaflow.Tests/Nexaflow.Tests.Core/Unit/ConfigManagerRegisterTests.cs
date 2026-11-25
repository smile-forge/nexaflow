using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[NoCoverage("ConfigManager instance-registration plumbing — no single product node")]
public class ConfigManagerRegisterTests
{
    private sealed class DummyConfig { public int Value { get; set; } }

    // The session (ShellHost) registers some global configs before FeatureManager re-registers them
    // with its own fresh instance as their assembly activates. Register must hand back the FIRST
    // (canonical) instance so both sides share one object — otherwise the feature mutates a shadow
    // copy and the Options panel never sees the change.
    [TestMethod]
    public void Register_DuplicateName_ReturnsCanonicalFirstInstance()
    {
        const string name = "test-canonical-dummy";
        var config = TestRuntimes.ConfigIn(TestTemp.CreateSubdirectory("register-").FullName);
        var first  = new DummyConfig { Value = 1 };
        var second = new DummyConfig { Value = 2 };

        var r1 = config.Register(first,  name);
        var r2 = config.Register(second, name);

        Assert.AreSame(first, r1);
        Assert.AreSame(first, r2);   // second registration gets the canonical first instance back
    }
}
