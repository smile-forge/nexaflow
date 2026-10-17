using System.IO;
using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// An ability's chain as the workspace saves it — and a grid saved with one model per ability, read back.
/// </summary>
[TestClass]
[CoversNode("opt-ai-ability-grid")]
public class AbilityChainTests
{
    private static string NewDir() => Path.Combine(TestTemp.Root, "nexaflow-test-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void AChain_IsSavedAndReadBackInOrder()
    {
        var dir = NewDir();
        try
        {
            var saved = new AiConfig { Assignments = { ["Conversation"] = AbilityChain.Of("b", "a") } };
            ConfigManager.Instance.SaveTo(dir, saved, saved.ConfigName);

            var loaded = new AiConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, loaded.ConfigName);

            CollectionAssert.AreEqual(new[] { "b", "a" }, loaded.Assignments["Conversation"].ColumnIds.ToArray());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void AGridSavedWithOneModelPerAbility_ReadsAsChainsOfOne()
    {
        var dir = NewDir();
        try
        {
            // The file name carries the running version, so the loader reads it as it stands — no migration step.
            var config = new AiConfig();
            ConfigManager.Instance.SaveTo(dir, config, config.ConfigName);
            var file = Directory.GetFiles(Path.Combine(dir, config.ConfigName), "config_*.json").Single();
            File.WriteAllText(file,
                """{ "Columns": [ { "Id": "c1", "ProviderName": "Claude", "Model": "m" } ], "Assignments": { "Conversation": "c1", "Analysis": "" } }""");

            var loaded = new AiConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, loaded.ConfigName);

            CollectionAssert.AreEqual(new[] { "c1" }, loaded.Assignments["Conversation"].ColumnIds.ToArray());
            Assert.IsTrue(loaded.Assignments["Analysis"].IsEmpty, "an unassigned ability stays unassigned");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
