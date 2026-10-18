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
            var saved = new AiConfig { Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("b", "a") } };
            ConfigManager.Instance.SaveTo(dir, saved, saved.ConfigName);

            var loaded = new AiConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, loaded.ConfigName);

            CollectionAssert.AreEqual(new[] { "b", "a" }, loaded.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
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
                """{ "Columns": [ { "Id": "c1", "ProviderName": "Claude", "Model": "m" } ], "Assignments": { "GeneralIntelligence": "c1", "ImageRecognition": "" } }""");

            var loaded = new AiConfig();
            ConfigManager.Instance.LoadFrom(dir, loaded, loaded.ConfigName);

            CollectionAssert.AreEqual(new[] { "c1" }, loaded.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
            Assert.IsTrue(loaded.Assignments["ImageRecognition"].IsEmpty, "an unassigned ability stays unassigned");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void AGridSavedBeforeGeneralIntelligence_KeepsItsConversationChain_AndDropsTheRowsFoldedIn()
    {
        var config = new AiConfig
        {
            Assignments =
            {
                ["Conversation"]   = AbilityChain.Of("claude", "gemma"),
                ["ProblemSolving"] = AbilityChain.Of("o3"),
                ["Analysis"]       = AbilityChain.Of("gemma"),
                ["Disambiguation"] = AbilityChain.Of("gemma"),
            },
        };

        Assert.IsTrue(config.FoldIntoGeneralIntelligence());

        CollectionAssert.AreEqual(new[] { "claude", "gemma" }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { "GeneralIntelligence", "Disambiguation" }, config.Assignments.Keys.ToArray(),
                                       "Problem Solving and Analysis go; the rest stays");
        Assert.IsFalse(config.FoldIntoGeneralIntelligence(), "a grid already folded is left as it is");
    }

    [TestMethod]
    public void AGridWithGeneralIntelligence_KeepsIt_OverAConversationRowLeftBehind()
    {
        var config = new AiConfig
        {
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("claude"), ["Conversation"] = AbilityChain.Of("ollama") },
        };

        Assert.IsTrue(config.FoldIntoGeneralIntelligence());

        CollectionAssert.AreEqual(new[] { "claude" }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
        Assert.IsFalse(config.Assignments.ContainsKey("Conversation"));
    }
}
